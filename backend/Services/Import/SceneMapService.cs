using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using JsonRepairSharp;
using PartyTown.Grains.Generation;

namespace PartyTown.Services.Import;

/// <summary>
/// The stateless map side of the import engine (ADR 0017): runs one scene's extraction
/// calls through the LLM router at General tier and returns typed items plus per-chunk
/// discards. Two ported paths: canon sections (dossier + recap chunks, split on the
/// author's seams) and message windows (overlapping, salience selection). No session
/// state is read or written here — same inputs, rerun freely.
/// </summary>
public interface ISceneMapService
{
    Task<SceneMapResult> RunAsync(SceneRunInput input, CancellationToken ct, Func<SceneMapProgress, Task>? onProgress = null);
}

/// <summary>Progress of one scene run: reported once up front (call 0 of N) and after
/// every extraction call, with the pre-fold items that call produced.</summary>
public sealed record SceneMapProgress(int CallsDone, int TotalCalls, string Stage, IReadOnlyList<MappedItem> NewItems);

/// <inheritdoc cref="ISceneMapService"/>
public sealed class SceneMapService(IGrainFactory grains, ILogger<SceneMapService> logger) : ISceneMapService
{
    // Window shape validated by the phase-3 probe: stride 4 keeps a 2-message overlap for
    // cross-window continuity without a running "story so far".
    private const int WindowSize = 6;
    private const int WindowOverlap = 2;
    private const int MaxMessageChars = 4_200; // per-message cap into the window prompt
    private const int Attempts = 3;

    public async Task<SceneMapResult> RunAsync(SceneRunInput input, CancellationToken ct, Func<SceneMapProgress, Task>? onProgress = null)
    {
        var items = new List<MappedItem>();
        var discards = new List<ChunkDiscard>();
        var degraded = new List<DegradedRecord>();
        var llmCalls = 0;
        var unparseable = 0;

        // ── canon path: dossier (opt-in) + recap chunks, split on author seams ──
        var sections = new List<CanonSection>();
        if (!string.IsNullOrWhiteSpace(input.SystemInstruction))
            sections.AddRange(CanonSectionSplitter.Split("systemInstruction", "dossier", input.SystemInstruction));
        foreach (var recap in input.Chunks.Where(c => c.Category == ImportChunkCategories.Recap))
            sections.AddRange(CanonSectionSplitter.Split(
                $"chunk[{recap.Index}]", "recap", recap.Text, new List<int> { recap.Index }));

        var registry = BuildRegistryBlock(input);

        // Windows are computed up front so progress can carry a real total from call 0.
        var messages = input.Chunks.Where(c => c.Category == ImportChunkCategories.Message).ToList();
        var messageIndices = messages.Select(m => m.Index).ToHashSet();
        var windows = BuildWindows(messages.Count, WindowSize, WindowOverlap);
        var totalCalls = sections.Count + windows.Count;

        if (onProgress is not null)
            await onProgress(new SceneMapProgress(0, totalCalls, "canon", Array.Empty<MappedItem>()));

        foreach (var section in sections)
        {
            ct.ThrowIfCancellationRequested();
            llmCalls++;
            var (raw, parsed) = await ClassifySectionAsync(section, input.Note, registry, ct);
            if (parsed?.Items is not { Count: > 0 })
            {
                unparseable++;
                degraded.Add(new DegradedRecord
                {
                    SourceId = section.Id,
                    Reason = "unparseable or empty items",
                    Detail = Truncate(raw, 200),
                });
                if (onProgress is not null)
                    await onProgress(new SceneMapProgress(llmCalls, totalCalls, "canon", Array.Empty<MappedItem>()));
                continue;
            }

            var sectionItems = parsed.Items.Select(i => new MappedItem
            {
                SourceId = section.Id,
                Type = i.Type?.ToLowerInvariant(),
                Persona = i.Persona,
                Summary = i.Summary,
                Weight = i.Weight,
                Participants = (i.Participants ?? new List<string>())
                    .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList(),
                Concepts = ToConceptDrafts(i.Concepts),
                SourceChunks = section.SourceChunks.ToList(),
            }).ToList();
            items.AddRange(sectionItems);
            if (onProgress is not null)
                await onProgress(new SceneMapProgress(llmCalls, totalCalls, "canon", sectionItems));
        }

        // ── message path: overlapping windows, salience selection ──
        for (var w = 0; w < windows.Count; w++)
        {
            ct.ThrowIfCancellationRequested();
            llmCalls++;
            var windowId = $"scene[{input.SceneId:N}]#w{w}";
            var user = BuildWindowPrompt(messages, windows[w]);
            var (raw, parsed) = await ClassifyWindowAsync(windowId, user, input.Note, registry, ct);
            if (parsed is null)
            {
                unparseable++;
                degraded.Add(new DegradedRecord
                {
                    SourceId = windowId,
                    Reason = "unparseable window output",
                    Detail = Truncate(raw, 200),
                });
                if (onProgress is not null)
                    await onProgress(new SceneMapProgress(llmCalls, totalCalls, "messages", Array.Empty<MappedItem>()));
                continue;
            }

            var windowItems = new List<MappedItem>();
            foreach (var ep in parsed.Episodes ?? new List<WindowEpisode>())
            {
                if (string.IsNullOrWhiteSpace(ep.Summary)) continue;
                windowItems.Add(new MappedItem
                {
                    SourceId = windowId,
                    Type = DraftItemTypes.Episode,
                    Persona = null,
                    Summary = ep.Summary!.Trim(),
                    Weight = ep.Weight,
                    Participants = (ep.Participants ?? new List<string>())
                        .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList(),
                    Concepts = ToConceptDrafts(ep.Concepts),
                    SourceChunks = (ep.SourceMessages ?? new List<int>())
                        .Where(messageIndices.Contains).Distinct().OrderBy(i => i).ToList(),
                });
            }
            items.AddRange(windowItems);
            foreach (var d in parsed.Discards ?? new List<WindowDiscard>())
                if (messageIndices.Contains(d.Index) && !string.IsNullOrWhiteSpace(d.Reason))
                    discards.Add(new ChunkDiscard { ChunkIndex = d.Index, Reason = d.Reason!.Trim() });
            if (onProgress is not null)
                await onProgress(new SceneMapProgress(llmCalls, totalCalls, "messages", windowItems));
        }

        logger.LogInformation(
            "Scene map {SceneId}: {Sections} sections + {Windows} windows → {Items} items, {Discards} discards, {Degraded} degraded",
            input.SceneId, sections.Count, windows.Count, items.Count, discards.Count, degraded.Count);

        return new SceneMapResult
        {
            Items = items,
            Discards = discards,
            Degraded = degraded,
            LlmCalls = llmCalls,
            UnparseableCalls = unparseable,
        };
    }

