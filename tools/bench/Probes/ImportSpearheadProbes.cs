using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using JsonRepairSharp;
using Microsoft.Extensions.Logging;
using PartyTown.Grains.Generation;

namespace PartyTown.Bench.Probes;

/// <summary>
/// Import-spearhead probe (scaffolding, pre-ADR-0013-rebuild): tests the
/// categorize-then-route import shape against the real Technokangs AI Studio export
/// WITHOUT touching grains, rooms, or AGE. Artifacts only.
///
/// Pipeline under test:
///   1. deterministic IR — every chunk categorized (thought / media / recap / message / empty)
///      by structured fields + one regex, never by LLM; conservation is observed.
///   2. section split — systemInstruction + recap chunks split on the author's own markdown
///      seams (headings, *** dividers). The human compressed the canon; we steal their seams.
///   3. one General-tier LLM call per section → typed items (trait | episode | rule).
///      The model judges CONTENT (what kind of fact, how many), never boundaries,
///      never timestamps, never identity.
///   4. deterministic routing — traits → persona cards, episodes → Events timestamped
///      anchor + ordinal·spacing (subset-per-import: one run = one band = one anchor),
///      rules → discarded WITH reason, concepts merged by source-stated aliases only.
///   5. deterministic identity + dedup — persona keys fold by token-subset after
///      honorific/parenthetical strip ("Lena" ⊆ "Dr. Lena Brandt"); near-identical names
///      (one token, edit distance ≤ 2) are NOT merged but surfaced as suspected typos;
///      episodes re-told by overlapping recap sections (shared participants,
///      word-overlap ≥ threshold) keep the first telling only.
///
/// The falsifiable question: do recap-derived and dossier-derived items come out at a
/// granularity that could coexist in one memory graph? Read artifact.events (count,
/// granularity, ordering) and artifact.personas (do dossier traits survive without
/// invention?) and judge.
/// </summary>
public static class ImportSpearheadProbes
{
    // ── knobs (env) ──────────────────────────────────────────────────────────────
    // IMPORT_FILE        path to the AI Studio export        (default: docs/Copy of Technokangs.json)
    // IMPORT_SOURCES     all | spec | recaps                 (default: all)
    // IMPORT_ANCHOR      ISO-8601 band anchor                (default: utcnow - 30d)
    // IMPORT_SPACING_MIN minutes between consecutive events  (default: 10)

    private const int MinSectionChars = 200;   // merge smaller sections into the previous one
    private const int MaxSectionChars = 8_000; // hard cap sent to the model (truncation observed)

    // Ground truth from the one-off jq analysis of this specific export — observed, not asserted,
    // so a parser regression is visible in the artifact instead of silently reshaping downstream.
    private static readonly Dictionary<string, int> ExpectedCategories = new()
    {
        ["thought"] = 93,
        ["media"] = 5,
        ["recap"] = 6,
        ["message"] = 212,
        ["chunks"] = 316,
    };

