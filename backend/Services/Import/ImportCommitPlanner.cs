using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Memory;

namespace PartyTown.Services.Import;

/// <summary>A persona this commit mints, card-as-drafted (traits joined verbatim).
/// Issue 03 replaces the card source with the finalize step (trait compress + Bio).</summary>
public sealed record PersonaMint(Guid PersonaId, string Name, string SystemPrompt);

/// <summary>Deterministic plan for one scene commit: everything the commit executes,
/// computed up front with no IO. <see cref="ImportCommitService"/> executes it.</summary>
public sealed record SceneCommitPlan
{
    /// <summary>Cast touched by this commit that no earlier commit minted.</summary>
    public List<PersonaMint> PersonasToMint { get; init; } = new();

    /// <summary>Full cast map after minting: canonical name → persona id.</summary>
    public Dictionary<string, Guid> CastByName { get; init; } = new();

    /// <summary>Participants this commit adds to the Party/Room: minted cast (LLM-driven)
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

    /// <summary>Episode participants no cast name claimed (counted, never minted).</summary>
    public List<string> UnmatchedParticipants { get; init; } = new();
}

/// <summary>
/// The deterministic half of scene commit (ADR 0017: "then only deterministic writes").
/// Pure input → plan, no IO, no LLM, no clock — every commit rule is assertable in
/// backend-test. Port of the phase-2 probe's seeder mapping (cast token-matching, event
/// shapes, ordinal anchors) onto the session draft.
/// </summary>
public static class ImportCommitPlanner
{
    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);

    public static SceneCommitPlan Plan(SceneCommitInput input)
    {
        var target = input.Target
            ?? throw new InvalidOperationException("Commit target not set — pin the target before planning.");

        // Cast universe: dossier'd characters (trait owners anywhere in the draft) plus
        // personas earlier commits already minted. Referenced characters without traits
        // stay unmatched by design (person-as-concept arrives with the registry, issue 03).
        var castNames = input.TraitItems
            .Where(t => !string.IsNullOrWhiteSpace(t.Persona))
            .Select(t => t.Persona!)
            .Concat(input.CommittedPersonas.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var unmatched = new List<string>();
        var touchedCast = new List<string>();

        void Touch(string castName)
        {
            if (!touchedCast.Contains(castName, StringComparer.OrdinalIgnoreCase))
                touchedCast.Add(castName);
        }

        foreach (var trait in input.Items.Where(i =>
                     i.Type == DraftItemTypes.Trait && !string.IsNullOrWhiteSpace(i.Persona)))
            Touch(trait.Persona!);

        var eventItems = input.Items
            .Where(i => i.Type == DraftItemTypes.Episode && i.Routing == DraftRouting.Event)
            .OrderBy(i => i.SourceChunks.Count == 0 ? int.MaxValue : i.SourceChunks.Min())
            .ToList();
        foreach (var name in eventItems.SelectMany(i => i.Participants))
        {
            var cast = ResolveCast(name, castNames);
            if (cast is null)
            {
                if (!unmatched.Contains(name, StringComparer.OrdinalIgnoreCase))
                    unmatched.Add(name);
            }
            else
            {
                Touch(cast);
            }
        }

        // Mint what this commit touches and earlier commits didn't. Deterministic ids so
        // a retried (or rolled-back-and-recommitted) session converges on the same persona.
        var castByName = new Dictionary<string, Guid>(input.CommittedPersonas, StringComparer.OrdinalIgnoreCase);
        var mints = new List<PersonaMint>();
        foreach (var name in touchedCast.Where(n => !castByName.ContainsKey(n)))
        {
            var personaId = DeterministicGuid(input.SessionId, "persona", name.ToLowerInvariant());
            castByName[name] = personaId;
            mints.Add(new PersonaMint(personaId, name, BuildCardAsDrafted(name, input.TraitItems)));
        }

        var participants = mints
            .Select(m => new PartyParticipant { Id = m.PersonaId, Name = m.Name, Driver = DriverKind.LLM })
            .ToList();
        participants.Add(new PartyParticipant
        {
            Id = target.UserParticipantId,
            Name = "You",
            Driver = DriverKind.User,
        });

        return new SceneCommitPlan
        {
            PersonasToMint = mints,
            CastByName = castByName,
            Participants = participants,
            Messages = PlanMessages(input, target),
            Events = PlanEvents(eventItems, castByName, castNames),
            Corrections = PlanCorrections(input),
            UnmatchedParticipants = unmatched,
        };
    }

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

    private static List<ImportedEventSeed> PlanEvents(
        List<ImportDraftItem> eventItems,
        Dictionary<string, Guid> castByName,
        List<string> castNames)
    {
        var seeds = new List<ImportedEventSeed>();
        foreach (var item in eventItems)
        {
            var recollectors = item.Participants
                .Select(p => ResolveCast(p, castNames))
                .Where(c => c is not null && castByName.ContainsKey(c))
                .Select(c => castByName[c!])
                .Distinct()
                .ToList();
            seeds.Add(new ImportedEventSeed
            {
                EventId = item.Id,
                Description = item.Summary,
                At = item.At,
                Weight = item.Weight,
                AnchorOrdinal = item.SourceChunks.Count == 0 ? 0 : item.SourceChunks.Min(),
                RecollectorPersonaIds = recollectors,
                Concepts = item.Concepts
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => new ImportedConceptSeed(c.Trim().ToLowerInvariant(), c.Trim()))
                    .ToList(),
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

    private static string SnapshotJsonOf(string summary, double weight, string routing, string? routingReason)
        => JsonSerializer.Serialize(new { summary, weight, routing, routingReason }, SnapshotJson);

    // ── cast matching (probe rule: token subset either way, unique hit or nothing) ─

    internal static string? ResolveCast(string name, IReadOnlyList<string> castNames)
    {
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

    // ── card-as-drafted (the issue-03 finalize seam) ─────────────────────────────

    private static string BuildCardAsDrafted(string castName, IReadOnlyList<ImportDraftItem> traitItems)
    {
        var traits = traitItems
            .Where(t => string.Equals(t.Persona, castName, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Summary)
            .ToList();
        var sb = new StringBuilder();
        sb.Append("You are ").Append(castName).Append('.');
        foreach (var trait in traits)
            sb.Append('\n').Append("- ").Append(trait);
        return sb.ToString();
    }

    private static DateTimeOffset ChunkTimestamp(ImportSettings settings, int chunkIndex)
        => settings.Anchor + TimeSpan.FromMinutes(settings.SpacingMinutes * chunkIndex);

    /// <summary>Stable ids for everything commit creates, so retries and re-commits after
    /// rollback converge instead of duplicating.</summary>
    internal static Guid DeterministicGuid(Guid sessionId, string kind, string key)
        => new(MD5.HashData(Encoding.UTF8.GetBytes(string.Create(
            CultureInfo.InvariantCulture, $"partytown-import-commit:{sessionId}:{kind}:{key}"))));
}