    private static List<ConceptDraft> ToConceptDrafts(List<ConceptRef>? refs)
        => (refs ?? new List<ConceptRef>())
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .Select(c => new ConceptDraft
            {
                Name = c.Name!.Trim(),
                Aliases = (c.Aliases ?? new List<string>())
                    .Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToList(),
            })
            .ToList();

    // ── canon path prompt (ported from the phase-1 probe) ────────────────────────

    private const string SectionSystemPrompt =
        """
        You convert ONE section of imported roleplay canon into structured memory items.

        The section comes from either a CHARACTER DOSSIER (author-written character source)
        or a STORY RECAP (the author's own compressed summary of past play).

        Item rules:
        - type = "trait"   → a timeless fact about ONE character (personality, occupation,
                             relationships-as-standing-facts, speech style). Set "persona"
                             to that character's name.
        - type = "episode" → something that HAPPENED: an occurrence with actors and a
                             consequence. "persona" = null. Keep items in story order.
        - type = "rule"    → formatting or roleplay instructions aimed at the AI (markup,
                             dialogue style, turn structure). "summary" = a short label.
                             Never convert instructions into traits.
        - summary: at most 320 characters of plain factual prose. Every claim must be
          traceable to the section text. Do NOT invent. PADDING IS WORSE THAN BREVITY.
        - weight: for an "episode", a number from 0 to 1 for how memorable the event is —
          how much it would stay with the people who lived it. Pivotal turning points,
          losses, betrayals, confessions, first meetings score high (0.8-0.9); routine
          logistics, small talk, passing background score low (0.2-0.3). SPREAD the scores
          across the whole range — do NOT cluster everything near 0.5. Use null for
          "trait" and "rule" items.
        - participants: proper-noun character names taking part in the item. Empty if none.
        - concepts: recurring named things (places, projects, works, organisations) as
          { "name", "aliases" }. Include an alias ONLY when the section itself states it,
          e.g. `Justin ("Marty")` → name "Justin", aliases ["Marty"]. Empty if none.
        - Split into MULTIPLE items when the section covers several distinct facts or
          events; a single item is fine for a short focused section. At most 8 items.
        - A section that is purely formatting/markup instructions → one "rule" item.
        """;

