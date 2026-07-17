using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Memory;

namespace PartyTown.Services.Import;

/// <summary>A persona write this commit performs: a mint (new persona) or a card refresh
/// on an existing one. Card = the reviewed finalize output (SystemPrompt + Bio) — the
/// planner never builds a card itself.</summary>
public sealed record PersonaMint(Guid PersonaId, string Name, string SystemPrompt, string? Bio);

/// <summary>A persona (with its trait list) that needs a reviewed card before this scene
/// can commit — the finalize endpoint's work list.</summary>
public sealed record PersonaCardRequirement(string Name, List<string> Traits, string TraitFingerprint);

/// <summary>Deterministic plan for one scene commit: everything the commit executes,
/// computed up front with no IO. <see cref="ImportCommitService"/> executes it.</summary>
public sealed record SceneCommitPlan
{
    /// <summary>Cast touched by this commit that no earlier commit minted or matched.</summary>
    public List<PersonaMint> PersonasToMint { get; init; } = new();

    /// <summary>Existing personas (matched or earlier-minted) whose reviewed card moved
    /// on since the last commit. Executed with the human-drift guard.</summary>
    public List<PersonaMint> PersonasToUpdate { get; init; } = new();

    /// <summary>Full cast map after minting: canonical name → persona id.</summary>
    public Dictionary<string, Guid> CastByName { get; init; } = new();

    /// <summary>Participants this commit adds to the Party/Room: touched cast (LLM-driven)
    /// plus the export's human as a User-driven participant.</summary>
    public List<PartyParticipant> Participants { get; init; } = new();

    /// <summary>Room history: every message-category chunk in the scene that was not
    /// discarded, in chunk order, at scheme timestamps.</summary>
    public List<ImportedMessage> Messages { get; init; } = new();

    /// <summary>AGE writes: one seed per event-routed episode. Sub-floor (history-only)
    /// episodes are deliberately absent — their chunks still appear in Messages.</summary>
    public List<ImportedEventSeed> Events { get; init; } = new();

    /// <summary>Suggested-vs-final diffs; party/room/committedAt stamped at execute time.</summary>
    public List<ImportCorrection> Corrections { get; init; } = new();

    /// <summary>Episode participants neither the cast nor a concept-routed registry entry
    /// claimed (counted, never minted).</summary>
    public List<string> UnmatchedParticipants { get; init; } = new();
}

