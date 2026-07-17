using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Memory;

namespace PartyTown.Services.Import;

/// <summary>
/// Executes a scene commit (ADR 0017 slice 3+4): pin the commit target (Party + Room),
/// run the pure <see cref="ImportCommitPlanner"/>, then perform only deterministic
/// writes — personas from reviewed finalize cards, Room membership, history messages,
/// AGE events, correction ledger. No LLM calls anywhere on this path: finalize runs
/// pre-commit and its output passes human review as draft first.
///
/// Retry story: messages are the one non-idempotent write, guarded by a persisted
/// per-scene flag; persona/AGE/ledger writes use deterministic ids and converge. The
/// scene is only marked committed after everything succeeded, so a failed commit can be
/// re-POSTed as-is.
/// </summary>
public sealed class ImportCommitService(
    IGrainFactory grains,
    IImportMemoryWriter memoryWriter,
    IImportCorrectionStore corrections,
    ILogger<ImportCommitService> logger)
{
    public async Task<SceneCommitResult> CommitSceneAsync(
        Guid sessionId, Guid sceneId, SceneCommitRequest request, CancellationToken ct)
    {
        var session = grains.GetGrain<IImportSessionGrain>(sessionId);
        var input = await session.GetSceneCommitInputAsync(sceneId);

        var target = await EnsureTargetAsync(session, input, request);
        var plan = ImportCommitPlanner.Plan(input with { Target = target });

        // 1. Personas from reviewed cards (finalize output — the plan refuses to exist
        //    without them): mint new ones, refresh matched/earlier-minted ones. Before
        //    any overwrite of a persona that has a committed baseline, compare the live
        //    card against that baseline — drift means a human edited the persona in the
        //    library since, and human edits win (the update is skipped and surfaced).
        var cards = new Dictionary<string, PersonaCardDraft>(input.Cards, StringComparer.OrdinalIgnoreCase);
        var personaRoot = grains.GetGrain<IPersonaRootGrain>(Guid.Empty);
        var updated = new List<string>();
        var skipped = new List<string>();
        foreach (var (write, isMint) in plan.PersonasToMint.Select(m => (m, true))
                     .Concat(plan.PersonasToUpdate.Select(m => (m, false))))
        {
            if (await IsDriftedAsync(personaRoot, write, cards.GetValueOrDefault(write.Name)))
            {
                skipped.Add(write.Name);
                continue;
            }
            await personaRoot.AddPersona(write.PersonaId, write.Name, write.SystemPrompt, write.Bio);
            if (!isMint) updated.Add(write.Name);
        }

        var newlyResolved = plan.CastByName
            .Where(kv => !input.CommittedPersonas.ContainsKey(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        if (newlyResolved.Count > 0)
            await session.RecordCommittedPersonasAsync(newlyResolved);
        var written = plan.PersonasToMint.Select(m => m.Name)
            .Concat(updated)
            .Where(n => !skipped.Contains(n))
            .ToList();
        if (written.Count > 0)
            await session.MarkCardsCommittedAsync(written);

        // 2. Membership: merge the import cast into the Party (dedup by id — reruns and
        //    later scenes are no-ops), then pin this Room's cast. The Narrator rides along
        //    automatically (PartyGrain enforces it; model-role messages are sent as it).
        var partyGrain = grains.GetGrain<IPartyGrain>(target.PartyId);
        var party = await partyGrain.GetParty();
        var byId = party.Participants.ToDictionary(p => p.Id);
        foreach (var participant in plan.Participants)
            byId[participant.Id] = participant;
        await partyGrain.SetParticipants(byId.Values.ToList());

        var room = grains.GetGrain<IChatGroupGrain>(target.RoomId);
        var roomIds = new HashSet<Guid>(await room.GetParticipantIdsAsync());
        foreach (var participant in plan.Participants)
            roomIds.Add(participant.Id);
        roomIds.Add(Narrator.PersonaId);
        await room.SetParticipantIdsAsync(roomIds);

        // 3. History messages — append once, flag persisted so retries never double-write.
        var messagesWritten = 0;
        if (!input.Scene.MessagesWritten && plan.Messages.Count > 0)
        {
            messagesWritten = await room.ImportMessagesAsync(plan.Messages, ct);
            await session.RecordSceneMessagesWrittenAsync(sceneId);
        }

        // 4. Memory graph: event-routed episodes only (sub-floor items stay history-only).
        var stats = await memoryWriter.SeedEventsAsync(target.PartyId, target.RoomId, plan.Events, ct);

        // 5. Correction ledger — durable, outlives the session grain.
        var committedAt = DateTimeOffset.UtcNow;
        var stamped = plan.Corrections
            .Select(c => c with { PartyId = target.PartyId, RoomId = target.RoomId, CommittedAt = committedAt })
            .ToList();
        await corrections.AppendAsync(stamped, ct);

        // 6. Settle the scene. From here on rerun/edit/delete are refused.
        var scene = await session.CompleteSceneCommitAsync(sceneId);

        logger.LogInformation(
            "Import session {SessionId} scene {SceneId} committed into room {RoomId}: {Messages} messages, {Events} events, {Recollections} recollections, {Minted} personas minted, {Corrections} corrections",
            sessionId, sceneId, target.RoomId, messagesWritten, stats.Events, stats.Recollections,
            plan.PersonasToMint.Count, stamped.Count);

        return new SceneCommitResult
        {
            SceneId = sceneId,
            PartyId = target.PartyId,
            RoomId = target.RoomId,
            CommittedAt = scene.CommittedAt ?? committedAt,
            MessagesWritten = messagesWritten,
            EventsWritten = stats.Events,
            RecollectionsWritten = stats.Recollections,
            ConceptLinks = stats.ConceptLinks,
            PersonasMinted = plan.PersonasToMint.Select(m => m.Name).ToList(),
            PersonasUpdated = updated,
            PersonaUpdatesSkipped = skipped,
            CorrectionsRecorded = stamped.Count,
            UnmatchedParticipants = plan.UnmatchedParticipants,
        };
    }

    /// <summary>True when the live library persona no longer matches the card's committed
    /// baseline — a human edited it outside the import, so the import must not overwrite.
    /// No baseline (first write, or a fresh mint) → not drifted.</summary>
    private async Task<bool> IsDriftedAsync(
        IPersonaRootGrain personaRoot, PersonaMint write, PersonaCardDraft? card)
    {
        if (card?.CommittedSystemPrompt is null) return false;
        if (!await personaRoot.HasPersonaId(write.PersonaId)) return false;
        var live = await grains.GetGrain<IPersonaGrain>(write.PersonaId).GetPersona();
        return !string.Equals(live.SystemPrompt, card.CommittedSystemPrompt, StringComparison.Ordinal)
               || !string.Equals(live.Bio, card.CommittedBio, StringComparison.Ordinal);
    }

    /// <summary>Whole-import rollback: delete the Room's AGE events, wipe and unregister
    /// the Room, and return every scene to editable draft. Minted personas stay in the
    /// library (deterministic ids — a re-commit converges on them).</summary>
    public async Task<ImportCommitTarget> RollbackAsync(Guid sessionId, CancellationToken ct)
    {
        var session = grains.GetGrain<IImportSessionGrain>(sessionId);
        var target = await session.GetCommitTargetAsync()
            ?? throw new InvalidOperationException("Nothing committed — no Room to roll back.");

        await memoryWriter.DeleteRoomEventsAsync(target.PartyId, target.RoomId, ct);

        var room = grains.GetGrain<IChatGroupGrain>(target.RoomId);
        await room.DeleteMessagesAfterAsync(0);
        await grains.GetGrain<IPartyGrain>(target.PartyId).RemoveChatGroup(target.RoomId);

        await session.ResetCommitsAsync();

        logger.LogInformation(
            "Import session {SessionId} rolled back: room {RoomId} removed from party {PartyId}",
            sessionId, target.RoomId, target.PartyId);
        return target;
    }

    private async Task<ImportCommitTarget> EnsureTargetAsync(
        IImportSessionGrain session, SceneCommitInput input, SceneCommitRequest request)
    {
        if (input.Target is { } existing)
        {
            if (request.PartyId is { } requested && requested != Guid.Empty && requested != existing.PartyId)
                throw new ArgumentException(
                    $"Session already commits into party {existing.PartyId} — one Room per import session.");
            return existing;
        }

        if (request.PartyId is not { } partyId || partyId == Guid.Empty)
            throw new ArgumentException("First commit needs a partyId to create the Room in.");
        var root = grains.GetGrain<IPartyRootGrain>(Guid.Empty);
        if (!await root.HasPartyId(partyId))
            throw new KeyNotFoundException($"Party {partyId} not found.");

        var roomName = !string.IsNullOrWhiteSpace(request.RoomName) ? request.RoomName.Trim()
            : !string.IsNullOrWhiteSpace(input.FileName) ? Path.GetFileNameWithoutExtension(input.FileName)
            : "Imported chat";
        var group = await grains.GetGrain<IPartyGrain>(partyId).CreateChatGroup(roomName);

        var target = new ImportCommitTarget
        {
            PartyId = partyId,
            RoomId = group.Id,
            RoomName = group.Name,
            UserParticipantId = ImportCommitPlanner.DeterministicGuid(input.SessionId, "participant", "user"),
        };
        await session.SetCommitTargetAsync(target);
        return target;
    }
}