    private async Task<(string Raw, SectionItems? Parsed)> ClassifySectionAsync(
        CanonSection section, string? note, string registry, CancellationToken ct)
    {
        var user = new StringBuilder()
            .AppendLine($"SOURCE KIND: {(section.Kind == "dossier" ? "CHARACTER DOSSIER" : "STORY RECAP")}")
            .AppendLine($"SECTION HEADING: {section.Heading}");
        if (!string.IsNullOrWhiteSpace(note))
            user.AppendLine($"OPERATOR NOTE (context from the human curating this import): {note}");
        if (registry.Length > 0)
            user.AppendLine(registry);
        user.AppendLine()
            .AppendLine("SECTION TEXT:")
            .AppendLine(section.Text);

        var raw = await CompleteAsync(section.Id, SectionSystemPrompt, user.ToString(), SectionItemsSchema, ct);
        return (raw, TryParse<SectionItems>(raw, allowBareItemsArray: true));
    }

    // ── message path prompt (ported from the phase-3 probe, genericized) ─────────

    private const string MessageWindowSystemPrompt =
        """
        You read a short WINDOW of consecutive messages from a two-person roleplay chat and
        pick out only the moments worth REMEMBERING later — the judgement a person makes
        recalling a conversation a day afterwards, not a transcript.

        The chat has two sides. The side labelled "model" plays the story's protagonist in
        first person. The side labelled "user" is the human GAME-MASTER, who narrates the
        scene AND voices every OTHER character through quoted dialogue inside their turns.
        Attribute named characters yourself from the text — the speaker label only tells
        you which side typed the message, never who is talking inside it.

        Each message is prefixed with [#N]. Return STRICT JSON:

        - episodes: 0..N salient moments. MOST messages are dialogue filler and produce NO
          episode. Pick only what would genuinely stick; prefer FEWER, higher-signal
          episodes over covering everything. Do NOT emit one episode per message. PADDING IS
          WORSE THAN BREVITY. For each episode:
          - summary: ONE neutral, third-person sentence of what happened (max 40 words).
            Every claim traceable to the window text — never invent. Use character names,
            never the speaker labels.
          - participants: proper-noun character names centrally involved. Empty array if none.
          - concepts: recurring named things (places, projects, works, organisations) as
            { "name", "aliases" }. Include an alias ONLY when the text states it. Empty if none.
          - weight: 0.0-1.0 — how much this moment would stick a day later. 0.1-0.3 minor
            colour, 0.4-0.6 notable, 0.7-0.9 significant, 1.0 unforgettable. A revelation, a
            confrontation, a decision, a first meeting is high; small talk is low.
          - sourceMessages: the [#N] ids this episode is drawn from.
        - discards: messages that are pure OUT-OF-CHARACTER meta, formatting, or scene-setup
          with nothing memorable (e.g. "# Chat starts here"). Each { "index", "reason" }. Do
          NOT list ordinary dialogue here — unlisted messages are kept as replayable history
          by default; discards are only for genuinely non-story chatter.

        At most 6 episodes per window.
        """;

    private async Task<(string Raw, WindowItems? Parsed)> ClassifyWindowAsync(
        string windowId, string user, string? note, string registry, CancellationToken ct)
    {
        var prompt = string.IsNullOrWhiteSpace(note)
            ? user
            : $"OPERATOR NOTE (context from the human curating this import): {note}\n\n{user}";
        if (registry.Length > 0)
            prompt = $"{registry}\n{prompt}";
        var raw = await CompleteAsync(windowId, MessageWindowSystemPrompt, prompt, WindowItemsSchema, ct);
        return (raw, TryParse<WindowItems>(raw, allowBareItemsArray: false));
    }

    // ── registry hints (ADR 0017: optional context, nouns not narrative) ─────────

    private const int MaxRegistryChars = 4_000;