/// <summary>
/// The deterministic half of scene commit (ADR 0017: "then only deterministic writes").
/// Pure input → plan, no IO, no LLM, no clock — every commit rule is assertable in
/// backend-test. Executes the registry's recorded match-or-mint decisions (never
/// prompts), routes person-as-concept cast into event concept links, and refuses to plan
/// while a match proposal is undecided or a mint lacks a reviewed card.
/// </summary>
public static class ImportCommitPlanner
{
    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);

    public static SceneCommitPlan Plan(SceneCommitInput input)
    {
        var target = input.Target
            ?? throw new InvalidOperationException("Commit target not set — pin the target before planning.");

        var cast = Resolve(input);

        // Commit executes recorded decisions, never prompts — an undecided library-match
        // proposal on touched cast is the one thing that blocks a commit.
        var pending = cast.TouchedCast
            .Select(cast.EntryOf)
            .Where(e => e is { MatchState: CastMatchStates.Proposed })
            .Select(e => e!.Name)
            .ToList();
        if (pending.Count > 0)
            throw new InvalidOperationException(
                $"Cast with undecided match proposals: {string.Join(", ", pending)} — confirm match or mint first.");

        var castByName = new Dictionary<string, Guid>(input.CommittedPersonas, StringComparer.OrdinalIgnoreCase);
        var cards = new Dictionary<string, PersonaCardDraft>(input.Cards, StringComparer.OrdinalIgnoreCase);
        var mints = new List<PersonaMint>();
        var updates = new List<PersonaMint>();

        foreach (var name in cast.TouchedCast)
        {
            var card = cards.GetValueOrDefault(name);
            if (castByName.TryGetValue(name, out var committedId))
            {
                // Minted/matched by an earlier commit — refresh only if the reviewed card
                // moved past what that commit wrote.
                if (card is not null && CardChanged(card))
                    updates.Add(new PersonaMint(committedId, name, card.SystemPrompt, card.Bio));
                continue;
            }

            if (cast.EntryOf(name) is { MatchState: CastMatchStates.ConfirmedMatch, MatchedPersonaId: { } matchedId })
            {
                castByName[name] = matchedId;
                if (card is not null && CardChanged(card))
                    updates.Add(new PersonaMint(matchedId, name, card.SystemPrompt, card.Bio));
                continue;
            }

            // Mint: confirmed-mint, or unmatched with no proposal (registry stays optional).
            // Deterministic ids so a retried (or rolled-back-and-recommitted) session
            // converges on the same persona. Minting requires a reviewed card — commit
            // never generates one (ADR 0017: finalize output passes review as draft first).
            if (card is null || string.IsNullOrWhiteSpace(card.SystemPrompt))
                throw new InvalidOperationException(
                    $"Persona '{name}' has no reviewed card — run scene finalize before committing.");
            var personaId = DeterministicGuid(input.SessionId, "persona", name.ToLowerInvariant());
            castByName[name] = personaId;
            mints.Add(new PersonaMint(personaId, name, card.SystemPrompt, card.Bio));
        }

        var participants = cast.TouchedCast
            .Select(n => new PartyParticipant { Id = castByName[n], Name = n, Driver = DriverKind.LLM })
            .ToList();
        participants.Add(new PartyParticipant
        {
            Id = target.UserParticipantId,
            Name = "You",
            Driver = DriverKind.User,
        });

        var corrections = PlanCorrections(input);
        corrections.AddRange(PlanMatchFlips(input, cast));

        return new SceneCommitPlan
        {
            PersonasToMint = mints,
            PersonasToUpdate = updates,
            CastByName = castByName,
            Participants = participants,
            Messages = PlanMessages(input, target),
            Events = PlanEvents(cast, castByName),
            Corrections = corrections,
            UnmatchedParticipants = cast.Unmatched,
        };
    }

    /// <summary>True when the reviewed card differs from what the last commit wrote —
    /// the "update, don't duplicate" trigger for re-finalized personas.</summary>
    private static bool CardChanged(PersonaCardDraft card)
        => !string.Equals(card.SystemPrompt, card.CommittedSystemPrompt, StringComparison.Ordinal)
           || !string.Equals(card.Bio, card.CommittedBio, StringComparison.Ordinal);

    // ── cast resolution (registry-aware) ─────────────────────────────────────────

    /// <summary>Everything cast-shaped the plan needs, resolved once: the cast universe,
    /// confirmed alias map, per-name registry entries, concept-routed claims, the cast
    /// this commit touches and the participants nobody claimed.</summary>
    private sealed class CastResolution
    {
        public required List<string> CastNames { get; init; }
        public required Dictionary<string, string> AliasOf { get; init; }
        public required Dictionary<string, RegistryCastEntry> Entries { get; init; }
        public required Dictionary<string, RegistryCastEntry> ConceptClaims { get; init; }
        public required List<string> TouchedCast { get; init; }
        public required List<string> Unmatched { get; init; }
        public required List<ImportDraftItem> EventItems { get; init; }

        public RegistryCastEntry? EntryOf(string name) => Entries.GetValueOrDefault(name);
    }

    private static CastResolution Resolve(SceneCommitInput input)
    {
        var entries = new Dictionary<string, RegistryCastEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in input.Cast.Where(e => !string.IsNullOrWhiteSpace(e.Name)))
            entries.TryAdd(entry.Name, entry);

        var aliasOf = ImportFold.RegistryAliasMap(input.Cast);

        // Confirmed person-as-concept entries claim names (and aliases) away from the
        // persona path — arc-critical non-cast characters become Concepts, not Personas.
        var conceptClaims = new Dictionary<string, RegistryCastEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in input.Cast.Where(e => e.Confirmed && e.Routing == CastRoutingModes.Concept))
        {
            conceptClaims.TryAdd(entry.Name, entry);
            foreach (var alias in entry.Aliases.Where(a => !string.IsNullOrWhiteSpace(a)))
                conceptClaims.TryAdd(alias.Trim(), entry);
        }
        bool ConceptRouted(string canonical) => conceptClaims.ContainsKey(canonical);

        // Cast universe: dossier'd characters (trait owners anywhere in the draft), cast
        // earlier commits resolved, plus confirmed persona-routed registry entries —
        // minus anything the human routed to Concept.
        var castNames = input.TraitItems
            .Where(t => !string.IsNullOrWhiteSpace(t.Persona))
            .Select(t => t.Persona!)
            .Concat(input.CommittedPersonas.Keys)
            .Concat(input.Cast
                .Where(e => e.Confirmed && e.Routing == CastRoutingModes.Persona)
                .Select(e => e.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => !ConceptRouted(n))
            .ToList();

        var touchedCast = new List<string>();
        var unmatched = new List<string>();

        void Touch(string castName)
        {
            if (!touchedCast.Contains(castName, StringComparer.OrdinalIgnoreCase))
                touchedCast.Add(castName);
        }

        foreach (var trait in input.Items.Where(i =>
                     i.Type == DraftItemTypes.Trait && !string.IsNullOrWhiteSpace(i.Persona)))
        {
            var name = aliasOf.GetValueOrDefault(trait.Persona!, trait.Persona!);
            if (!ConceptRouted(name))
                Touch(ResolveCast(name, castNames, aliasOf) ?? name);
        }

        var eventItems = input.Items
            .Where(i => i.Type == DraftItemTypes.Episode && i.Routing == DraftRouting.Event)
            .OrderBy(i => i.SourceChunks.Count == 0 ? int.MaxValue : i.SourceChunks.Min())
            .ToList();
        foreach (var name in eventItems.SelectMany(i => i.Participants))
        {
            var resolved = ResolveCast(name, castNames, aliasOf);
            if (resolved is not null)
            {
                Touch(resolved);
            }
            else if (!conceptClaims.ContainsKey(name)
                     && !unmatched.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                unmatched.Add(name);
            }
        }

        return new CastResolution
        {
            CastNames = castNames,
            AliasOf = aliasOf,
            Entries = entries,
            ConceptClaims = conceptClaims,
            TouchedCast = touchedCast,
            Unmatched = unmatched,
            EventItems = eventItems,
        };
    }

    // ── finalize work list (shared with the pre-commit finalize endpoint) ────────

    /// <summary>The personas this scene's commit would touch, with the full draft trait
    /// list each card compresses. Works without a commit target — finalize runs before
    /// the first commit pins one.</summary>
    public static List<PersonaCardRequirement> PersonasNeedingCards(SceneCommitInput input)
    {
        var cast = Resolve(input);
        return cast.TouchedCast
            .Where(name => cast.EntryOf(name) is not { MatchState: CastMatchStates.Proposed })
            .Select(name =>
            {
                var traits = TraitsFor(name, input.TraitItems);
                return new PersonaCardRequirement(name, traits, TraitFingerprint(traits));
            })
            .ToList();
    }

    internal static List<string> TraitsFor(string castName, IReadOnlyList<ImportDraftItem> traitItems)
        => traitItems
            .Where(t => string.Equals(t.Persona, castName, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Summary)
            .ToList();

    /// <summary>Stable digest of a trait list — cards store it so finalize can skip
    /// personas whose traits have not changed since the last pass.</summary>
    public static string TraitFingerprint(IReadOnlyList<string> traits)
        => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", traits.Select(t => t.Trim()).OrderBy(t => t, StringComparer.OrdinalIgnoreCase)))));

    // ── messages: history-routed chunks → Room history ───────────────────────────

    private static List<ImportedMessage> PlanMessages(SceneCommitInput input, ImportCommitTarget target)
    {
        var dispositionByChunk = input.ChunkRoutings.ToDictionary(r => r.ChunkIndex, r => r.Disposition);
        var messages = new List<ImportedMessage>();
        foreach (var chunk in input.Chunks.OrderBy(c => c.Index))
        {
            if (chunk.Category != ImportChunkCategories.Message) continue;
            var disposition = dispositionByChunk.GetValueOrDefault(chunk.Index, ChunkDispositions.Unprocessed);
            // Discarded (pure OOC/meta) chunks are ledger-recorded, never written; recap /
            // thought / media chunks already fell out via the category filter.
            if (disposition is not (ChunkDispositions.EventRouted or ChunkDispositions.Folded or ChunkDispositions.HistoryOnly))
                continue;

            // No per-chunk speaker attribution exists in the map output (deliberate — the
            // legacy classify step was an LLM call). "model" prose is un-personed
            // multi-character narration, which is exactly what the Narrator stands for.
            var isUser = chunk.Role.Equals("user", StringComparison.OrdinalIgnoreCase);
            messages.Add(new ImportedMessage
            {
                SenderId = isUser ? target.UserParticipantId : Narrator.PersonaId,
                SenderType = isUser ? "user" : "assistant",
                Content = chunk.Text,
                SendAt = ChunkTimestamp(input.Settings, chunk.Index).ToUnixTimeMilliseconds(),
                ChatGroupId = target.RoomId,
            });
        }
        return messages;
    }

    // ── events: event-routed episodes → AGE seeds ────────────────────────────────

    private static List<ImportedEventSeed> PlanEvents(CastResolution cast, Dictionary<string, Guid> castByName)
    {
        var seeds = new List<ImportedEventSeed>();
        foreach (var item in cast.EventItems)
        {
            var recollectors = item.Participants
                .Select(p => ResolveCast(p, cast.CastNames, cast.AliasOf))
                .Where(c => c is not null && castByName.ContainsKey(c))
                .Select(c => castByName[c!])
                .Distinct()
                .ToList();
            var concepts = item.Concepts
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => new ImportedConceptSeed(c.Trim().ToLowerInvariant(), c.Trim()))
                .ToList();

            // Person-as-concept: participants the registry routes to Concept link the
            // Event to that concept instead of minting a persona (phase-2 finding —
            // arc-critical non-cast characters are reachable only via Concept).
            foreach (var participant in item.Participants)
            {
                if (ResolveCast(participant, cast.CastNames, cast.AliasOf) is not null) continue;
                if (!cast.ConceptClaims.TryGetValue(participant, out var entry)) continue;
                if (concepts.Any(c => c.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase))) continue;
                concepts.Add(new ImportedConceptSeed(entry.Name.ToLowerInvariant(), entry.Name));
            }

            seeds.Add(new ImportedEventSeed
            {
                EventId = item.Id,
                Description = item.Summary,
                At = item.At,
                Weight = item.Weight,
                AnchorOrdinal = item.SourceChunks.Count == 0 ? 0 : item.SourceChunks.Min(),
                RecollectorPersonaIds = recollectors,
                Concepts = concepts,
            });
        }
        return seeds;
    }

    // ── corrections: suggested vs human-final ────────────────────────────────────

    private static List<ImportCorrection> PlanCorrections(SceneCommitInput input)
    {
        var corrections = new List<ImportCorrection>();

        foreach (var item in input.Items)
        {
            // Pre-snapshot items (folded before this field existed) have no suggestion to
            // diff against; skip rather than invent one.
            if (item.SuggestedSummary.Length == 0 && item.SuggestedRouting.Length == 0) continue;

            var kinds = new List<string>();
            if (item.SuggestedRouting.Length > 0 && item.Routing != item.SuggestedRouting)
            {
                if (item.Routing == DraftRouting.Event) kinds.Add(CorrectionKinds.Promoted);
                else if (item.Routing == DraftRouting.History) kinds.Add(CorrectionKinds.Demoted);
            }
            if (Math.Abs(item.Weight - item.SuggestedWeight) > 1e-9)
                kinds.Add(CorrectionKinds.Reweighted);
            if (!string.Equals(item.Summary, item.SuggestedSummary, StringComparison.Ordinal))
                kinds.Add(CorrectionKinds.Renamed);
            if (kinds.Count == 0) continue;

            var suggested = SnapshotJsonOf(item.SuggestedSummary, item.SuggestedWeight, item.SuggestedRouting, null);
            var final = SnapshotJsonOf(item.Summary, item.Weight, item.Routing, item.RoutingReason);
            corrections.AddRange(kinds.Select(kind => new ImportCorrection
            {
                Id = DeterministicGuid(input.SessionId, "correction", $"{item.Id}:{kind}"),
                SessionId = input.SessionId,
                SceneId = input.Scene.Id,
                ItemId = item.Id,
                Kind = kind,
                ChunkRefs = item.SourceChunks.ToList(),
                Suggested = suggested,
                Final = final,
            }));
        }

        // A rerun is itself a human correction: the first output wasn't good enough. The
        // note (when present) is the label a future extraction prompt can learn from.
        if (input.Scene.RunCount > 1)
        {
            corrections.Add(new ImportCorrection
            {
                Id = DeterministicGuid(input.SessionId, "correction", $"{input.Scene.Id}:{CorrectionKinds.RegeneratedWithNote}"),
                SessionId = input.SessionId,
                SceneId = input.Scene.Id,
                ItemId = null,
                Kind = CorrectionKinds.RegeneratedWithNote,
                ChunkRefs = Enumerable.Range(input.Scene.FromChunk, input.Scene.ToChunk - input.Scene.FromChunk + 1).ToList(),
                Note = input.Scene.Note,
            });
        }

        return corrections;
    }

    /// <summary>A human overriding the matcher's proposal (minting despite a proposed
    /// match, or matching a different persona) is a label worth keeping — the ledger's
    /// match-flipped kind. Deterministic ids: retried commits re-insert, never duplicate.</summary>
    private static List<ImportCorrection> PlanMatchFlips(SceneCommitInput input, CastResolution cast)
    {
        var flips = new List<ImportCorrection>();
        foreach (var name in cast.TouchedCast)
        {
            if (cast.EntryOf(name) is not { ProposedPersonaId: { } proposedId } entry) continue;
            var flipped = entry.MatchState == CastMatchStates.ConfirmedMint
                || (entry.MatchState == CastMatchStates.ConfirmedMatch && entry.MatchedPersonaId != proposedId);
            if (!flipped) continue;

            flips.Add(new ImportCorrection
            {
                Id = DeterministicGuid(input.SessionId, "correction", $"{entry.Name.ToLowerInvariant()}:{CorrectionKinds.MatchFlipped}"),
                SessionId = input.SessionId,
                SceneId = input.Scene.Id,
                ItemId = null,
                Kind = CorrectionKinds.MatchFlipped,
                Suggested = JsonSerializer.Serialize(new
                {
                    matchState = CastMatchStates.Proposed,
                    personaId = proposedId,
                    personaName = entry.ProposedPersonaName,
                }, SnapshotJson),
                Final = JsonSerializer.Serialize(new
                {
                    matchState = entry.MatchState,
                    personaId = entry.MatchedPersonaId,
                }, SnapshotJson),
            });
        }
        return flips;
    }

    private static string SnapshotJsonOf(string summary, double weight, string routing, string? routingReason)
        => JsonSerializer.Serialize(new { summary, weight, routing, routingReason }, SnapshotJson);

    // ── cast matching (probe rule + registry aliases) ────────────────────────────

    /// <summary>Registry alias exact match first, then exact name, then token subset
    /// either way with a unique hit (probe rule) — or nothing.</summary>
    internal static string? ResolveCast(
        string name, IReadOnlyList<string> castNames, IReadOnlyDictionary<string, string>? aliasOf = null)
    {
        if (aliasOf is not null && aliasOf.TryGetValue(name.Trim(), out var canonical)
            && castNames.Contains(canonical, StringComparer.OrdinalIgnoreCase))
            return canonical;

        var exact = castNames.FirstOrDefault(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        var tokens = ImportFold.TokenSet(name);
        if (tokens.Count == 0) return null;
        var hits = castNames.Where(c =>
        {
            var castTokens = ImportFold.TokenSet(c);
            return castTokens.Count > 0 && (tokens.IsSubsetOf(castTokens) || castTokens.IsSubsetOf(tokens));
        }).ToList();
        return hits.Count == 1 ? hits[0] : null;
    }

    private static DateTimeOffset ChunkTimestamp(ImportSettings settings, int chunkIndex)
        => settings.Anchor + TimeSpan.FromMinutes(settings.SpacingMinutes * chunkIndex);

    /// <summary>Stable ids for everything commit creates, so retries and re-commits after
    /// rollback converge instead of duplicating.</summary>
    internal static Guid DeterministicGuid(Guid sessionId, string kind, string key)
        => new(MD5.HashData(Encoding.UTF8.GetBytes(string.Create(
            CultureInfo.InvariantCulture, $"partytown-import-commit:{sessionId}:{kind}:{key}"))));
}