    [Probe("Import spearhead (scaffolding): categorize-then-route an AI Studio export into a memory-seed artifact — no grains, no rooms, no AGE. Deterministic IR over all 316 Technokangs chunks (structured fields + one regex; expected counts observed vs actual), author-seam section split of systemInstruction + '# History' recaps, then ONE General-tier call per section returning typed items (trait|episode|rule). Routing is deterministic: traits → persona cards, episodes → Events at anchor+ordinal·spacing (env: IMPORT_ANCHOR / IMPORT_SPACING_MIN / IMPORT_SOURCES for subset-per-band imports), rules → discarded with reason, concepts merged by source-stated aliases only. Persona keys fold deterministically (honorific/parenthetical strip + token-subset), cross-recap episode re-tellings dedup to the first telling. Judge artifact.events for granularity/ordering coherence, artifact.personas for invention-free traits, cast.unmatched + cast.suspectedTypos for the HITL surfaces (the 'Lana' typo should land in suspectedTypos, never silently merge), conservation.* for silent-loss (must be none).")]
    public static async Task Import_CanonSectionsToArtifact(Bench bench)
    {
        var ct = bench.Cancellation;
        var log = bench.Logger("import-spearhead");

        // ── 0. knobs ─────────────────────────────────────────────────────────────
        var filePath = ResolveFile(Environment.GetEnvironmentVariable("IMPORT_FILE") ?? "docs/Copy of Technokangs.json");
        var sourcesFilter = (Environment.GetEnvironmentVariable("IMPORT_SOURCES") ?? "all").ToLowerInvariant();
        var anchor = DateTimeOffset.TryParse(Environment.GetEnvironmentVariable("IMPORT_ANCHOR"), out var a)
            ? a : DateTimeOffset.UtcNow.AddDays(-30);
        var spacing = TimeSpan.FromMinutes(
            double.TryParse(Environment.GetEnvironmentVariable("IMPORT_SPACING_MIN"), out var m) ? m : 10);

        bench.Observe("config", new { file = filePath, sources = sourcesFilter, anchor, spacingMin = spacing.TotalMinutes });

        // ── 1. parse + deterministic IR ──────────────────────────────────────────
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(filePath, ct));
        var systemInstruction = doc.RootElement.GetProperty("systemInstruction").GetProperty("text").GetString() ?? "";
        var chunks = doc.RootElement.GetProperty("chunkedPrompt").GetProperty("chunks");

        var ir = new List<IrEntry>();
        var idx = 0;
        foreach (var chunk in chunks.EnumerateArray())
        {
            // `.text` is canonical — AI Studio in-app edits leave `parts` stale.
            var text = chunk.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            var role = chunk.TryGetProperty("role", out var r) ? r.GetString() ?? "?" : "?";
            var category =
                chunk.TryGetProperty("isThought", out var th) && th.ValueKind == JsonValueKind.True ? "thought"
                : (chunk.TryGetProperty("driveImage", out _) || chunk.TryGetProperty("driveAudio", out _)) && text.Length == 0 ? "media"
                : text.Length == 0 ? "empty"
                : RecapHeader.IsMatch(text) ? "recap"
                : "message";
            ir.Add(new IrEntry(idx++, role, category, text.Length, text));
        }

        var counts = ir.GroupBy(e => e.Category).ToDictionary(g => g.Key, g => g.Count());
        bench.Observe("ir.summary", new
        {
            actual = new { chunks = ir.Count, categories = counts },
            expected = ExpectedCategories,
            conservation = counts.Values.Sum() == ir.Count,
        });
        bench.Observe("ir.recaps", ir.Where(e => e.Category == "recap")
            .Select(e => new { e.Index, e.Chars, head = FirstLine(e.Text) }).ToList());

        // ── 2. section split on the author's own seams ───────────────────────────
        var sources = new List<(string Id, string Kind, string Text)>();
        if (sourcesFilter is "all" or "spec")
            sources.Add(("systemInstruction", "dossier", systemInstruction));
        if (sourcesFilter is "all" or "recaps")
            sources.AddRange(ir.Where(e => e.Category == "recap")
                .Select(e => ($"chunk[{e.Index}]", "recap", e.Text)));

        var sections = sources.SelectMany(s => SplitSections(s.Id, s.Kind, s.Text)).ToList();
        bench.Observe("sections.summary", new
        {
            total = sections.Count,
            bySource = sections.GroupBy(s => s.SourceId).ToDictionary(g => g.Key, g => g.Count()),
            minChars = sections.Min(s => s.Text.Length),
            maxChars = sections.Max(s => s.Text.Length),
            truncated = sections.Count(s => s.Truncated),
        });