    /// <summary>Confirmed registry cast + concepts rendered as canonical-name hints.
    /// Empty string for a registry-less run — the map call is unchanged then.</summary>
    internal static string BuildRegistryBlock(SceneRunInput input)
    {
        if (input.Cast.Count == 0 && input.Concepts.Count == 0) return string.Empty;

        var sb = new StringBuilder()
            .AppendLine("REGISTRY (canonical names confirmed by the human curating this import — treat as hints):");
        if (input.Cast.Count > 0)
        {
            sb.AppendLine("Cast — use these exact canonical spellings for these characters:");
            foreach (var entry in input.Cast)
            {
                sb.Append("- ").Append(entry.Name);
                if (entry.Aliases.Count > 0)
                    sb.Append(" (also appears as: ").Append(string.Join(", ", entry.Aliases)).Append(')');
                if (entry.Routing == CastRoutingModes.Concept)
                    sb.Append(" — background character: reference as a concept, do NOT emit traits for them");
                sb.AppendLine();
            }
        }
        if (input.Concepts.Count > 0)
        {
            sb.AppendLine("Concepts — reuse these exact names for recurring named things instead of minting variants:");
            foreach (var concept in input.Concepts)
            {
                sb.Append("- ").Append(concept.Name);
                if (concept.Aliases.Count > 0)
                    sb.Append(" (also appears as: ").Append(string.Join(", ", concept.Aliases)).Append(')');
                sb.AppendLine();
            }
        }
        return Truncate(sb.ToString().TrimEnd(), MaxRegistryChars);
    }

    internal readonly record struct Window(int Start, int End);

    /// <summary>Overlapping windows over <paramref name="count"/> items. stride = size −
    /// overlap; the last window is extended to reach the end so no tail message is skipped.</summary>
    internal static List<Window> BuildWindows(int count, int size, int overlap)
    {
        var stride = Math.Max(1, size - overlap);
        var windows = new List<Window>();
        if (count <= 0) return windows;
        for (var start = 0; start < count; start += stride)
        {
            var end = Math.Min(start + size, count);
            windows.Add(new Window(start, end));
            if (end == count) break;
        }
        return windows;
    }

    private static string BuildWindowPrompt(List<ImportChunk> messages, Window window)
    {
        var sb = new StringBuilder().AppendLine("MESSAGE WINDOW (consecutive turns; [#N] is the message id):").AppendLine();
        for (var i = window.Start; i < window.End; i++)
        {
            var m = messages[i];
            sb.AppendLine($"[#{m.Index}] ({m.Role}):")
              .AppendLine(Truncate(m.Text.Trim(), MaxMessageChars))
              .AppendLine();
        }
        return sb.ToString();
    }

    // ── the actual call: route General, one-shot, retry transient failures ───────

