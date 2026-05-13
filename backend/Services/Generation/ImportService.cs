using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using JsonRepairSharp;
using PartyTown.Grains.Generation;

namespace PartyTown.Services.Generation;

/// <summary>
/// LLM driver for the chat-import flow:
///   • <see cref="ExtractPersonasAsync"/> — map-reduce persona extraction. Window the
///     transcript, ask the cheap-tier model "who appears here?" per window in parallel,
///     then reduce the mention list + the original character-source doc into a
///     canonical persona roster via a single general-tier call.
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
                    .Take(3)
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
            "Persona extraction: {DistinctMentions} distinct name(s) collected from {TotalMentions} raw mention(s).",
            mentionsByName.Count,
            mentions.Count);

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
                "Persona extraction reduce returned empty (parsed null? {ParsedNull}). Raw output (truncated): {Raw}. Falling back to single Narrator stub.",
                parsed is null,
                raw.Length > 600 ? raw[..600] : raw);
            return [new ExtractedPersona { Name = "Narrator", Archetype = null, SystemPrompt = "A neutral narrator who describes the scene and events.", Bio = null }];
        }

        return parsed.Personas;
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
            return parsed;
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
            logger.LogError("{Label} JSON repair failed: {Error}. Raw (truncated): {Raw}",
                label, ex.Message, raw.Length > 400 ? raw[..400] : raw);
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