        // ── 3. one LLM call per section — model judges content, nothing else ─────
        var personas = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var events = new List<EventCard>();
        var eventWords = new List<HashSet<string>>();
        var dedupedEvents = new List<object>();
        var concepts = new List<ConceptCard>();
        var discarded = new List<object>();
        var degraded = new List<object>();
        var participants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unparseableSections = 0;
        var ordinal = 0;
        var inChars = 0L;
        var outChars = 0L;
        var stopwatch = Stopwatch.StartNew();

        foreach (var section in sections)
        {
            ct.ThrowIfCancellationRequested();
            var (raw, parsed) = await ClassifySectionAsync(bench, section, ct);
            inChars += section.Text.Length;
            outChars += raw.Length;

            if (parsed?.Items is not { Count: > 0 })
            {
                unparseableSections++;
                degraded.Add(new { section = section.Id, section.Heading, reason = "unparseable or empty items", rawLen = raw.Length, rawHead = Truncate(raw, 200) });
                continue;
            }

            bench.Observe($"section.{section.Id}", new
            {
                section.SourceId, section.Heading, chars = section.Text.Length,
                items = parsed.Items.Select(i => new { i.Type, i.Persona, i.Summary }).ToList(),
            });

            foreach (var item in parsed.Items)
            {
                foreach (var p in item.Participants ?? []) participants.Add(p);
                foreach (var c in item.Concepts ?? []) MergeConcept(concepts, c);

                switch (item.Type?.ToLowerInvariant())
                {
                    case "trait" when string.IsNullOrWhiteSpace(item.Persona):
                        degraded.Add(new { section = section.Id, reason = "trait without persona", item.Summary });
                        break;
                    case "trait" when !LooksLikePersonaName(item.Persona!):
                        degraded.Add(new { section = section.Id, reason = "persona key not name-shaped", key = item.Persona, item.Summary });
                        break;
                    case "trait":
                        if (!personas.TryGetValue(item.Persona!, out var card))
                            personas[item.Persona!] = card = [];
                        card.Add(item.Summary ?? "");
                        break;
                    case "episode":
                        var words = ContentWords(item.Summary ?? "");
                        var dupOf = FindDuplicateEvent(events, eventWords, words, item.Participants, section.Id);
                        if (dupOf is not null)
                        {
                            dedupedEvents.Add(new
                            {
                                keptOrdinal = dupOf.Ordinal, keptSource = dupOf.Source, keptSummary = dupOf.Summary,
                                droppedSource = section.Id, droppedSummary = item.Summary,
                            });
                            break;
                        }
                        var eventWeight = item.Weight is { } w && w is >= 0.0 and <= 1.0 ? w : DefaultEpisodeWeight;
                        var eventConcepts = (item.Concepts ?? [])
                            .Select(c => c.Name?.Trim())
                            .Where(n => !string.IsNullOrWhiteSpace(n))
                            .Select(n => n!)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        events.Add(new EventCard(ordinal, anchor + ordinal * spacing, item.Summary ?? "",
                            eventWeight, (item.Participants ?? []).ToList(), eventConcepts, section.Id));
                        eventWords.Add(words);
                        ordinal++;
                        break;
                    case "rule":
                        discarded.Add(new { section = section.Id, reason = "agent-instruction", label = item.Summary });
                        break;
                    default:
                        degraded.Add(new { section = section.Id, reason = $"unknown type '{item.Type}'", item.Summary });
                        break;
                }
            }
        }
        stopwatch.Stop();

        // ── 4. deterministic identity fold — same person, several keys → one card ─
        // Token-subset after honorific/parenthetical strip merges "Lena" / "Lena Brandt" /
        // "Dr. Lena Brandt". Near-identical names ("Lana Brandt") deliberately do NOT merge:
        // a typo is a human call, so it lands in cast.suspectedTypos instead.
        var folds = BuildPersonaFolds(personas.Keys);
        var foldedPersonas = folds.ToDictionary(
            f => f.Canonical,
            f => f.Members.SelectMany(m => personas[m]).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            StringComparer.OrdinalIgnoreCase);