    private async Task<string> CompleteAsync(
        string callId, string system, string user, JsonObject schema, CancellationToken ct)
    {
        var job = new LlmGenerationJob
        {
            Messages = new List<LlmChatMessage>
            {
                new() { Role = "system", Content = system },
                new() { Role = "user", Content = user },
            },
            JobComplexity = JobComplexity.General,
            ResponseFormat = schema.ToJsonString(),
        };

        // Transient provider failures (dropped streams, 429s) degrade the one call — the
        // conservation ledger accounts for it — instead of aborting the scene run.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var router = grains.GetGrain<ILlmRouterGrain>(0);
                var endpoint = await router.RouteAsync(JobComplexity.General, ct);
                var raw = (await endpoint.CompleteOneShotAsync(job, ct)).Trim();
                // reasoning models occasionally spend the whole turn thinking and stream
                // no content at all — that's transient, not an answer
                if (raw.Length == 0 && attempt < Attempts)
                {
                    logger.LogWarning("Import map call {CallId}: empty content, attempt {Attempt}", callId, attempt);
                    continue;
                }
                return raw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < Attempts)
            {
                logger.LogWarning(ex, "Import map call {CallId} failed, attempt {Attempt}", callId, attempt);
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Import map call {CallId} failed after {Attempts} attempts", callId, Attempts);
                return $"<llm-error: {ex.Message}>";
            }
        }
    }

    // ── response schemas (ported verbatim from the probes) ───────────────────────

    private static readonly JsonObject SectionItemsSchema = new()
    {
        ["type"] = "json_schema",
        ["json_schema"] = new JsonObject
        {
            ["name"] = "canon_section_items",
            ["strict"] = true,
            ["schema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["items"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["maxItems"] = 8,
                        ["items"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject
                            {
                                ["type"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("trait", "episode", "rule") },
                                ["persona"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                                ["summary"] = new JsonObject { ["type"] = "string" },
                                ["weight"] = new JsonObject { ["type"] = new JsonArray("number", "null") },
                                ["participants"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                                ["concepts"] = ConceptsSchema(),
                            },
                            ["required"] = new JsonArray("type", "persona", "summary", "weight", "participants", "concepts"),
                        },
                    },
                },
                ["required"] = new JsonArray("items"),
            },
        },
    };

    private static readonly JsonObject WindowItemsSchema = new()
    {
        ["type"] = "json_schema",
        ["json_schema"] = new JsonObject
        {
            ["name"] = "message_window_items",
            ["strict"] = true,
            ["schema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["episodes"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["maxItems"] = 6,
                        ["items"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject
                            {
                                ["summary"] = new JsonObject { ["type"] = "string" },
                                ["participants"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                                ["concepts"] = ConceptsSchema(),
                                ["weight"] = new JsonObject { ["type"] = "number" },
                                ["sourceMessages"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "integer" } },
                            },
                            ["required"] = new JsonArray("summary", "participants", "concepts", "weight", "sourceMessages"),
                        },
                    },
                    ["discards"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject
                            {
                                ["index"] = new JsonObject { ["type"] = "integer" },
                                ["reason"] = new JsonObject { ["type"] = "string" },
                            },
                            ["required"] = new JsonArray("index", "reason"),
                        },
                    },
                },
                ["required"] = new JsonArray("episodes", "discards"),
            },
        },
    };

    private static JsonObject ConceptsSchema() => new()
    {
        ["type"] = "array",
        ["items"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["name"] = new JsonObject { ["type"] = "string" },
                ["aliases"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
            },
            ["required"] = new JsonArray("name", "aliases"),
        },
    };

    // ── output types + lenient parse (probes' drift tolerance, kept intact) ──────

    private sealed record SectionItems([property: JsonPropertyName("items")] List<SectionItem>? Items);

    private sealed record SectionItem(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("persona")] string? Persona,
        [property: JsonPropertyName("summary")] string? Summary,
        [property: JsonPropertyName("weight")] double? Weight,
        [property: JsonPropertyName("participants")] List<string>? Participants,
        [property: JsonPropertyName("concepts")] List<ConceptRef>? Concepts);

    private sealed record WindowItems(
        [property: JsonPropertyName("episodes")] List<WindowEpisode>? Episodes,
        [property: JsonPropertyName("discards")] List<WindowDiscard>? Discards);

    private sealed record WindowEpisode(
        [property: JsonPropertyName("summary")] string? Summary,
        [property: JsonPropertyName("participants")] List<string>? Participants,
        [property: JsonPropertyName("concepts")] List<ConceptRef>? Concepts,
        [property: JsonPropertyName("weight")] double? Weight,
        [property: JsonPropertyName("sourceMessages")] List<int>? SourceMessages);

    private sealed record WindowDiscard(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("reason")] string? Reason);

    private sealed record ConceptRef(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("aliases")] List<string>? Aliases);

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private static T? TryParse<T>(string raw, bool allowBareItemsArray) where T : class
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        foreach (var candidate in new[] { raw, StripFences(raw), TryRepair(StripFences(raw)) })
        {
            if (candidate is null) continue;
            try
            {
                // models without enforced structured outputs drift between {"items":[...]}
                // and a bare [...] — accept both shapes on the section path
                if (allowBareItemsArray && candidate.TrimStart().StartsWith('[') && typeof(T) == typeof(SectionItems))
                    return new SectionItems(JsonSerializer.Deserialize<List<SectionItem>>(candidate, WebOptions)) as T;
                return JsonSerializer.Deserialize<T>(candidate, WebOptions);
            }
            catch { /* next candidate */ }
        }
        return null;
    }

    private static string? TryRepair(string s)
    {
        try { return JsonRepair.RepairJson(s, JsonRepair.InputType.LLM); }
        catch { return null; }
    }

    private static string StripFences(string raw)
    {
        var s = raw.Trim();
        if (!s.StartsWith("```")) return s;
        var firstNewline = s.IndexOf('\n');
        s = firstNewline >= 0 ? s[(firstNewline + 1)..] : s[3..];
        var closing = s.LastIndexOf("```", StringComparison.Ordinal);
        return (closing >= 0 ? s[..closing] : s).Trim();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
