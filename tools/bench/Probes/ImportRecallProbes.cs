using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PartyTown.Data;
using PartyTown.Model;
using PartyTown.Services.Memory;

namespace PartyTown.Bench.Probes;

/// <summary>
/// Import spearhead phase 2 (scaffolding, ADR-0013 pre-work, issue #116): seed the bench's
/// scratch AGE from the FROZEN phase-1 artifact (no LLM calls — the artifact IS the input),
/// then run the real recall hot path (<see cref="IMemoryRepository.RecallAsync"/>, the same
/// two-arm salience-ranked read ResponsePipeline calls) over the imported graph and observe.
///
/// Seeding mirrors the production write shapes exactly (Event / ABOUT / Concept / RECOLLECTS
/// property sets from <c>MemoryRepository</c>), except timestamps come from the artifact's
/// anchor+ordinal band instead of now — the one thing CaptureMomentAsync cannot do, and the
/// reason this probe writes raw Cypher via <c>bench.MemoryDb</c>. Persona traits are seeded
/// through the real repo write ADR-0016/#91 uses for persona-owned facts:
/// <see cref="IMemoryRepository.AppendIntrinsicStanceAsync"/> (self-targeted, neutral valence).
///
/// The falsifiable questions (observed, never asserted — ADR 0011):
///   1. What does top-N-recent return when 96 imported events share one ~16-hour band?
///   2. Does anchor+ordinal spacing preserve story order in recall?
///   3. Do recap-derived events drown — or get drowned by — organic-conversation-shaped
///      recollections captured "today"? (Simulated by seeding 3 fresh events post-import.)
///   4. What salience do imported memories arrive at, given the band anchor sits weeks in
///      the past and decay half-life is 7 days?
/// </summary>
public static class ImportRecallProbes
{
    // ── knobs (env) ──────────────────────────────────────────────────────────────
    // IMPORT_ARTIFACT  path to the frozen phase-1 Probe Artifact JSON
    //                  (default: the mistral-small-2603 canonical run)
    // RECALL_LIMIT     memories per recall call                        (default: 10)

    private const string DefaultArtifact =
        "bench-runs/Import_CanonSectionsToArtifact/20260715-211927.json";