        // ── 5. the artifact — plus the loss-surfaces a real import must show ─────
        var knownNames = new HashSet<string>(personas.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var f in folds) knownNames.Add(f.Canonical);
        foreach (var c in concepts) { knownNames.Add(c.Name); foreach (var al in c.Aliases) knownNames.Add(al); }

        bench.Observe("artifact.personas", foldedPersonas);
        bench.Observe("personas.folds", folds.Where(f => f.Members.Count > 1)
            .Select(f => new { canonical = f.Canonical, merged = f.Members }).ToList());
        bench.Observe("artifact.events", events.Select(e => new
        {
            ordinal = e.Ordinal, at = e.At, summary = e.Summary, weight = e.Weight,
            participants = e.Participants.Select(p => Canonical(p, folds))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            concepts = e.Concepts,
            source = e.Source,
        }).ToList());
        ObserveWeightDistribution(bench, events);
        bench.Observe("artifact.dedupedEvents", dedupedEvents);
        bench.Observe("artifact.concepts", concepts);
        bench.Observe("artifact.discarded", discarded);
        bench.Observe("artifact.degraded", degraded);
        // participants that no trait and no concept accounts for = the HITL review queue
        bench.Observe("cast.unmatched", participants
            .Select(p => Canonical(p, folds))
            .Where(p => !knownNames.Contains(p))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToList());
        // near-identical names across cards and unclaimed participants = typo queue
        bench.Observe("cast.suspectedTypos", SuspectedTypos(folds, participants));
        bench.Observe("conservation.sections", new
        {
            sections = sections.Count,
            producedItems = sections.Count - unparseableSections,
            degradedEntries = degraded.Count,
            rule = "every section must appear in items, discarded, or degraded — nothing vanishes silently",
        });
        bench.Observe("ledger", new
        {
            llmCalls = sections.Count,
            elapsedMs = stopwatch.ElapsedMilliseconds,
            inChars, outChars,
            personas = foldedPersonas.Count,
            personaFolds = folds.Count(f => f.Members.Count > 1),
            events = events.Count,
            dedupedEvents = dedupedEvents.Count,
            concepts = concepts.Count,
            weightSpread = events.Count == 0 ? 0 : Math.Round(events.Max(e => e.Weight) - events.Min(e => e.Weight), 3),
            distinctWeights = events.Select(e => e.Weight).Distinct().Count(),
        });

