using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using JsonRepairSharp;
using PartyTown.Grains.Generation;

namespace PartyTown.Services.ResponsePipeline;

/// <summary>
/// LLM driver for the chat-import flow:
///   • <see cref="ExtractPersonasAsync"/> — map-reduce persona extraction. Window the
///     transcript, ask the cheap-tier model "who appears here?" per window in parallel,
///     then reduce the mention list + the original character-source doc into a
///     canonical persona roster via a single general-tier call.
///   • <see cref="MergePersonasAsync"/> — collapse 2+ user-flagged persona stubs into
///     one canonical entry. Used by the review-personas UI to fix duplicates / name
///     confusion that the extractor produces (e.g. "Lena" + "Lena S." actually being
///     two different characters mis-grouped, or one character split across rows).
///   • <see cref="ClassifyChunkAsync"/> — split one transcript chunk into
///     per-character segments tagged by kind.
///
/// Map-reduce on extraction (vs. single-pass concat) catches characters that appear
/// past the first few KB of transcript, parallelizes across windows, and keeps the
/// per-window prompt small (no character-source doc on the hot path). The reduce
/// step is where the source doc is reintroduced and synthesis happens.
///
/// Mirrors the <see cref="PersonaDecisionService"/> idiom: route via
/// <see cref="ILlmRouterGrain"/>, consume the streaming generator to completion, parse
/// with the standard cleanup pipeline (<see cref="LlmJsonParsing.ExtractJsonPayload"/>
/// then <see cref="JsonRepair"/>), and fall back to a safe default on parse failure
/// rather than throwing.
/// </summary>
public sealed class ImportService(IGrainFactory grains, ILogger<ImportService> logger)
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private ILlmRouterGrain Router => grains.GetGrain<ILlmRouterGrain>(0);

    public async Task<IReadOnlyList<ExtractedPersona>> ExtractPersonasAsync(
        string systemInstructionText,
        IReadOnlyList<ImportSampleChunk>? sampleChunks,
        CancellationToken cancellationToken)
    {
        // Window the transcript at ~5 KB each, capping any single oversized chunk at
        // 8 KB. Typical exports of ~100 selected chunks produce ~20 windows; the map
        // step runs them in parallel via Parallel.ForEachAsync (max 5 in flight).
        var windows = BuildWindows(sampleChunks, targetCharsPerWindow: 5_000, maxWindowChars: 8_000);

        logger.LogInformation(
            "Persona extraction: mapping {WindowCount} window(s) from {ChunkCount} chunk(s).",
            windows.Count,
            sampleChunks?.Count ?? 0);

        var mentions = windows.Count == 0
            ? new List<Mention>()
            : await MapMentionsAsync(windows, cancellationToken);

        // Group mentions by case-insensitive name. Cap at top 40 most-mentioned to
        // keep the reduce prompt token budget bounded; long-tail names are usually
        // typos / mis-extractions and the merging rules already drop them.
        var mentionsByName = mentions
            .Where(m => !string.IsNullOrWhiteSpace(m.Name))
            .GroupBy(m => m.Name.Trim().ToLowerInvariant())
            .Select(g => new
            {
                name = g.First().Name.Trim(),
                count = g.Count(),
                evidence = g
                    .Select(m => m.Evidence)
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Distinct()
                    .Take(5)
                    .ToList(),
                role_hints = g
                    .Select(m => m.RoleHint)
                    .Where(h => !string.IsNullOrWhiteSpace(h))
                    .Select(h => h!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .ToList(),
            })
            .OrderByDescending(x => x.count)
            .Take(40)
            .ToList();

        logger.LogInformation(
            "Persona extraction: {DistinctMentions} distinct name(s) from {TotalMentions} raw mention(s). Names: {Names}",
            mentionsByName.Count,
            mentions.Count,
            string.Join(", ", mentionsByName.Select(x => $"{x.name}×{x.count}")));

        var mentionsJson = JsonSerializer.Serialize(mentionsByName, WebOptions);
        return await ReducePersonasAsync(systemInstructionText, mentionsJson, cancellationToken);
    }

    private async Task<List<Mention>> MapMentionsAsync(
        List<string> windows,
        CancellationToken cancellationToken)
    {
        var responseFormat = new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject
            {
                ["name"] = "mention_extraction",
                ["strict"] = true,
                ["schema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["mentions"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["name"] = new JsonObject { ["type"] = "string" },
                                    ["evidence"] = new JsonObject { ["type"] = "string" },
                                    ["role_hint"] = new JsonObject { ["type"] = new JsonArray("string", "null") }
                                },
                                ["required"] = new JsonArray("name", "evidence", "role_hint")
                            }
                        }
                    },
                    ["required"] = new JsonArray("mentions")
                }
            }
        };

        var results = new ConcurrentBag<Mention>();
        var opts = new ParallelOptions
        {
            MaxDegreeOfParallelism = 5,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(windows, opts, async (window, ct) =>
        {
            try
            {
                var messages = new List<LlmChatMessage>
                {
                    new() { Role = "system", Content = ImportPrompts.MentionExtractionSystem },
                    new() { Role = "user", Content = ImportPrompts.MentionExtractionUser(window) }
                };
                var raw = await RunStructuredAsync(messages, JobComplexity.CharacterThoughts, responseFormat, ct);
                var parsed = TryParse<MentionExtractionPayload>(raw, "MapMentions");
                if (parsed?.Mentions is null) return;
                foreach (var m in parsed.Mentions)
                {
                    results.Add(m);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Mention extraction failed for one window; skipping.");
            }
        });

        return results.ToList();
    }

    private async Task<IReadOnlyList<ExtractedPersona>> ReducePersonasAsync(
        string systemInstructionText,
        string mentionsJson,
        CancellationToken cancellationToken)
    {
        var messages = new List<LlmChatMessage>
        {
            new() { Role = "system", Content = ImportPrompts.PersonaExtractionSystem },
            new() { Role = "user", Content = ImportPrompts.PersonaExtractionUser(systemInstructionText, mentionsJson) }
        };

        var responseFormat = new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject
            {
                ["name"] = "extracted_personas",
                ["strict"] = true,
                ["schema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["personas"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["name"] = new JsonObject { ["type"] = "string" },
                                    ["archetype"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                                    ["system_prompt"] = new JsonObject { ["type"] = "string" },
                                    ["bio"] = new JsonObject { ["type"] = new JsonArray("string", "null") }
                                },
                                ["required"] = new JsonArray("name", "archetype", "system_prompt", "bio")
                            }
                        }
                    },
                    ["required"] = new JsonArray("personas")
                }
            }
        };

        var raw = await RunStructuredAsync(messages, JobComplexity.General, responseFormat, cancellationToken);
        var parsed = TryParse<ExtractedPersonasPayload>(raw, "ReducePersonas");

        if (parsed?.Personas is null || parsed.Personas.Count == 0)
        {
            logger.LogWarning(
                "Persona extraction reduce returned empty (parsed null? {ParsedNull}, rawLength={RawLength}). Falling back to single Narrator stub.",
                parsed is null,
                raw.Length);
            return [new ExtractedPersona { Name = "Narrator", Archetype = null, SystemPrompt = "A neutral narrator who describes the scene and events.", Bio = null }];
        }

        return parsed.Personas;
    }

    public async Task<ExtractedPersona> MergePersonasAsync(
        IReadOnlyList<ExtractedPersona> personas,
        CancellationToken cancellationToken)
    {
        if (personas.Count < 2)
        {
            throw new ArgumentException("Merge requires at least 2 personas.", nameof(personas));
        }

        var personasJson = JsonSerializer.Serialize(personas, WebOptions);

        var messages = new List<LlmChatMessage>
        {
            new() { Role = "system", Content = ImportPrompts.PersonaMergeSystem },
            new() { Role = "user", Content = ImportPrompts.PersonaMergeUser(personasJson) }
        };

        var responseFormat = new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject
            {
                ["name"] = "merged_persona",
                ["strict"] = true,
                ["schema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["name"] = new JsonObject { ["type"] = "string" },
                        ["archetype"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                        ["system_prompt"] = new JsonObject { ["type"] = "string" },
                        ["bio"] = new JsonObject { ["type"] = new JsonArray("string", "null") }
                    },
                    ["required"] = new JsonArray("name", "archetype", "system_prompt", "bio")
                }
            }
        };

        var raw = await RunStructuredAsync(messages, JobComplexity.General, responseFormat, cancellationToken);
        var parsed = TryParse<ExtractedPersona>(raw, "MergePersonas");

        if (parsed is null || string.IsNullOrWhiteSpace(parsed.Name))
        {
            // Fall back to the first input rather than failing the whole UI flow.
            // The user can re-trigger the merge or edit by hand.
            logger.LogWarning(
                "Persona merge returned empty (parsed null? {ParsedNull}). Raw output (truncated): {Raw}. Falling back to first input.",
                parsed is null,
                raw.Length > 600 ? raw[..600] : raw);
            return personas[0];
        }

        return parsed;
    }

    private static List<string> BuildWindows(
        IReadOnlyList<ImportSampleChunk>? chunks,
        int targetCharsPerWindow,
        int maxWindowChars)
    {
        var windows = new List<string>();
        if (chunks is null || chunks.Count == 0) return windows;

        var sb = new StringBuilder();
        foreach (var chunk in chunks)
        {
            var role = string.IsNullOrWhiteSpace(chunk.Role) ? "user" : chunk.Role;
            var text = chunk.Text ?? string.Empty;

            // Flush before adding if the current window already has content and the
            // next chunk would push us past target. A single oversized chunk gets its
            // own window (then immediately flushed) so it never blocks progress.
            if (sb.Length > 0 && sb.Length + text.Length > targetCharsPerWindow)
            {
                windows.Add(sb.ToString().TrimEnd());
                sb.Clear();
            }

            var body = text.Length > maxWindowChars ? text[..maxWindowChars] + "…" : text;
            sb.Append('[').Append(role).Append(']').Append('\n').Append(body).Append("\n\n");
        }

        if (sb.Length > 0) windows.Add(sb.ToString().TrimEnd());
        return windows;
    }

    /// <summary>
    /// Per-message mention extraction. One call = one msg = one window. The controller
    /// runs many of these concurrently (cap=5) for the live identify stream.
    /// </summary>
    public async Task<MentionExtractionPayload?> IdentifyMentionsForMsgAsync(
        string msgText,
        string role,
        CancellationToken cancellationToken)
    {
        var responseFormat = new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject
            {
                ["name"] = "mention_extraction",
                ["strict"] = true,
                ["schema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["mentions"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["name"] = new JsonObject { ["type"] = "string" },
                                    ["evidence"] = new JsonObject { ["type"] = "string" },
                                    ["role_hint"] = new JsonObject { ["type"] = new JsonArray("string", "null") }
                                },
                                ["required"] = new JsonArray("name", "evidence", "role_hint")
                            }
                        }
                    },
                    ["required"] = new JsonArray("mentions")
                }
            }
        };

        var safeRole = string.IsNullOrWhiteSpace(role) ? "user" : role;
        var window = $"[{safeRole}]\n{msgText}";

        var messages = new List<LlmChatMessage>
        {
            new() { Role = "system", Content = ImportPrompts.MentionExtractionSystem },
            new() { Role = "user", Content = ImportPrompts.MentionExtractionUser(window) }
        };

        try
        {
            var raw = await RunStructuredAsync(messages, JobComplexity.CharacterThoughts, responseFormat, cancellationToken);
            return TryParse<MentionExtractionPayload>(raw, "IdentifyMentionsForMsg");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Per-msg mention extraction failed; treating as empty.");
            return null;
        }
    }

    /// <summary>
    /// Single general-tier reduce step that collapses cross-msg name groups into a
    /// canonical character list (with name-variant merges). The controller post-
    /// processes the response to map mentions → canonical char ids by case-insensitive
    /// name match.
    /// </summary>
    public async Task<RosterMergeResult?> MergeRosterAsync(
        IReadOnlyList<MentionGroupDto> groups,
        CancellationToken cancellationToken)
    {
        if (groups.Count == 0)
        {
            return new RosterMergeResult();
        }

        var groupsJson = JsonSerializer.Serialize(groups, WebOptions);

        var responseFormat = new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject
            {
                ["name"] = "roster_merge",
                ["strict"] = true,
                ["schema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["characters"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["id"] = new JsonObject { ["type"] = "string" },
                                    ["primary_name"] = new JsonObject { ["type"] = "string" },
                                    ["names"] = new JsonObject
                                    {
                                        ["type"] = "array",
                                        ["items"] = new JsonObject { ["type"] = "string" }
                                    },
                                    ["archetype"] = new JsonObject { ["type"] = new JsonArray("string", "null") }
                                },
                                ["required"] = new JsonArray("id", "primary_name", "names", "archetype")
                            }
                        }
                    },
                    ["required"] = new JsonArray("characters")
                }
            }
        };

        var messages = new List<LlmChatMessage>
        {
            new() { Role = "system", Content = ImportPrompts.RosterMergeSystem },
            new() { Role = "user", Content = ImportPrompts.RosterMergeUser(groupsJson) }
        };

        var raw = await RunStructuredAsync(messages, JobComplexity.General, responseFormat, cancellationToken);
        var parsed = TryParse<RosterMergeResult>(raw, "MergeRoster");

        if (parsed?.Characters is null || parsed.Characters.Count == 0)
        {
            logger.LogWarning(
                "Roster merge returned empty (parsed null? {ParsedNull}). Raw output (truncated): {Raw}.",
                parsed is null,
                raw.Length > 600 ? raw[..600] : raw);
            return null;
        }

        return parsed;
    }

    /// <summary>
    /// Synthesize one character's prompt + bio from evidence + the source doc.
    /// Used by both the per-char reduce step in identify-ws and the standalone
    /// regenerate-char-detail endpoint.
    /// </summary>
    public async Task<CharDetailResult?> SynthesizeCharDetailAsync(
        string primaryName,
        IReadOnlyList<string> nameVariants,
        IReadOnlyList<string> evidence,
        string? archetype,
        string systemInstructionText,
        string mode,
        CancellationToken cancellationToken)
    {
        var safeMode = mode is "prompt" or "bio" or "both" ? mode : "both";

        var charPayload = JsonSerializer.Serialize(new
        {
            primary_name = primaryName,
            names = nameVariants,
            archetype,
            evidence,
        }, WebOptions);

        var responseFormat = new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject
            {
                ["name"] = "char_detail",
                ["strict"] = true,
                ["schema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["prompt"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                        ["bio"] = new JsonObject { ["type"] = new JsonArray("string", "null") }
                    },
                    ["required"] = new JsonArray("prompt", "bio")
                }
            }
        };

        var messages = new List<LlmChatMessage>
        {
            new() { Role = "system", Content = ImportPrompts.CharDetailSynthSystem },
            new() { Role = "user", Content = ImportPrompts.CharDetailSynthUser(charPayload, systemInstructionText, safeMode) }
        };

        try
        {
            var raw = await RunStructuredAsync(messages, JobComplexity.General, responseFormat, cancellationToken);
            return TryParse<CharDetailResult>(raw, "SynthesizeCharDetail");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Per-char detail synth failed for {Name}.", primaryName);
            return null;
        }
    }

    public async Task<ChunkClassification> ClassifyChunkAsync(
        string chunkText,
        string chunkRole,
        IReadOnlyList<PersonaRosterEntry> roster,
        CancellationToken cancellationToken)
    {
        var rosterJson = JsonSerializer.Serialize(roster, WebOptions);

        var messages = new List<LlmChatMessage>
        {
            new() { Role = "system", Content = ImportPrompts.ChunkClassifySystem },
            new() { Role = "user", Content = ImportPrompts.ChunkClassifyUser(rosterJson, chunkRole, chunkText) }
        };

        var responseFormat = new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject
            {
                ["name"] = "chunk_classification",
                ["strict"] = true,
                ["schema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["segments"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["persona_id"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                                    ["new_persona_name"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                                    ["text"] = new JsonObject { ["type"] = "string" },
                                    ["kind"] = new JsonObject
                                    {
                                        ["type"] = "string",
                                        ["enum"] = new JsonArray("dialogue", "action", "thought", "narration", "ooc")
                                    }
                                },
                                ["required"] = new JsonArray("persona_id", "new_persona_name", "text", "kind")
                            }
                        }
                    },
                    ["required"] = new JsonArray("segments")
                }
            }
        };

        try
        {
            var raw = await RunStructuredAsync(messages, JobComplexity.CharacterThoughts, responseFormat, cancellationToken);
            var parsed = TryParse<ChunkClassification>(raw, "ClassifyChunk");

            if (parsed?.Segments is null || parsed.Segments.Count == 0)
            {
                return UnattributedFallback(chunkText);
            }
            return parsed with { Segments = MergeMidSentenceFragments(parsed.Segments) };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Chunk classification failed; using unattributed fallback.");
            return UnattributedFallback(chunkText);
        }
    }

    private async Task<string> RunStructuredAsync(
        List<LlmChatMessage> messages,
        JobComplexity complexity,
        JsonObject responseFormat,
        CancellationToken cancellationToken)
    {
        var job = new LlmGenerationJob
        {
            Messages = messages,
            JobComplexity = complexity,
            ResponseFormat = responseFormat.ToJsonString()
        };

        var endpoint = await Router.RouteAsync(complexity, cancellationToken);
        var buffer = new StringBuilder();
        await foreach (var chunk in endpoint.GenerateAsync(job, cancellationToken))
        {
            if (chunk.Type == "message")
                buffer.Append(chunk.Data);
        }
        return buffer.ToString().Trim();
    }

    private T? TryParse<T>(string raw, string label) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(raw, WebOptions); }
        catch (Exception ex) { logger.LogDebug("{Label} direct parse failed: {Error}", label, ex.Message); }

        try
        {
            var cleaned = LlmJsonParsing.ExtractJsonPayload(raw);
            try { return JsonSerializer.Deserialize<T>(cleaned, WebOptions); }
            catch
            {
                var repaired = JsonRepair.RepairJson(cleaned, JsonRepair.InputType.LLM);
                return JsonSerializer.Deserialize<T>(repaired, WebOptions);
            }
        }
        catch (Exception ex)
        {
            logger.LogError("{Label} JSON repair failed: {Error}. RawLength={RawLength}",
                label, ex.Message, raw.Length);
            return null;
        }
    }

    private static ChunkClassification UnattributedFallback(string chunkText)
        => new()
        {
            Segments = [new ClassifiedSegment
            {
                PersonaId = null,
                NewPersonaName = "Unattributed",
                Text = chunkText,
                Kind = "narration"
            }]
        };

    /// <summary>
    /// Safety net for the small classifier that splits on commas / inside paired
    /// markup despite the prompt's "minimum granularity = one sentence" rule.
    /// Glues consecutive segments that share persona + kind when the previous one
    /// does not end at a sentence boundary, OR when it has unclosed italic markers
    /// (`*` or `_` count is odd). Preserves intentional sentence-boundary splits.
    /// </summary>
    private static List<ClassifiedSegment> MergeMidSentenceFragments(List<ClassifiedSegment> segments)
    {
        if (segments.Count <= 1) return segments;
        var merged = new List<ClassifiedSegment>(segments.Count);
        foreach (var seg in segments)
        {
            if (merged.Count == 0)
            {
                merged.Add(seg);
                continue;
            }
            var prev = merged[^1];
            if (ShouldGlue(prev, seg))
            {
                merged[^1] = prev with { Text = $"{prev.Text} {seg.Text}".Trim() };
            }
            else
            {
                merged.Add(seg);
            }
        }
        return merged;
    }

    private static bool ShouldGlue(ClassifiedSegment prev, ClassifiedSegment next)
    {
        var prevPersona = prev.PersonaId ?? prev.NewPersonaName ?? string.Empty;
        var nextPersona = next.PersonaId ?? next.NewPersonaName ?? string.Empty;
        if (!string.Equals(prevPersona, nextPersona, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(prev.Kind, next.Kind, StringComparison.OrdinalIgnoreCase)) return false;
        var prevText = prev.Text.TrimEnd();
        if (prevText.Length == 0) return true;
        return !EndsAtSentenceBoundary(prevText) || HasUnclosedInlineMarkers(prevText);
    }

    private static bool EndsAtSentenceBoundary(string text)
    {
        var last = text[^1];
        if (last is '.' or '!' or '?' or '…') return true;
        // Closing quote / italics / bracket — boundary only if preceded by a terminator.
        if (last is '"' or '”' or ')' or ']' or '*' or '_')
        {
            if (text.Length >= 2)
            {
                var penult = text[^2];
                return penult is '.' or '!' or '?' or '…';
            }
        }
        return false;
    }

    private static bool HasUnclosedInlineMarkers(string text)
    {
        var stars = 0;
        var unders = 0;
        foreach (var c in text)
        {
            if (c == '*') stars++;
            else if (c == '_') unders++;
        }
        return stars % 2 == 1 || unders % 2 == 1;
    }
}

