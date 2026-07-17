using System.Text.RegularExpressions;

namespace PartyTown.Services.Import;

/// <summary>
/// The reduce side of the import engine (ADR 0017): merges one scene's map output into
/// the session's accumulated draft. Pure state-in → state-out — no IO, no LLM, no clock
/// reads — so every merge rule is assertable in backend-test.
///
/// Rules ported from the spearhead probes: episode dedup by word-overlap + shared
/// participant (re-tellings keep the first telling), persona-key folding by token-subset
/// after honorific/parenthetical strip, concept merging by source-stated aliases only,
/// weight-floor routing marks, anchor + chunk-ordinal · spacing timestamps, and the
/// per-chunk conservation ledger (every chunk accounted, nothing vanishes silently).
/// </summary>
public static class ImportFold
{
    /// <summary>Tuned on the reference export: caught 3/3 true re-tellings, 0 false positives.</summary>
    public const double DupJaccard = 0.25;

    /// <summary>Fallback when the model returns a null/out-of-range episode weight.</summary>
    public const double DefaultEpisodeWeight = 0.5;

    private const int MaxSummaryChars = 500;

    // ── the incremental fold ─────────────────────────────────────────────────────

    /// <summary>
    /// Fold one scene run into the draft. Replaces that scene's existing slice first
    /// (side-effect-free regeneration); items owned by other scenes are never touched.
    /// </summary>
    public static SceneRunResult Apply(ImportSessionState state, Guid sceneId, SceneMapResult map, DateTimeOffset ranAt)
    {
        var scene = state.Scenes.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new KeyNotFoundException($"Scene {sceneId} not found.");
        if (scene.Committed)
            throw new InvalidOperationException(
                $"Scene {sceneId} is committed — rerun refused (regeneration is a draft-only power).");

        var replaced = state.Items.RemoveAll(i => i.SceneId == sceneId);
        state.RunRecords.RemoveAll(r => r.SceneId == sceneId);

        var degraded = new List<DegradedRecord>(map.Degraded);
        var deduped = new List<DedupDrop>();
        var accepted = new List<ImportDraftItem>();

        // Persona canonicalisation: fold incoming trait keys among themselves, then pin
        // each fold group to an existing draft canonical when exactly one claims it —
        // existing names stay stable so other scenes' items are never renamed.
        var incomingKeys = map.Items
            .Where(i => i.Type == DraftItemTypes.Trait && !string.IsNullOrWhiteSpace(i.Persona) && LooksLikePersonaName(i.Persona!))
            .Select(i => i.Persona!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var existingPersonas = state.Items
            .Where(i => i.Type == DraftItemTypes.Trait && !string.IsNullOrWhiteSpace(i.Persona))
            .Select(i => i.Persona!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var canonicalOf = BuildCanonicalMap(existingPersonas, incomingKeys);
        var knownPersonaNames = existingPersonas
            .Concat(canonicalOf.Values)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var item in map.Items)
        {
            var summary = Truncate((item.Summary ?? "").Trim(), MaxSummaryChars);
            if (summary.Length == 0)
            {
                degraded.Add(new DegradedRecord { SourceId = item.SourceId, Reason = "empty summary" });
                continue;
            }

            var participants = item.Participants
                .Select(p => Canonicalise(p, knownPersonaNames))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var conceptNames = item.Concepts
                .Select(c => MergeConcept(state.Concepts, c))
                .Where(n => n is not null)
                .Select(n => n!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            switch (item.Type)
            {
                case DraftItemTypes.Trait when string.IsNullOrWhiteSpace(item.Persona):
                    degraded.Add(new DegradedRecord { SourceId = item.SourceId, Reason = "trait without persona", Detail = summary });
                    break;

                case DraftItemTypes.Trait when !LooksLikePersonaName(item.Persona!):
                    degraded.Add(new DegradedRecord { SourceId = item.SourceId, Reason = "persona key not name-shaped", Detail = item.Persona });
                    break;

                case DraftItemTypes.Trait:
                {
                    var persona = canonicalOf.GetValueOrDefault(item.Persona!, item.Persona!);
                    var dup = state.Items.Concat(accepted).FirstOrDefault(x =>
                        x.Type == DraftItemTypes.Trait &&
                        string.Equals(x.Persona, persona, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(x.Summary, summary, StringComparison.OrdinalIgnoreCase));
                    if (dup is not null)
                    {
                        deduped.Add(Drop(dup, summary, item.SourceId));
                        break;
                    }
                    accepted.Add(Snapshot(new ImportDraftItem
                    {
                        Id = Guid.NewGuid(), SceneId = sceneId,
                        Type = DraftItemTypes.Trait, Persona = persona, Summary = summary,
                        Weight = 0, Participants = participants, Concepts = conceptNames,
                        SourceChunks = item.SourceChunks.ToList(), SourceId = item.SourceId,
                        Routing = DraftRouting.PersonaCard,
                        At = state.Settings.Anchor,
                    }));
                    break;
                }

                case DraftItemTypes.Episode:
                {
                    var weight = item.Weight is { } w && w is >= 0.0 and <= 1.0 ? w : DefaultEpisodeWeight;
                    var words = ContentWords(summary);
                    var dup = FindDuplicateEpisode(state.Items.Concat(accepted), words, participants, item.SourceId);
                    if (dup is not null)
                    {
                        deduped.Add(Drop(dup, summary, item.SourceId));
                        break;
                    }
                    var episode = new ImportDraftItem
                    {
                        Id = Guid.NewGuid(), SceneId = sceneId,
                        Type = DraftItemTypes.Episode, Persona = null, Summary = summary,
                        Weight = weight, Participants = participants, Concepts = conceptNames,
                        SourceChunks = item.SourceChunks.ToList(), SourceId = item.SourceId,
                        Routing = "", At = ItemTimestamp(state.Settings, item.SourceChunks),
                    };
                    ApplyFloor(episode, state.Settings.WeightFloor);
                    accepted.Add(Snapshot(episode));
                    break;
                }

                case DraftItemTypes.Rule:
                    accepted.Add(Snapshot(new ImportDraftItem
                    {
                        Id = Guid.NewGuid(), SceneId = sceneId,
                        Type = DraftItemTypes.Rule, Persona = null, Summary = summary,
                        Weight = 0, Participants = participants, Concepts = conceptNames,
                        SourceChunks = item.SourceChunks.ToList(), SourceId = item.SourceId,
                        Routing = DraftRouting.Discarded, RoutingReason = "agent-instruction",
                        At = state.Settings.Anchor,
                    }));
                    break;

                default:
                    degraded.Add(new DegradedRecord { SourceId = item.SourceId, Reason = $"unknown type '{item.Type}'", Detail = summary });
                    break;
            }
        }

        state.Items.AddRange(accepted);

        // A chunk that fed ANY extracted episode — even one later deduped — was judged
        // salient; the ledger books it as folded, not history-only.
        var salient = map.Items
            .Where(i => i.Type == DraftItemTypes.Episode)
            .SelectMany(i => i.SourceChunks)
            .Distinct().OrderBy(i => i).ToList();

        var record = new SceneRunRecord
        {
            SceneId = sceneId,
            RanAt = ranAt,
            LlmCalls = map.LlmCalls,
            UnparseableCalls = map.UnparseableCalls,
            LlmDiscards = map.Discards
                .GroupBy(d => d.ChunkIndex).Select(g => g.First()).ToList(),
            SalientChunks = salient,
            Deduped = deduped,
            Degraded = degraded,
        };
        state.RunRecords.Add(record);
        scene.RunCount++;
        scene.LastRunAt = ranAt;

        return new SceneRunResult
        {
            SceneId = sceneId,
            RanAt = ranAt,
            LlmCalls = map.LlmCalls,
            UnparseableCalls = map.UnparseableCalls,
            ReplacedItems = replaced,
            Items = accepted.ToList(),
            ChunkRoutings = BuildChunkRoutings(state, scene),
            Deduped = deduped.ToList(),
            Degraded = degraded.ToList(),
        };
    }

    /// <summary>Freeze the extractor's suggestion the moment the item enters the draft —
    /// the correction ledger diffs the human-final values against these at commit.</summary>
    private static ImportDraftItem Snapshot(ImportDraftItem item)
    {
        item.SuggestedSummary = item.Summary;
        item.SuggestedWeight = item.Weight;
        item.SuggestedRouting = item.Routing;
        return item;
    }

    private static DedupDrop Drop(ImportDraftItem kept, string droppedSummary, string droppedSourceId) => new()
    {
        KeptItemId = kept.Id,
        KeptSummary = kept.Summary,
        DroppedSummary = droppedSummary,
        DroppedSourceId = droppedSourceId,
    };

    // ── item edits + settings reapplication ──────────────────────────────────────

    /// <summary>Apply a human edit to one draft item. Routing flips are episode-only and
    /// set the override flag; "auto" clears it and falls back to the weight floor.</summary>
    public static ImportDraftItem Edit(ImportSessionState state, Guid itemId, DraftItemEdit edit)
    {
        var item = state.Items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new KeyNotFoundException($"Draft item {itemId} not found.");
        if (state.Scenes.Any(s => s.Id == item.SceneId && s.Committed))
            throw new InvalidOperationException(
                $"Draft item {itemId} belongs to a committed scene — committed scenes are settled.");

        if (edit.Summary is not null)
        {
            var summary = Truncate(edit.Summary.Trim(), MaxSummaryChars);
            if (summary.Length == 0) throw new ArgumentException("Summary cannot be empty.");
            item.Summary = summary;
        }

        if (edit.Weight is { } weight)
        {
            if (weight is < 0.0 or > 1.0) throw new ArgumentException("Weight must be between 0 and 1.");
            item.Weight = weight;
        }

        if (edit.Routing is not null)
        {
            if (item.Type != DraftItemTypes.Episode)
                throw new ArgumentException("Routing flips only apply to episodes.");
            switch (edit.Routing.ToLowerInvariant())
            {
                case DraftRouting.Event:
                    item.Routing = DraftRouting.Event;
                    item.RoutingReason = "manual";
                    item.RoutingOverridden = true;
                    break;
                case DraftRouting.History:
                    item.Routing = DraftRouting.History;
                    item.RoutingReason = "manual";
                    item.RoutingOverridden = true;
                    break;
                case "auto":
                    item.RoutingOverridden = false;
                    ApplyFloor(item, state.Settings.WeightFloor);
                    break;
                default:
                    throw new ArgumentException($"Unknown routing '{edit.Routing}' — use event, history or auto.");
            }
        }
        else if (edit.Weight is not null && item.Type == DraftItemTypes.Episode && !item.RoutingOverridden)
        {
            ApplyFloor(item, state.Settings.WeightFloor);
        }

        return item;
    }

    /// <summary>Re-derive floor routing (non-overridden episodes) and scheme timestamps
    /// after a settings change. Human overrides are left alone, and items of committed
    /// scenes are frozen — their timestamps/routing are already written into the Room/AGE.</summary>
    public static void ReapplySettings(ImportSessionState state)
    {
        var committedScenes = state.Scenes.Where(s => s.Committed).Select(s => s.Id).ToHashSet();
        foreach (var item in state.Items)
        {
            if (committedScenes.Contains(item.SceneId)) continue;
            if (item.Type == DraftItemTypes.Episode)
            {
                item.At = ItemTimestamp(state.Settings, item.SourceChunks);
                if (!item.RoutingOverridden)
                    ApplyFloor(item, state.Settings.WeightFloor);
            }
            else
            {
                item.At = state.Settings.Anchor;
            }
        }
    }

    private static void ApplyFloor(ImportDraftItem episode, double floor)
    {
        if (episode.Weight < floor)
        {
            episode.Routing = DraftRouting.History;
            episode.RoutingReason = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"below floor ({episode.Weight:0.##} < {floor:0.##})");
        }
        else
        {
            episode.Routing = DraftRouting.Event;
            episode.RoutingReason = null;
        }
    }

    private static DateTimeOffset ItemTimestamp(ImportSettings settings, List<int> sourceChunks)
        => sourceChunks.Count == 0
            ? settings.Anchor
            : settings.Anchor + TimeSpan.FromMinutes(settings.SpacingMinutes * sourceChunks.Min());

    // ── conservation ledger (derived live — flips and reruns can never desync it) ─

    public static ImportLedger BuildLedger(ImportSessionState state)
    {
        var chunks = state.Chunks.Select(c => ResolveChunk(state, c)).ToList();
        var byDisposition = chunks
            .GroupBy(c => c.Disposition)
            .ToDictionary(g => g.Key, g => g.Count());
        return new ImportLedger
        {
            TotalChunks = state.Chunks.Count,
            ByDisposition = byDisposition,
            Reconciles = byDisposition.Values.Sum() == state.Chunks.Count,
            Chunks = chunks,
        };
    }

    public static List<ChunkRouting> BuildChunkRoutings(ImportSessionState state, ImportScene scene)
        => state.Chunks
            .Where(c => c.Index >= scene.FromChunk && c.Index <= scene.ToChunk)
            .Select(c => ResolveChunk(state, c))
            .ToList();

    private static ChunkRouting ResolveChunk(ImportSessionState state, ImportChunk chunk)
    {
        var runScenes = state.Scenes
            .Where(s => s.FromChunk <= chunk.Index && chunk.Index <= s.ToChunk &&
                        state.RunRecords.Any(r => r.SceneId == s.Id))
            .ToList();
        if (runScenes.Count == 0)
            return Routing(chunk, ChunkDispositions.Unprocessed, null, null);

        var sceneId = runScenes[0].Id;
        switch (chunk.Category)
        {
            case ImportChunkCategories.Thought:
            case ImportChunkCategories.Media:
            case ImportChunkCategories.Empty:
                return Routing(chunk, ChunkDispositions.Discarded, chunk.Category, sceneId);
            case ImportChunkCategories.Recap:
                return Routing(chunk, ChunkDispositions.Folded, "canon-extracted", sceneId);
        }

        var touching = state.Items
            .Where(i => i.Type == DraftItemTypes.Episode && i.SourceChunks.Contains(chunk.Index))
            .ToList();
        if (touching.Any(i => i.Routing == DraftRouting.Event))
            return Routing(chunk, ChunkDispositions.EventRouted, null, sceneId);

        var records = runScenes
            .Select(s => state.RunRecords.First(r => r.SceneId == s.Id))
            .ToList();
        if (records.Any(r => r.SalientChunks.Contains(chunk.Index)))
            return Routing(chunk, ChunkDispositions.Folded, "fed episode", sceneId);

        var discard = records
            .SelectMany(r => r.LlmDiscards)
            .FirstOrDefault(d => d.ChunkIndex == chunk.Index);
        if (discard is not null)
            return Routing(chunk, ChunkDispositions.Discarded, discard.Reason, sceneId);

        return Routing(chunk, ChunkDispositions.HistoryOnly, null, sceneId);
    }

    private static ChunkRouting Routing(ImportChunk chunk, string disposition, string? reason, Guid? sceneId) => new()
    {
        ChunkIndex = chunk.Index,
        Category = chunk.Category,
        Disposition = disposition,
        Reason = reason,
        SceneId = sceneId,
    };

    // ── episode dedup (ported: word-overlap + shared participant, first telling wins) ─

    private static ImportDraftItem? FindDuplicateEpisode(
        IEnumerable<ImportDraftItem> candidates, HashSet<string> words,
        List<string> participants, string sourceId)
    {
        var cand = participants.SelectMany(TokenSet).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var prior in candidates)
        {
            if (prior.Type != DraftItemTypes.Episode) continue;
            // items from the same extraction call are already distinct — the model split them
            if (prior.SourceId == sourceId) continue;
            var priorTokens = prior.Participants.SelectMany(TokenSet).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (cand.Count > 0 && priorTokens.Count > 0 && !cand.Overlaps(priorTokens)) continue;
            if (Jaccard(ContentWords(prior.Summary), words) >= DupJaccard) return prior;
        }
        return null;
    }

    // ── persona identity fold (ported: token-subset after honorific/parenthetical strip) ─

    private sealed record PersonaFold(string Canonical, List<string> Members, HashSet<string> Tokens);

    private static readonly Regex Parenthetical = new(@"\s*\([^)]*\)\s*", RegexOptions.Compiled);
    private static readonly Regex Honorific = new(@"^(dr|mr|mrs|ms|prof)\.?\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ContentWordRx = new(@"[a-z][a-z'-]{3,}", RegexOptions.Compiled);

    internal static string NormalizeName(string name)
    {
        var s = Parenthetical.Replace(name, " ");
        s = Honorific.Replace(s.Trim(), "");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    internal static HashSet<string> TokenSet(string name)
        => NormalizeName(name).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>A trait's persona field must look like a name, not a leaked trait sentence.</summary>
    internal static bool LooksLikePersonaName(string key)
    {
        var tokens = TokenSet(key);
        return tokens.Count is >= 1 and <= 4 && NormalizeName(key).Length <= 48;
    }

    /// <summary>Fold persona keys whose token set is a subset of exactly one other key's
    /// ("Lena" ⊆ "Lena Brandt" ⊆ "Dr. Lena Brandt"). Ambiguous subsets stay separate.
    /// Canonical = most name tokens, then cleanest spelling, then longest.</summary>
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
                groups.Add((tokens, new List<string> { key }));
        }
        return groups.Select(g => new PersonaFold(
            g.Members
                .OrderByDescending(m => TokenSet(m).Count)
                .ThenBy(m => m.Count(c => !char.IsLetter(c) && c != ' '))
                .ThenByDescending(m => m.Length)
                .First(),
            g.Members, g.Tokens)).ToList();
    }

    /// <summary>
    /// incoming key → final canonical name. Incoming keys fold among themselves first;
    /// a fold group whose tokens relate by subset to exactly one existing draft canonical
    /// adopts that existing name, so canonicalisation never renames other scenes' items.
    /// </summary>
    internal static Dictionary<string, string> BuildCanonicalMap(
        IReadOnlyList<string> existingCanonicals, IReadOnlyList<string> incomingKeys)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fold in BuildPersonaFolds(incomingKeys))
        {
            var hits = existingCanonicals
                .Where(e =>
                {
                    var et = TokenSet(e);
                    return et.IsSubsetOf(fold.Tokens) || fold.Tokens.IsSubsetOf(et);
                })
                .ToList();
            // New canonicals are display-normalized ("Dr. Lena Brandt" → "Lena Brandt");
            // adopting an existing draft canonical keeps it verbatim for stability.
            var canonical = hits.Count == 1 ? hits[0] : NormalizeName(fold.Canonical);
            foreach (var member in fold.Members)
                map[member] = canonical;
        }
        return map;
    }

