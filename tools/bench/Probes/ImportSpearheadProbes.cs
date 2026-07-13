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

    [Probe("Import spearhead (scaffolding): categorize-then-route an AI Studio export into a memory-seed artifact — no grains, no rooms, no AGE. Deterministic IR over all 316 Technokangs chunks (structured fields + one regex; expected counts observed vs actual), author-seam section split of systemInstruction + '# History' recaps, then ONE General-tier call per section returning typed items (trait|episode|rule). Routing is deterministic: traits → persona cards, episodes → Events at anchor+ordinal·spacing (env: IMPORT_ANCHOR / IMPORT_SPACING_MIN / IMPORT_SOURCES for subset-per-band imports), rules → discarded with reason, concepts merged by source-stated aliases only. Judge artifact.events for granularity/ordering coherence, artifact.personas for invention-free traits, cast.unmatched for the HITL surface ('Lana' should appear), conservation.* for silent-loss (must be none).")]
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
        var events = new List<object>();
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
                degraded.Add(new { section = section.Id, section.Heading, reason = "unparseable or empty items", rawLen = raw.Length });
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
                    case "trait" when !string.IsNullOrWhiteSpace(item.Persona):
                        if (!personas.TryGetValue(item.Persona, out var card))
                            personas[item.Persona] = card = [];
                        card.Add(item.Summary ?? "");
                        break;
                    case "trait":
                        degraded.Add(new { section = section.Id, reason = "trait without persona", item.Summary });
                        break;
                    case "episode":
                        events.Add(new
                        {
                            ordinal,
                            at = anchor + ordinal * spacing,
                            summary = item.Summary,
                            participants = item.Participants ?? [],
                            source = section.Id,
                        });
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

        // ── 4. the artifact — plus the two loss-surfaces a real import must show ─
        var knownNames = new HashSet<string>(personas.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var c in concepts) { knownNames.Add(c.Name); foreach (var al in c.Aliases) knownNames.Add(al); }

        bench.Observe("artifact.personas", personas.ToDictionary(kv => kv.Key, kv => kv.Value));
        bench.Observe("artifact.events", events);
        bench.Observe("artifact.concepts", concepts);
        bench.Observe("artifact.discarded", discarded);
        bench.Observe("artifact.degraded", degraded);
        // participants that no trait and no concept accounts for = the HITL review queue
        bench.Observe("cast.unmatched", participants.Where(p => !knownNames.Contains(p)).OrderBy(p => p).ToList());
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
            personas = personas.Count,
            events = events.Count,
            concepts = concepts.Count,
        });

        log.LogInformation("Import spearhead: {Sections} sections → {Events} events, {Personas} personas, {Degraded} degraded in {Ms}ms",
            sections.Count, events.Count, personas.Count, degraded.Count, stopwatch.ElapsedMilliseconds);
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

        var endpoint = await bench.Router.RouteAsync(JobComplexity.General, ct);
        var buffer = new StringBuilder();
        await foreach (var chunk in endpoint.GenerateAsync(job, ct))
        {
            if (chunk.Type == LlmGenerationEvent.ContentChunk)
                buffer.Append(chunk.Data);
        }
        var raw = buffer.ToString().Trim();
        return (raw, TryParse(raw));
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
                            ["required"] = new JsonArray("type", "persona", "summary", "participants", "concepts"),
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
        [property: JsonPropertyName("participants")] List<string>? Participants,
        [property: JsonPropertyName("concepts")] List<ConceptRef>? Concepts);

    private sealed record ConceptRef(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("aliases")] List<string>? Aliases);

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private static SectionItems? TryParse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return JsonSerializer.Deserialize<SectionItems>(raw, WebOptions); }
        catch { /* fall through */ }
        try
        {
            var s = StripFences(raw);
            try { return JsonSerializer.Deserialize<SectionItems>(s, WebOptions); }
            catch { return JsonSerializer.Deserialize<SectionItems>(JsonRepair.RepairJson(s, JsonRepair.InputType.LLM), WebOptions); }
        }
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