/// <summary>One transcript chunk passed to <see cref="ImportService.ExtractPersonasAsync"/>
/// as additional grounding context. Used to expose actual dialogue and action prose
/// to the extractor — the <c>systemInstruction.text</c> on its own is often a thin
/// "you are a GM" frame that doesn't name the cast.</summary>
public sealed record class ImportSampleChunk
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "user";

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

public sealed record class MentionExtractionPayload
{
    [JsonPropertyName("mentions")]
    public List<Mention> Mentions { get; init; } = [];
}

public sealed record class Mention
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("evidence")]
    public string Evidence { get; init; } = string.Empty;

    [JsonPropertyName("role_hint")]
    public string? RoleHint { get; init; }
}

public sealed record class ExtractedPersonasPayload
{
    [JsonPropertyName("personas")]
    public List<ExtractedPersona> Personas { get; init; } = [];
}

public sealed record class ExtractedPersona
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("archetype")]
    public string? Archetype { get; init; }

    [JsonPropertyName("system_prompt")]
    public string SystemPrompt { get; init; } = string.Empty;

    [JsonPropertyName("bio")]
    public string? Bio { get; init; }
}

public sealed record class PersonaRosterEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("archetype")]
    public string? Archetype { get; init; }

    /// <summary>First ~200 chars of system_prompt — keeps the per-chunk prompt token
    /// budget bounded across hundreds of classification calls.</summary>
    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;
}