    /// <summary>Map a participant/alias name onto a canonical persona name when exactly
    /// one claims its tokens; otherwise the name passes through untouched.</summary>
    internal static string Canonicalise(string name, IReadOnlyList<string> canonicalNames)
    {
        var tokens = TokenSet(name);
        if (tokens.Count == 0) return name.Trim();
        var hits = canonicalNames.Where(c => tokens.IsSubsetOf(TokenSet(c))).ToList();
        return hits.Count == 1 ? hits[0] : name.Trim();
    }

    // ── concept merge: source-stated aliases only, no LLM identity-guessing ──────

    /// <summary>Merge one incoming concept into the session list; returns the canonical
    /// concept name, or null for a blank incoming name.</summary>
    internal static string? MergeConcept(List<ImportConcept> concepts, ConceptDraft incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming.Name)) return null;
        var names = new List<string> { incoming.Name };
        names.AddRange(incoming.Aliases.Where(a => !string.IsNullOrWhiteSpace(a)));

        var hit = concepts.FirstOrDefault(c =>
            names.Any(n => c.Name.Equals(n, StringComparison.OrdinalIgnoreCase) ||
                           c.Aliases.Contains(n, StringComparer.OrdinalIgnoreCase)));
        if (hit is null)
        {
            hit = new ImportConcept { Name = incoming.Name };
            concepts.Add(hit);
        }
        hit.Mentions++;
        foreach (var n in names.Where(n =>
                     !n.Equals(hit.Name, StringComparison.OrdinalIgnoreCase) &&
                     !hit.Aliases.Contains(n, StringComparer.OrdinalIgnoreCase)))
            hit.Aliases.Add(n);
        return hit.Name;
    }

    // ── word overlap ─────────────────────────────────────────────────────────────

    internal static HashSet<string> ContentWords(string summary)
        => ContentWordRx.Matches(summary.ToLowerInvariant())
            .Select(m => Stem(m.Value)).ToHashSet(StringComparer.Ordinal);

    private static string Stem(string w) => w switch
    {
        _ when w.Length > 5 && w.EndsWith("ing", StringComparison.Ordinal) => w[..^3],
        _ when w.Length > 4 && w.EndsWith("ed", StringComparison.Ordinal) => w[..^2],
        _ when w.Length > 4 && w.EndsWith("s", StringComparison.Ordinal) && !w.EndsWith("ss", StringComparison.Ordinal) => w[..^1],
        _ => w,
    };

    internal static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var inter = a.Count(b.Contains);
        var union = a.Count + b.Count - inter;
        return union == 0 ? 0 : (double)inter / union;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