    [Probe("Import spearhead phase 2 (scaffolding, #116): seed scratch AGE from the frozen phase-1 artifact (96 events / 7 personas / 45 concepts, NO LLM calls) at their anchor+ordinal timestamps, mirroring production vertex/edge shapes — Events with ABOUT→Participant + ABOUT→Concept (concept↔event linked by name-in-summary heuristic, probe-side scaffolding), RECOLLECTS per matched participant at the event's own ts, persona traits as Intrinsic self-Stances via the real AppendIntrinsicStanceAsync. Then run the REAL RecallAsync hot path over it: Lena about Mara, Justin about the Island arc, Denise about the Shower Protocol, an anchorless top-N-recent control, and the same Mara query again after 3 fresh 'organic' events land — does imported data survive a today-timestamped burst? Read recall.* for arm/salience/story-order (ordinals shown), seed.* for ingest warts (unmatched participants, concept-link counts), salience.note for the imported-memories-arrive-pre-decayed finding. Pure DB reads — no live session needed.", RequiresMemory = true)]
    public static async Task Import_SeedArtifactAndRecall(Bench bench)
    {
        var ct = bench.Cancellation;

        if (bench.MemoryDb is null)
        {
            bench.Observe("ABORT",
                "bench.MemoryDb is null — the AGE container did not start (no Docker?). "
                + "Seeding needs direct Cypher; nothing to observe.");
            return;
        }

        // ── 0. load the frozen artifact ──────────────────────────────────────────
        var path = ResolveFile(Environment.GetEnvironmentVariable("IMPORT_ARTIFACT") ?? DefaultArtifact);
        var limit = int.TryParse(Environment.GetEnvironmentVariable("RECALL_LIMIT"), out var l) && l > 0 ? l : 10;
        var (events, personas, concepts) = LoadFrozenArtifact(await File.ReadAllTextAsync(path, ct));
        bench.Observe("config", new
        {
            artifact = path,
            recallLimit = limit,
            events = events.Count,
            personas = personas.Keys.ToList(),
            concepts = concepts.Count,
            band = new { first = events.Min(e => e.At), last = events.Max(e => e.At) },
        });

        // ── 1. identity: deterministic ids so artifacts diff across runs ─────────
        var partyId = DeterministicGuid("import-party");
        var roomId = DeterministicGuid("import-room");
        var personaIds = personas.Keys.ToDictionary(n => n, n => DeterministicGuid($"persona:{n}"));
        bench.Observe("scope", new { partyId, roomId, personaIds });

        // ── 2. seed ───────────────────────────────────────────────────────────────
        var seeder = new Seeder(bench.MemoryDb, partyId, roomId, personaIds);
        await seeder.SeedAsync(events, concepts, ct);
        bench.Observe("seed.summary", new
        {
            seeder.EventsCreated,
            seeder.AboutParticipantEdges,
            seeder.AboutConceptEdges,
            seeder.RecollectionEdges,
            eventsWithNoRecollector = seeder.EventsWithNoRecollector,
        });
        bench.Observe("seed.unmatchedParticipants (raw string → occurrences; known warts: 'I', 'Host', malformed quotes)",
            seeder.Unmatched.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value));
        bench.Observe("seed.conceptLinkCounts (concept → events linked by name-in-summary; heuristic is PROBE scaffolding, not a production rule)",
            seeder.ConceptLinks.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value));

        // Persona traits → the real persona-owned-fact write path (#91 Intrinsic Stances).
        var traitCounts = new Dictionary<string, int>();
        foreach (var (name, traits) in personas)
        {
            foreach (var trait in traits)
            {
                await bench.Memory.AppendIntrinsicStanceAsync(
                    personaIds[name],
                    new StanceTargetSpec(StanceTargetKind.Self, null, null, null),
                    valence: 0.0,
                    reasoning: trait,
                    attribution: null,
                    ct);
            }
            traitCounts[name] = traits.Count;
        }
        bench.Observe("seed.traitsAsIntrinsicSelfStances (valence 0 — traits carry no feeling; the importer has no valence source)", traitCounts);

        // Ground truth read-back through the production graph read.
        var graph = await bench.Memory.GetPartyMemoryGraphAsync(partyId, ct);
        bench.Observe("seed.graphReadBack", new
        {
            nodes = graph.Nodes.GroupBy(n => n.Kind).ToDictionary(g => g.Key, g => g.Count()),
            links = graph.Links.GroupBy(li => li.Kind).ToDictionary(g => g.Key, g => g.Count()),
        });

        // ── 3. salience arithmetic the reader needs up front ─────────────────────
        var now = DateTimeOffset.UtcNow;
        var newestEv = events.OrderByDescending(e => e.At).First();
        var oldestEv = events.OrderBy(e => e.At).First();
        var weights = events.Select(e => e.Weight).ToList();
        bench.Observe("salience.note (imported weights now vary per validation 2 — not the flat default)", new
        {
            decayHalfLifeDays = SalienceMath.DecayHalfLifeDays,
            importedWeightRange = new { min = weights.Min(), max = weights.Max(), mean = Math.Round(weights.Average(), 3) },
            newestImportAgeDays = Math.Round((now - newestEv.At).TotalDays, 1),
            newestImportSalience = Math.Round(SalienceMath.Score(newestEv.Weight, newestEv.At, 0, null, now), 4),
            oldestImportSalience = Math.Round(SalienceMath.Score(oldestEv.Weight, oldestEv.At, 0, null, now), 4),
            freshOrganicSalience = Math.Round(SalienceMath.Score(0.7, now, 0, null, now), 4),
        });

        // ── 4. recall scenarios over the imported graph (pure DB, real hot path) ─
        var ordinalBySnippet = events.ToDictionary(e => e.Summary, e => (int?)e.Ordinal);

        var lena = personaIds["Dr. Lena Brandt"];
        var justin = personaIds["Justin"];
        var denise = personaIds["Denise"];
        var anne = personaIds["Anne"];

        const string maraQuery =
            "I keep thinking about Mara — what she said about my work back then, and her staying at the apartment.";
        await ObserveRecallAsync(bench, "recall.lena.aboutMara",
            lena, partyId, new[] { lena, justin, denise }, maraQuery, limit, ordinalBySnippet, ct);

        await ObserveRecallAsync(bench, "recall.justin.islandArc",
            justin, partyId, new[] { justin, denise },
            "Do you remember Schiermonnikoog? What actually happened on that island between us?",
            limit, ordinalBySnippet, ct);

        await ObserveRecallAsync(bench, "recall.denise.showerProtocol",
            denise, partyId, new[] { denise, anne },
            "Whatever happened to the Shower Protocol? Did anyone ever write it down properly?",
            limit, ordinalBySnippet, ct);

        // Anchorless control: empty cast + no matching tokens → the bare recency leg. THE
        // phase-2 question: what is "top-N-recent" when all 96 events share one time band?
        await ObserveRecallAsync(bench, "recall.control.topNRecent (anchorless — pure recency arm)",
            lena, partyId, Array.Empty<Guid>(), "hm.", limit, ordinalBySnippet, ct);

        // The Stance floor over imported traits — what '# Where you stand' would render.
        var stanceLines = await bench.Memory.RecallStancesAsync(
            partyId, lena, new[] { lena, justin, denise }, maraQuery, limit: 8, ct);
        bench.Observe("recall.lena.stanceFloor (# Where you stand — imported traits, all valence 0)",
            stanceLines.Select(s => new { s.Reasoning, s.Valence, s.Contrast }).ToList());

        // ── 5. organic overlay: 3 fresh events land "today", then re-ask about Mara ──
        var organic = new[]
        {
            (Summary: "Lena and Justin argued over coffee about whether the sprint board reflects reality.",
                Cast: new[] { "Dr. Lena Brandt", "Justin" }),
            (Summary: "Denise brought stroopwafels to the office and Lena took two without apology.",
                Cast: new[] { "Dr. Lena Brandt", "Denise" }),
            (Summary: "Lena mentioned she slept badly and cancelled her evening plans.",
                Cast: new[] { "Dr. Lena Brandt" }),
        };
        var ordinal = 9000;
        foreach (var (summary, cast) in organic)
        {
            // Organic captures arrive at model-judged weights (rubric 0.7–0.9) and now-stamps —
            // exactly what CaptureMomentAsync would write, minus the LLM.
            await seeder.SeedOrganicEventAsync(summary, cast, ordinal++, weight: 0.7, now, ct);
            ordinalBySnippet[summary] = null; // renders as "organic" in the observation
        }
        bench.Observe("organic.seeded", organic.Select(o => o.Summary).ToList());

        await ObserveRecallAsync(bench, "recall.lena.aboutMara.afterOrganicBurst",
            lena, partyId, new[] { lena, justin, denise }, maraQuery, limit, ordinalBySnippet, ct);
        await ObserveRecallAsync(bench, "recall.control.topNRecent.afterOrganicBurst",
            lena, partyId, Array.Empty<Guid>(), "hm.", limit, ordinalBySnippet, ct);

        bench.Observe("judge",
            "Q1 one-band top-N-recent: does recall.control.topNRecent return the LAST ~10 ordinals in "
            + "strictly descending story order (spacing preserved), or does same-band compression scramble it? "
            + "Q2 relevant arm: do the Mara/Island/Shower queries surface on-topic OLD events via the "
            + "concept/participant walk despite ~5-week decay, and does the 5-slot quota leave room for them? "
            + "Q3 drowning: after the organic burst, do imported events vanish from the recent arm (salience "
            + "~0.02 vs ~0.7) while the relevant arm still carries them — i.e. is the two-arm union the thing "
            + "that keeps imports alive at all? Q4 note the ingest warts for the ADR-0013 amendment: "
            + "unmatched participant strings, persons-as-concepts inflating concept links, dream-framed "
            + "events indistinguishable from actual ones in recall.");
    }

    // ── recall observation ───────────────────────────────────────────────────────

    private static async Task ObserveRecallAsync(
        Bench bench,
        string label,
        Guid personaId,
        Guid partyId,
        IReadOnlyList<Guid> presentIds,
        string anchorText,
        int limit,
        IReadOnlyDictionary<string, int?> ordinalBySnippet,
        CancellationToken ct)
    {
        var recalled = await bench.Memory.RecallAsync(personaId, partyId, presentIds, anchorText, limit, ct);
        var rows = recalled.Select((r, i) => new
        {
            n = i + 1,
            ordinal = ordinalBySnippet.TryGetValue(r.Snippet, out var o) ? (o?.ToString(CultureInfo.InvariantCulture) ?? "organic") : "?",
            arm = r.Arm.ToString(),
            salience = Math.Round(r.Salience, 4),
            capturedAt = r.CapturedAt,
            snippet = r.Snippet.Length > 120 ? r.Snippet[..120] + "…" : r.Snippet,
        }).ToList();

        var importedOrdinals = recalled
            .Select(r => ordinalBySnippet.TryGetValue(r.Snippet, out var o) ? o : null)
            .Where(o => o is not null)
            .Select(o => o!.Value)
            .ToList();
        var storyOrderPreserved = importedOrdinals.Count < 2
            || importedOrdinals.Zip(importedOrdinals.Skip(1), (a, b) => a > b).All(x => x);

        bench.Observe(label, new
        {
            anchorText,
            returned = recalled.Count,
            arms = recalled.GroupBy(r => r.Arm.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            importedOrdinalsNewestFirst = storyOrderPreserved,
            rows,
        });
    }

    // ── seeding ──────────────────────────────────────────────────────────────────

    /// <summary>Raw-Cypher seeder mirroring <c>MemoryRepository</c>'s write shapes (Event /
    /// ABOUT / Concept MERGE / RECOLLECTS / HAS_PARTICIPANT) with artifact timestamps. Reads
    /// still go through the real repo — this class only exists because the production write
    /// path stamps <c>now</c> and calls an LLM.</summary>
    private sealed class Seeder(
        IDbContextFactory<AppDbContext> dbFactory,
        Guid partyId,
        Guid roomId,
        IReadOnlyDictionary<string, Guid> personaIds)
    {
        public int EventsCreated { get; private set; }
        public int AboutParticipantEdges { get; private set; }
        public int AboutConceptEdges { get; private set; }
        public int RecollectionEdges { get; private set; }
        public int EventsWithNoRecollector { get; private set; }
        public Dictionary<string, int> Unmatched { get; } = new();
        public Dictionary<string, int> ConceptLinks { get; } = new();

        private readonly Dictionary<string, IReadOnlyList<string>> _personaTokens =
            personaIds.Keys.ToDictionary(n => n, Tokens);

        public async Task SeedAsync(
            IReadOnlyList<SeedEvent> events, IReadOnlyList<SeedConcept> concepts, CancellationToken ct)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await db.Database.OpenConnectionAsync(ct);
            try
            {
                foreach (var ev in events.OrderBy(e => e.Ordinal))
                {
                    var atIso = ev.At.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                    var eventId = DeterministicGuid($"event:{ev.Ordinal}");

                    await ExecAsync(db, $$"""
                        SELECT * FROM cypher('memory', $cy$
                          CREATE (e:Event {
                            event_id: '{{eventId}}',
                            description: {{CypherStr(ev.Summary)}},
                            created_at: '{{atIso}}',
                            party_id: '{{partyId}}',
                            room_id: '{{roomId}}',
                            anchor_message_id: {{ev.Ordinal.ToString(CultureInfo.InvariantCulture)}}
                          })
                          RETURN e.event_id
                        $cy$) AS (event_id ag_catalog.agtype)
                        """, ct);
                    EventsCreated++;

                    var matched = MatchCast(ev.Participants);
                    foreach (var personaId in matched)
                    {
                        await ExecAsync(db, $$"""
                            SELECT * FROM cypher('memory', $cy$
                              MATCH (e:Event {event_id: '{{eventId}}'})
                              MERGE (p:Participant {persona_id: '{{personaId}}', party_id: '{{partyId}}'})
                              CREATE (e)-[:ABOUT]->(p)
                              RETURN p.persona_id
                            $cy$) AS (persona_id ag_catalog.agtype)
                            """, ct);
                        AboutParticipantEdges++;

                        await AddRecollectionAsync(db, eventId, personaId, ev.Summary,
                            ev.Weight, atIso, ct);
                        RecollectionEdges++;
                    }
                    if (matched.Count == 0)
                        EventsWithNoRecollector++;

                    foreach (var concept in concepts)
                    {
                        if (!ConceptMatchesSummary(concept, ev.Summary))
                            continue;
                        var name = concept.Name.Trim().ToLowerInvariant();
                        await ExecAsync(db, $$"""
                            SELECT * FROM cypher('memory', $cy$
                              MATCH (e:Event {event_id: '{{eventId}}'})
                              MERGE (c:Concept {name: {{CypherStr(name)}}})
                              SET c.display = coalesce(c.display, {{CypherStr(concept.Name)}})
                              CREATE (e)-[:ABOUT]->(c)
                              RETURN c.name
                            $cy$) AS (concept_name ag_catalog.agtype)
                            """, ct);
                        AboutConceptEdges++;
                        ConceptLinks[concept.Name] = ConceptLinks.GetValueOrDefault(concept.Name) + 1;
                    }
                }
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }

        /// <summary>One fresh "organic-shaped" event: now-stamped, model-rubric weight — what a
        /// real CaptureMomentAsync write looks like next to the imported band.</summary>
        public async Task SeedOrganicEventAsync(
            string summary, IReadOnlyList<string> cast, int ordinal, double weight,
            DateTimeOffset at, CancellationToken ct)
        {
            var atIso = at.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            var eventId = DeterministicGuid($"organic:{ordinal}");

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await db.Database.OpenConnectionAsync(ct);
            try
            {
                await ExecAsync(db, $$"""
                    SELECT * FROM cypher('memory', $cy$
                      CREATE (e:Event {
                        event_id: '{{eventId}}',
                        description: {{CypherStr(summary)}},
                        created_at: '{{atIso}}',
                        party_id: '{{partyId}}',
                        room_id: '{{roomId}}',
                        anchor_message_id: {{ordinal.ToString(CultureInfo.InvariantCulture)}}
                      })
                      RETURN e.event_id
                    $cy$) AS (event_id ag_catalog.agtype)
                    """, ct);
                foreach (var name in cast)
                {
                    var personaId = personaIds[name];
                    await ExecAsync(db, $$"""
                        SELECT * FROM cypher('memory', $cy$
                          MATCH (e:Event {event_id: '{{eventId}}'})
                          MERGE (p:Participant {persona_id: '{{personaId}}', party_id: '{{partyId}}'})
                          CREATE (e)-[:ABOUT]->(p)
                          RETURN p.persona_id
                        $cy$) AS (persona_id ag_catalog.agtype)
                        """, ct);
                    await AddRecollectionAsync(db, eventId, personaId, summary, weight, atIso, ct);
                }
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }

        // Mirror of MemoryRepository.AddRecollectionAsync, ts/weight parameterised.
        private Task AddRecollectionAsync(
            AppDbContext db, Guid eventId, Guid personaId, string snippet, double weight,
            string tsIso, CancellationToken ct)
            => ExecAsync(db, $$"""
                SELECT * FROM cypher('memory', $cy$
                  MATCH (e:Event {event_id: '{{eventId}}'})
                  MERGE (persona:Persona {id: '{{personaId}}'})
                  MERGE (part:Participant {persona_id: '{{personaId}}', party_id: '{{partyId}}'})
                  MERGE (persona)-[:HAS_PARTICIPANT]->(part)
                  CREATE (part)-[:RECOLLECTS {
                    id: '{{Guid.NewGuid()}}',
                    snippet: {{CypherStr(snippet)}},
                    ts: '{{tsIso}}',
                    weight: {{Math.Clamp(weight, 0.0, 1.0).ToString("0.0###", CultureInfo.InvariantCulture)}},
                    recall_count: 0
                  }]->(e)
                  RETURN part.persona_id
                $cy$) AS (persona_id ag_catalog.agtype)
                """, ct);

        /// <summary>Artifact participant strings → persona ids. Sanitizes the known model warts
        /// (trailing commas, stray quotes, parentheticals), then folds by token-subset either way
        /// — the same rule phase 1 uses ("Lena" ⊆ "Dr. Lena Brandt"; "Lana Brandt" folds to
        /// "Lana", NOT to "Lena Brandt" — the typo wart is inherited on purpose). Unmatched
        /// strings are counted, never minted into vertices.</summary>
        private List<Guid> MatchCast(IReadOnlyList<string> participants)
        {
            var matched = new List<Guid>();
            foreach (var raw in participants)
            {
                var tokens = Tokens(raw);
                if (tokens.Count == 0)
                {
                    Unmatched[raw] = Unmatched.GetValueOrDefault(raw) + 1;
                    continue;
                }

                string? best = null;
                var bestOverlap = 0;
                var ambiguous = false;
                foreach (var (name, personaTokens) in _personaTokens)
                {
                    var subset = !tokens.Except(personaTokens).Any() || !personaTokens.Except(tokens).Any();
                    if (!subset) continue;
                    var overlap = tokens.Intersect(personaTokens).Count();
                    if (overlap == 0) continue;
                    if (overlap > bestOverlap)
                    {
                        best = name;
                        bestOverlap = overlap;
                        ambiguous = false;
                    }
                    else if (overlap == bestOverlap)
                    {
                        ambiguous = true;
                    }
                }

                if (best is null || ambiguous)
                {
                    Unmatched[raw] = Unmatched.GetValueOrDefault(raw) + 1;
                    continue;
                }
                var id = personaIds[best];
                if (!matched.Contains(id))
                    matched.Add(id);
            }
            return matched;
        }

        private static bool ConceptMatchesSummary(SeedConcept concept, string summary)
        {
            if (concept.Name.Length >= 3
                && summary.Contains(concept.Name, StringComparison.OrdinalIgnoreCase))
                return true;
            foreach (var alias in concept.Aliases)
            {
                if (alias.Length >= 3 && summary.Contains(alias, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static async Task ExecAsync(AppDbContext db, string sql, CancellationToken ct)
        {
            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    // ── frozen-artifact parsing ──────────────────────────────────────────────────

    private sealed record SeedEvent(
        int Ordinal, DateTimeOffset At, string Summary, double Weight, IReadOnlyList<string> Participants);

    private sealed record SeedConcept(string Name, IReadOnlyList<string> Aliases);

    private static (IReadOnlyList<SeedEvent> Events,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Personas,
        IReadOnlyList<SeedConcept> Concepts)
        LoadFrozenArtifact(string json)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement Data(string label)
        {
            foreach (var obs in doc.RootElement.GetProperty("Observations").EnumerateArray())
            {
                if (obs.GetProperty("Label").GetString() == label)
                    return obs.GetProperty("Data").Clone();
            }
            throw new KeyNotFoundException($"Observation '{label}' not found in the frozen artifact.");
        }

        var events = new List<SeedEvent>();
        foreach (var e in Data("artifact.events").EnumerateArray())
        {
            events.Add(new SeedEvent(
                e.GetProperty("ordinal").GetInt32(),
                e.GetProperty("at").GetDateTimeOffset(),
                e.GetProperty("summary").GetString() ?? "",
                // phase-1 artifacts before the weight field fall back to the flat default —
                // that's the pre-validation-2 behaviour, so old artifacts still replay unchanged.
                e.TryGetProperty("weight", out var w) && w.ValueKind == JsonValueKind.Number
                    ? w.GetDouble() : SalienceMath.DefaultWeight,
                e.GetProperty("participants").EnumerateArray()
                    .Select(p => p.GetString() ?? "").Where(p => p.Length > 0).ToList()));
        }

        var personas = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var prop in Data("artifact.personas").EnumerateObject())
        {
            personas[prop.Name] = prop.Value.EnumerateArray()
                .Select(t => t.GetString() ?? "").Where(t => t.Length > 0).ToList();
        }

        var concepts = new List<SeedConcept>();
        foreach (var c in Data("artifact.concepts").EnumerateArray())
        {
            var name = c.GetProperty("Name").GetString() ?? "";
            if (name.Length == 0) continue;
            concepts.Add(new SeedConcept(
                name,
                c.TryGetProperty("Aliases", out var al)
                    ? al.EnumerateArray().Select(a => a.GetString() ?? "").Where(a => a.Length > 0).ToList()
                    : new List<string>()));
        }

        return (events, personas, concepts);
    }

    // ── small helpers ────────────────────────────────────────────────────────────

    /// <summary>Deterministic ids: same artifact → same graph ids → recall observations diff
    /// cleanly across runs (the container is fresh each run, so collisions can't accumulate).</summary>
    private static Guid DeterministicGuid(string key)
        => new(MD5.HashData(Encoding.UTF8.GetBytes($"partytown-import-recall:{key}")));

    /// <summary>Name tokens for cast folding: parentheticals stripped (the '(\"D\")' wart),
    /// punctuation dropped, lowercased, honorific 'dr' and single letters removed (kills the
    /// bare-'I' wart).</summary>
    private static IReadOnlyList<string> Tokens(string name)
    {
        var stripped = Regex.Replace(name, @"\(.*?\)", " ");
        var sb = new StringBuilder(stripped.Length);
        foreach (var ch in stripped.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        return sb.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 2 && t != "dr")
            .ToList();
    }

    private static string ResolveFile(string path)
    {
        if (Path.IsPathRooted(path) || File.Exists(path))
            return path;
        // Walk up from cwd so `dotnet run --project tools/bench` works from repo root or below.
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, path);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return path; // let File.ReadAllTextAsync throw the honest error
    }

    // Copy of MemoryRepository.CypherStr (private there): single-quoted Cypher literal with
    // backslash escaping so artifact text can't terminate the literal or smuggle a $cy$ close.
    private static string CypherStr(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('\'');
        foreach (var c in s)
        {
            switch (c)
            {
                case '\'': sb.Append("\\'"); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('\'');
        return sb.ToString();
    }
}