        log.LogInformation("Import spearhead: {Sections} sections → {Events} events ({Deduped} deduped), {Personas} personas ({Folds} folded), {Degraded} degraded in {Ms}ms",
            sections.Count, events.Count, dedupedEvents.Count, foldedPersonas.Count, folds.Count(f => f.Members.Count > 1), degraded.Count, stopwatch.ElapsedMilliseconds);
    }

    // ── section splitting ────────────────────────────────────────────────────────

    private static readonly Regex RecapHeader = new(@"^\s*#+\s*(history|checkpoint)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeadingLine = new(@"^#{1,6}\s+\S", RegexOptions.Compiled);
    private static readonly Regex DividerLine = new(@"^\*{3,}\s*$", RegexOptions.Compiled);

    private sealed record IrEntry(int Index, string Role, string Category, int Chars, string Text);

    private sealed record Section(string Id, string SourceId, string Kind, string Heading, string Text, bool Truncated);

    /// <summary>Split on markdown headings and *** dividers — the seams the author already
    /// chose when compressing. Sections under <see cref="MinSectionChars"/> merge backward.</summary>
    private static List<Section> SplitSections(string sourceId, string kind, string text)
    {
        var rawSections = new List<(string Heading, StringBuilder Body)>();
        var current = (Heading: "(preamble)", Body: new StringBuilder());
        foreach (var line in text.Split('\n'))
        {
            if (HeadingLine.IsMatch(line) || DividerLine.IsMatch(line))
            {
                if (current.Body.Length > 0) rawSections.Add(current);
                var heading = DividerLine.IsMatch(line) ? "(divider)" : line.TrimStart('#', ' ').Trim();
                current = (heading, new StringBuilder());
                if (!DividerLine.IsMatch(line)) current.Body.AppendLine(line);
                continue;
            }
            current.Body.AppendLine(line);
        }
        if (current.Body.Length > 0) rawSections.Add(current);

        // merge tiny fragments backward so the model never sees a 3-line sliver alone
        var merged = new List<(string Heading, string Body)>();
        foreach (var (heading, body) in rawSections)
        {
            var trimmed = body.ToString().Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed.Length < MinSectionChars && merged.Count > 0)
                merged[^1] = (merged[^1].Heading, merged[^1].Body + "\n\n" + trimmed);
            else
                merged.Add((heading, trimmed));
        }

        return merged.Select((s, i) =>
        {
            var truncated = s.Body.Length > MaxSectionChars;
            var body = truncated ? s.Body[..MaxSectionChars] + "\n[…truncated]" : s.Body;
            return new Section($"{sourceId}#{i}", sourceId, kind, Truncate(s.Heading, 80), body, truncated);
        }).ToList();
    }

    // ── the one LLM judgment: section content → typed items ─────────────────────

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

    private static async Task<(string Raw, SectionItems? Parsed)> ClassifySectionAsync(Bench bench, Section section, CancellationToken ct)
    {
        var user = new StringBuilder()
            .AppendLine($"SOURCE KIND: {(section.Kind == "dossier" ? "CHARACTER DOSSIER" : "STORY RECAP")}")
            .AppendLine($"SECTION HEADING: {section.Heading}")
            .AppendLine()
            .AppendLine("SECTION TEXT:")
            .AppendLine(section.Text)
            .ToString();

        var job = new LlmGenerationJob
        {
            Messages = new List<LlmChatMessage>
            {
                new() { Role = "system", Content = SectionSystemPrompt },
                new() { Role = "user", Content = user },
            },
            JobComplexity = JobComplexity.General,
            ResponseFormat = SectionItemsSchema.ToJsonString(),
        };

        // Transient provider failures (dropped streams, 429s on free tiers) degrade the one
        // section — the conservation ledger accounts for it — instead of aborting the run.
        const int attempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var endpoint = await bench.Router.RouteAsync(JobComplexity.General, ct);
                var buffer = new StringBuilder();
                await foreach (var chunk in endpoint.GenerateAsync(job, ct))
                {
                    if (chunk.Type == LlmGenerationEvent.ContentChunk)
                        buffer.Append(chunk.Data);
                }
                var raw = buffer.ToString().Trim();
                // reasoning models occasionally spend the whole turn thinking and stream
                // no content at all — that's transient, not an answer
                if (raw.Length == 0 && attempt < attempts)
                {
                    bench.Observe($"section.{section.Id}.retry", new { attempt, error = "empty content" });
                    continue;
                }
                return (raw, TryParse(raw));
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < attempts)
            {
                bench.Observe($"section.{section.Id}.retry", new { attempt, error = ex.Message });
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                bench.Observe($"section.{section.Id}.failed", new { attempts, error = ex.Message });
                return ($"<llm-error: {ex.Message}>", null);
            }
        }
    }

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
                                ["concepts"] = new JsonObject
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
                                },
                            },
                            ["required"] = new JsonArray("type", "persona", "summary", "weight", "participants", "concepts"),
                        },
                    },
                },
                ["required"] = new JsonArray("items"),
            },
        },
    };

    // ── output types + lenient parse (mirrors ImportService.TryParse, kept local
    //    because LlmJsonParsing is internal to backend) ──────────────────────────

    private sealed record SectionItems([property: JsonPropertyName("items")] List<SectionItem>? Items);

    private sealed record SectionItem(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("persona")] string? Persona,
        [property: JsonPropertyName("summary")] string? Summary,
        [property: JsonPropertyName("weight")] double? Weight,
        [property: JsonPropertyName("participants")] List<string>? Participants,
        [property: JsonPropertyName("concepts")] List<ConceptRef>? Concepts);

    private sealed record ConceptRef(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("aliases")] List<string>? Aliases);

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private static SectionItems? TryParse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        foreach (var candidate in new[] { raw, StripFences(raw), TryRepair(StripFences(raw)) })
        {
            if (candidate is null) continue;
            // models without enforced structured outputs drift between {"items":[...]}
            // and a bare [...] — accept both shapes
            try
            {
                return candidate.TrimStart().StartsWith('[')
                    ? new SectionItems(JsonSerializer.Deserialize<List<SectionItem>>(candidate, WebOptions))
                    : JsonSerializer.Deserialize<SectionItems>(candidate, WebOptions);
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

    // ── deterministic identity fold + episode dedup ──────────────────────────────

    private sealed record EventCard(int Ordinal, DateTimeOffset At, string Summary, double Weight, List<string> Participants, List<string> Concepts, string Source);

    // Fallback when the model returns null/out-of-range weight for an episode — a neutral
    // mid-value. A run where this fires often is itself the degeneracy signal (weights.*).
    private const double DefaultEpisodeWeight = 0.5;

    private sealed record PersonaFold(string Canonical, List<string> Members, HashSet<string> Tokens);

    private static readonly Regex Parenthetical = new(@"\s*\([^)]*\)\s*", RegexOptions.Compiled);
    private static readonly Regex Honorific = new(@"^(dr|mr|mrs|ms|prof)\.?\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ContentWordRx = new(@"[a-z][a-z'-]{3,}", RegexOptions.Compiled);

    private static string NormalizeName(string name)
    {
        var s = Parenthetical.Replace(name, " ");
        s = Honorific.Replace(s.Trim(), "");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    private static HashSet<string> TokenSet(string name)
        => NormalizeName(name).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>A trait's persona field must look like a name, not a leaked trait sentence.</summary>
    private static bool LooksLikePersonaName(string key)
    {
        var tokens = TokenSet(key);
        return tokens.Count is >= 1 and <= 4 && NormalizeName(key).Length <= 48;
    }

    /// <summary>Fold persona keys whose token set is a subset of exactly one other key's
    /// ("Lena" ⊆ "Lena Brandt" ⊆ "Dr. Lena Brandt"). Ambiguous subsets stay separate.
    /// Canonical = most name tokens, then cleanest spelling ("Justin", not "Justin ('Marty')"),
    /// then longest.</summary>
    private static List<PersonaFold> BuildPersonaFolds(IEnumerable<string> keys)
    {
        var groups = new List<(HashSet<string> Tokens, List<string> Members)>();
        foreach (var key in keys.OrderByDescending(k => TokenSet(k).Count).ThenByDescending(k => k.Length))
        {
            var tokens = TokenSet(key);
            var hits = groups.Where(g => tokens.IsSubsetOf(g.Tokens)).ToList();
            if (hits.Count == 1)
                hits[0].Members.Add(key);
            else
                groups.Add((tokens, [key]));
        }
        return groups.Select(g => new PersonaFold(
            g.Members
                .OrderByDescending(m => TokenSet(m).Count)
                .ThenBy(m => m.Count(c => !char.IsLetter(c) && c != ' '))
                .ThenByDescending(m => m.Length)
                .First(),
            g.Members, g.Tokens)).ToList();
    }

    /// <summary>Map any name (participant, alias) onto its folded persona card, if one
    /// uniquely claims it; otherwise the name passes through untouched.</summary>
    private static string Canonical(string name, List<PersonaFold> folds)
    {
        var tokens = TokenSet(name);
        if (tokens.Count == 0) return name;
        var hits = folds.Where(f => tokens.IsSubsetOf(f.Tokens)).ToList();
        return hits.Count == 1 ? hits[0].Canonical : name;
    }

    /// <summary>Names (persona cards AND participants no card claims) where some token of
    /// one is a near-miss (edit distance ≤ 1) of a token of the other — "Lana" vs
    /// "Lena Brandt". Distance 1 keeps single-letter typos and drops different-name noise
    /// (Lana/Mara is distance 2). A typo is a human call, never merged.</summary>
    private static List<object> SuspectedTypos(List<PersonaFold> folds, IEnumerable<string> participants)
    {
        var suspects = new List<object>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Compare(string aName, HashSet<string> aTokens, string bName, HashSet<string> bTokens)
        {
            var pair = aTokens.Where(t => t.Length >= 3)
                .SelectMany(ta => bTokens.Where(t => t.Length >= 3), (ta, tb) => (ta, tb))
                .FirstOrDefault(p =>
                    !p.ta.Equals(p.tb, StringComparison.OrdinalIgnoreCase) &&
                    Levenshtein(p.ta, p.tb) <= 1);
            if (pair != default && seen.Add($"{aName}|{pair.ta}|{pair.tb}"))
                suspects.Add(new { a = aName, b = bName, tokens = new[] { pair.ta, pair.tb } });
        }

        for (var i = 0; i < folds.Count; i++)
            for (var j = i + 1; j < folds.Count; j++)
                Compare(folds[i].Canonical, folds[i].Tokens, folds[j].Canonical, folds[j].Tokens);

        // participants no card claims (Granite gave 'Lana' no card at all — the typo must
        // still surface); relation to a card in either subset direction means "claimed"
        var unclaimed = participants
            .Select(p => (Name: p, Tokens: TokenSet(p)))
            .Where(p => p.Tokens.Count > 0 &&
                !folds.Any(f => p.Tokens.IsSubsetOf(f.Tokens) || f.Tokens.IsSubsetOf(p.Tokens)));
        foreach (var p in unclaimed)
            foreach (var f in folds)
                Compare(f.Canonical, f.Tokens, p.Name, p.Tokens);

        return suspects;
    }

    private static int Levenshtein(string a, string b)
    {
        a = a.ToLowerInvariant(); b = b.ToLowerInvariant();
        var prev = Enumerable.Range(0, b.Length + 1).ToArray();
        var curr = new int[b.Length + 1];
        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
                curr[j] = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    // Recaps re-tell the same episode — across chunks AND across sections of one chunk
    // (verified against the 20260713 hy3 artifact: all three true re-tellings were
    // intra-chunk). The model can't see across sections, so dedup is deterministic:
    // different section + shared participant + word-overlap ≥ threshold ⇒ same
    // occurrence, keep the first telling. 0.25 caught 3/3 true dups, 0 false positives.
    private const double DupJaccard = 0.25;

    private static HashSet<string> ContentWords(string summary)
        => ContentWordRx.Matches(summary.ToLowerInvariant())
            .Select(m => Stem(m.Value)).ToHashSet(StringComparer.Ordinal);

    private static string Stem(string w) => w switch
    {
        _ when w.Length > 5 && w.EndsWith("ing", StringComparison.Ordinal) => w[..^3],
        _ when w.Length > 4 && w.EndsWith("ed", StringComparison.Ordinal) => w[..^2],
        _ when w.Length > 4 && w.EndsWith("s", StringComparison.Ordinal) && !w.EndsWith("ss", StringComparison.Ordinal) => w[..^1],
        _ => w,
    };

    private static EventCard? FindDuplicateEvent(
        List<EventCard> events, List<HashSet<string>> eventWords,
        HashSet<string> words, List<string>? itemParticipants, string sectionId)
    {
        var cand = (itemParticipants ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < events.Count; i++)
        {
            // events from the same section are already distinct — the model split them
            if (events[i].Source == sectionId) continue;
            if (cand.Count > 0 && !events[i].Participants.Any(cand.Contains)) continue;
            var inter = eventWords[i].Count(words.Contains);
            var union = eventWords[i].Count + words.Count - inter;
            if (union > 0 && (double)inter / union >= DupJaccard) return events[i];
        }
        return null;
    }

    // ── concept merge: source-stated aliases only, no LLM identity-guessing ─────

    private sealed class ConceptCard
    {
        public required string Name { get; set; }
        public HashSet<string> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int Mentions { get; set; }
    }

    private static void MergeConcept(List<ConceptCard> concepts, ConceptRef incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming.Name)) return;
        var names = new List<string> { incoming.Name };
        names.AddRange(incoming.Aliases?.Where(a => !string.IsNullOrWhiteSpace(a)) ?? []);

        var hit = concepts.FirstOrDefault(c =>
            names.Any(n => c.Name.Equals(n, StringComparison.OrdinalIgnoreCase) || c.Aliases.Contains(n)));
        if (hit is null)
        {
            hit = new ConceptCard { Name = incoming.Name };
            concepts.Add(hit);
        }
        hit.Mentions++;
        foreach (var n in names.Where(n => !n.Equals(hit.Name, StringComparison.OrdinalIgnoreCase)))
            hit.Aliases.Add(n);
    }

    // ── weight distribution (validation 2 — degeneracy is the only failure) ──────

    /// <summary>The degeneracy check: weights must SPREAD, not collapse to one value — uniform
    /// weights reproduce phase 2's failure where all recall ranking fell back to timestamps.
    /// Emits histogram + spread stats and the highest/lowest events so a reader can eyeball
    /// whether arc-critical beats (a suicide, a betrayal) outrank filler (a party invite).</summary>
    private static void ObserveWeightDistribution(Bench bench, List<EventCard> events)
    {
        if (events.Count == 0)
        {
            bench.Observe("weights.distribution", "(no episodes extracted)");
            return;
        }

        var weights = events.Select(e => e.Weight).ToList();
        var mean = weights.Average();
        var stddev = Math.Sqrt(weights.Sum(x => (x - mean) * (x - mean)) / weights.Count);
        var distinct = weights.Distinct().Count();

        // 0.0-0.1 … 0.9-1.0; the top bucket is closed so a weight of exactly 1.0 lands in it.
        var histogram = Enumerable.Range(0, 10).ToDictionary(
            b => $"{b / 10.0:0.0}-{(b + 1) / 10.0:0.0}",
            b => weights.Count(x => x >= b / 10.0 && (x < (b + 1) / 10.0 || (b == 9 && x <= 1.0))));

        bench.Observe("weights.distribution", new
        {
            count = weights.Count,
            min = weights.Min(),
            max = weights.Max(),
            mean = Math.Round(mean, 3),
            stddev = Math.Round(stddev, 3),
            distinctValues = distinct,
            atDefaultFallback = weights.Count(x => x == DefaultEpisodeWeight),
            degenerate = distinct <= 1,
            histogram,
        });

        string Snip(EventCard e) => e.Summary.Length > 90 ? e.Summary[..90] + "…" : e.Summary;
        bench.Observe("weights.spotCheck (highest / lowest — does arc-critical beat filler?)", new
        {
            highest = events.OrderByDescending(e => e.Weight).Take(6)
                .Select(e => new { e.Weight, summary = Snip(e) }).ToList(),
            lowest = events.OrderBy(e => e.Weight).Take(6)
                .Select(e => new { e.Weight, summary = Snip(e) }).ToList(),
        });
    }

    // ── misc ─────────────────────────────────────────────────────────────────────

    private static string ResolveFile(string path)
    {
        if (File.Exists(path)) return path;
        // bench may run from tools/bench — walk up looking for the repo-relative path
        var dir = Directory.GetCurrentDirectory();
        for (var i = 0; i < 5 && dir is not null; i++, dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(dir, path);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"Import source not found: {path}");
    }

    private static string FirstLine(string text)
    {
        var nl = text.IndexOf('\n');
        return Truncate((nl >= 0 ? text[..nl] : text).Trim(), 80);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