public sealed record class ChunkClassification
{
    [JsonPropertyName("segments")]
    public List<ClassifiedSegment> Segments { get; init; } = [];
}

public sealed record class ClassifiedSegment
{
    [JsonPropertyName("persona_id")]
    public string? PersonaId { get; init; }

    [JsonPropertyName("new_persona_name")]
    public string? NewPersonaName { get; init; }

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "narration";
}

/// <summary>One observed name across the transcript. The list of refs records every
/// (msgId, mentionId) pair where this name appeared, plus an evidence quote per ref.
/// Sent into the roster-merge LLM call — the model groups variants of the same person
/// into one canonical character.</summary>
public sealed record class MentionGroupDto
{
    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("refs")]
    public List<MentionRefDto> Refs { get; init; } = [];

    [JsonPropertyName("role_hints")]
    public List<string> RoleHints { get; init; } = [];
}

public sealed record class MentionRefDto
{
    [JsonPropertyName("msg_id")]
    public string MsgId { get; init; } = string.Empty;

    [JsonPropertyName("mention_id")]
    public string MentionId { get; init; } = string.Empty;

    [JsonPropertyName("evidence")]
    public string Evidence { get; init; } = string.Empty;

    [JsonPropertyName("role_hint")]
    public string? RoleHint { get; init; }
}

public sealed record class RosterMergeResult
{
    [JsonPropertyName("characters")]
    public List<RosterCharacter> Characters { get; init; } = [];
}

public sealed record class RosterCharacter
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("primary_name")]
    public string PrimaryName { get; init; } = string.Empty;

    [JsonPropertyName("names")]
    public List<string> Names { get; init; } = [];

    [JsonPropertyName("archetype")]
    public string? Archetype { get; init; }
}

public sealed record class CharDetailResult
{
    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }

    [JsonPropertyName("bio")]
    public string? Bio { get; init; }
}
