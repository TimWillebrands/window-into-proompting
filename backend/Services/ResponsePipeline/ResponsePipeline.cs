using System.Diagnostics;
using System.Text.Json;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Logging;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.Services.Papertrail;
using PartyTown.Services.Streaming;
using PartyTown.World;
using PartyTown.World.Self;

namespace PartyTown.Services.ResponsePipeline;

/// <summary>
/// Per-turn orchestration of a single persona's response to a triggering message:
/// pre-gate → slot reservation → decision phase → speaking phase → terminal write,
/// with race-cancel emote and external-cancel paths in the catch.
///
/// Stateless: the per-persona in-flight tracking lives in <see cref="InFlightStore"/>,
/// passed in per call. One singleton serves every persona; the only state lives
/// inside the store and on the chat group grain.
/// </summary>
public sealed class ResponsePipeline(
    IGrainFactory grainFactory,
    IMemoryRepository memoryRepository,
    ILoggerFactory loggerFactory,
    ILogger<ResponsePipeline> logger,
    World.Self.FollowThrough? followThrough = null,
    World.Work.Responsibilities? responsibilities = null)
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);
    private const int MaxDirectAiRounds = 6;

    public async Task HandleAsync(
        Persona persona,
        Guid chatGroupId,
        ChatMessage triggeringMessage,
        IChatGroupGrain chatGroupGrain,
        InFlightStore store,
        CancellationToken ct)
    {
        // Defense-in-depth: PersonaGrain already filters self-fan-out, but a stray
        // direct call shouldn't make Vlad ruminate on Vlad's last line.
        if (triggeringMessage.SenderId == persona.Id)
            return;

        // Self-as-User short-circuits here — the User-Effective rule lives in exactly one
        // place rather than at every fan-out site (per ADR 0011).
        var partyId = await chatGroupGrain.GetPartyIdAsync();
        var roomCast = await chatGroupGrain.GetCastAsync();

        // Only present, LLM-Effective Participants run the response pipeline. User-Effective
        // is human-typed; System-Effective (Narrator) never auto-speaks (ADR 0012).
        if (roomCast.Speaker(persona.Id) is not { } self)
        {
            logger.LogDebug(
                "Persona {PersonaName} is {Driver}-Effective in chat group {ChatGroupId}; skipping pipeline",
                persona.Name, roomCast.EffectiveOf(persona.Id)?.ToString() ?? "absent", chatGroupId);
            return;
        }

        var participantViews = roomCast.Views();

        // One root span per persona per triggering message. The fan-out at ChatGroupGrain
        // does NOT wrap these in a parent span on purpose — each persona reaction is an
        // independent root so the Aspire timeline shows them as siblings, mirroring reality.
        using var turnSpan = Tracing.Persona.StartActivity("persona.turn", ActivityKind.Internal);
        turnSpan?.SetTag("persona.id", persona.Id);
        turnSpan?.SetTag("persona.name", persona.Name);
        turnSpan?.SetTag("chat_group.id", chatGroupId);
        turnSpan?.SetTag("triggered_by.message_id", triggeringMessage.MessageId);
        turnSpan?.SetTag("triggered_by.sender_id", triggeringMessage.SenderId);

        logger.LogInformation(
            "Persona {PersonaName} notified of message {MessageId} in chat group {ChatGroupId}",
            persona.Name, triggeringMessage.MessageId, chatGroupId);

        // Pre-gate: snapshot history before reserving a slot so an obvious-skip persona
        // leaves no trace in the chat (no message slot → no thought-log entry → no LLM call).
        var preHistory = await chatGroupGrain.GetMessagesUntilAsync(int.MaxValue, listener: persona.Id);
        var preRounds = await chatGroupGrain.CountTrailingAssistantMessagesAsync();

        var isDirect = roomCast.IsDirect;
        var preUrge = UrgeMath.CalculateResponseUrge(self, preHistory, preRounds, directlyAddressed: isDirect);
        var preRecentSelf = UrgeMath.CountRecentSelfMessages(preHistory, persona.Id);
        turnSpan?.SetTag("urge.total", preUrge.Total);
        turnSpan?.SetTag("urge.mention", preUrge.MentionScore);
        turnSpan?.SetTag("urge.question", preUrge.QuestionScore);
        turnSpan?.SetTag("urge.silence_streak", preUrge.SilenceStreakScore);
        turnSpan?.SetTag("urge.cold_open", preUrge.ColdOpenScore);
        turnSpan?.SetTag("rounds.total_assistant", preRounds);
        turnSpan?.SetTag("rounds.recent_self", preRecentSelf);

        // Two Personas DMing can't ping-pong forever: a long all-AI streak in a DM ends it.
        var dmExhausted = isDirect && preRounds >= MaxDirectAiRounds;
        if (dmExhausted || UrgeMath.IsObviousSkip(preUrge, preRounds, preRecentSelf))
        {
            turnSpan?.SetTag("decision", "skip-obvious");
            var skipReason = $"obvious-skip (rounds={preRounds}, recentSelf={preRecentSelf})";
            logger.LogDebug(
                "Persona {PersonaName} silently skipped (urge={Urge:F2}, rounds={Rounds}, recentSelf={Self})",
                persona.Name, preUrge.Total, preRounds, preRecentSelf);
            try
            {
                await chatGroupGrain.RecordSkippedTurnAsync(
                    persona.Id, persona.Name ?? string.Empty,
                    triggeringMessage.MessageId, preUrge.Total, skipReason);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to record skip for persona {PersonaName}", persona.Name);
            }
            return;
        }

        var messageId = await chatGroupGrain.BeginTurnAsync(persona.Id, triggeringMessage.MessageId);
        turnSpan?.SetTag("result.message_id", messageId);

        // Race-trigger state: register before any awaits so a concurrent NotifyMessageAsync
        // can race against this one. Mutated in-place as decision → generation → done.
        var newCts = new CancellationTokenSource();
        var inFlight = store.Register(chatGroupId, messageId, newCts);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, newCts.Token);
        var linkedCt = linkedCts.Token;

        try
        {
            // Single history snapshot, shared by decision + generation.
            // Taking the snapshot at `messageId` includes any messages other personas
            // committed between our slot reservation and our read.
            var history = await chatGroupGrain.GetMessagesUntilAsync(messageId, listener: persona.Id);
            var scenario = await chatGroupGrain.GetScenarioAsync();

            // Info-isolation is a property of ParticipantView (identity + Driver only),
            // not a runtime contract — same roster is safe for both Decision and Speaking.
            var decisionParticipants = participantViews;

            await NotifyStreamAsync(chatGroupGrain, chatGroupId, messageId,
                MessageStreamEvent.PersonaEvaluatingResponse, persona.Id.ToString(), false);

            // Consume any pending repair hint for this group — one-shot, cleared regardless
            // of decision outcome so it can't double-fire.
            RepairHint? repairHint = store.ConsumeRepairHint(chatGroupId);
            if (repairHint is not null)
                turnSpan?.SetTag("repair.missed_message_id", repairHint.Value.MissedMessageId);

            // ADR 0015 recall: two-arm quota union for this Persona scoped to the Party
            // (cross-Room within one Party). The relevant arm graph-walks from the beat's
            // anchors (present cast + Concepts named in the triggering message); the recency
            // arm fills the remainder. Salience orders the candidates; the decision LLM
            // still judges beat-relevance in context. Recall failure is non-fatal — log +
            // continue with an empty list so a memory outage never blocks the persona from
            // responding.
            var presentPersonaIds = participantViews.Select(p => p.Id).ToList();
            var beat = await memoryRepository.RecallBeatAsync(
                new BeatAnchors(persona.Id, partyId, presentPersonaIds, triggeringMessage.Content ?? string.Empty,
                    await PlaceOfAsync(roomCast, partyId, chatGroupId)),
                memoryLimit: 10, stanceLimit: 5, logger, linkedCt);
            beat = await FreshenAsync(beat, partyId, persona.Id);
            var recollections = beat.Memories;
            turnSpan?.SetTag("recall.candidate_count", recollections.Count);

            // Observability (ADR 0015): the candidate set — with per-candidate arm
            // attribution — is what makes recall misses visible; this is the
            // embeddings-deferral contract's trigger instrument.
            if (recollections.Count > 0)
            {
                logger.LogInformation(
                    "Recall for persona {PersonaName} in party {PartyId}: {Candidates}",
                    persona.Name, partyId,
                    string.Join(", ", recollections.Select((r, i) =>
                        $"#{i + 1} {r.EdgeId:N}={r.Salience:F3} ({ArmTag(r.Arm)})")));
            }

            // ADR 0016 stance floor for the ambient "# Where you stand" block — zero LLM calls on the beat path.
            IReadOnlyList<string> stanceLines = beat.Stances.Select(StanceBlock.FormatLine).ToList();
            var ambivalenceCount = beat.Stances.Count(s => !string.IsNullOrEmpty(s.Contrast));
            turnSpan?.SetTag("stance.line_count", stanceLines.Count);
            turnSpan?.SetTag("stance.ambivalence_count", ambivalenceCount);

            var decision = await RunDecisionPhaseAsync(
                chatGroupGrain, chatGroupId, messageId, self, history, decisionParticipants, isDirect, scenario,
                recollections.Select(r => r.Snippet).ToList(), linkedCt,
                repairHint, stanceLines);

            // Resolve the index pick (1-based into the numbered block; decision service
            // already range-clamped) back to the recalled memory: snippet → speaking phase,
            // edge id → strengthening write. When the model declined but a never-deployed
            // relevant-arm memory sits at "they'd bring it up on their own" salience, the
            // floor deploys it anyway (RecallDeployment — one-shot per memory).
            var (pickedMemory, autoDeployed) = RecallDeployment.ResolvePick(
                decision.MemoryToReference, decision.Respond, recollections);
            var pickedIndex = pickedMemory is null
                ? (int?)null
                : recollections.ToList().IndexOf(pickedMemory) + 1;
            turnSpan?.SetTag("recall.picked_index", pickedIndex);
            turnSpan?.SetTag("recall.auto_deployed", autoDeployed);
            if (pickedMemory is not null)
            {
                logger.LogInformation(
                    "Persona {PersonaName} {PickSource} memory #{Index} ({EdgeId}, salience {Salience:F3})",
                    persona.Name, autoDeployed ? "auto-deployed (salience floor)" : "picked",
                    pickedIndex, pickedMemory.EdgeId, pickedMemory.Salience);

                // Strengthening fires on the pick, not mere surfacing (ADR 0015) — one DB
                // write, off the beat path, so neither failure nor latency touches the reply.
                if (pickedMemory.EdgeId != Guid.Empty)
                {
                    _ = StrengthenPickedMemoryAsync(pickedMemory.EdgeId, persona.Name);
                }
            }

            // Candidate set + pick ride the appraisal JSON so the papertrail can show why
            // this memory surfaced (and which surfaced-but-unpicked ones lost). Built as
            // the same RecallSummary type the papertrail renderer deserializes, so the
            // write and read sides share one schema.
            var recallLog = recollections.Count == 0 ? null : new RecallSummary
            {
                Candidates = recollections.Select((r, i) => new RecallCandidateSummary
                {
                    Index = i + 1,
                    Id = r.EdgeId,
                    Score = Math.Round(r.Salience, 4),
                    Weight = r.Weight,
                    RecallCount = r.RecallCount,
                    Arm = ArmTag(r.Arm),
                }).ToList(),
                Picked = pickedIndex,
                PickedId = pickedMemory?.EdgeId,
                AutoDeployed = autoDeployed ? true : null,
            };

            if (!decision.Respond)
            {
                turnSpan?.SetTag("decision", "skip-llm");
                turnSpan?.SetTag("decision.reason", decision.Reason);
                logger.LogInformation(
                    "Persona {PersonaName} decided NOT to respond: {Reason}", persona.Name, decision.Reason);
                // Mirror the success-path appraisal shape so the papertrail renderer can
                // surface the gut reaction uniformly. Plain-string appraisal would fail
                // TryParseAppraisal silently and drop the reason from the rendered output.
                var declinedAppraisal = JsonSerializer.Serialize(new
                {
                    personaId = persona.Id,
                    instruction = (string?)null,
                    reason = decision.Reason,
                    stop = true,
                    recall = recallLog
                }, WebOptions);
                await chatGroupGrain.EndTurnAsync(messageId, new TurnOutcome.Declined(declinedAppraisal));
                await NotifyStreamAsync(chatGroupGrain, chatGroupId, messageId,
                    MessageStreamEvent.PersonaDeclinedResponse,
                    JsonSerializer.Serialize(new { personaId = persona.Id, reason = decision.Reason }, WebOptions),
                    true);
                return;
            }

            turnSpan?.SetTag("decision", "respond");
            turnSpan?.SetTag("decision.reason", decision.Reason);
            logger.LogInformation("Persona {PersonaName} decided to respond: {Reason}", persona.Name, decision.Reason);

            await NotifyStreamAsync(chatGroupGrain, chatGroupId, messageId,
                MessageStreamEvent.PersonaEvaluationComplete,
                JsonSerializer.Serialize(new
                {
                    personaId = persona.Id,
                    instruction = decision.Instruction,
                    reason = decision.Reason,
                    stop = false
                }, WebOptions),
                false);

            // Decision committed to "speak". Promote the in-flight record so the race
            // sees it as Speaking phase and has the gut/preview to feed salience.
            inFlight.MarkGenerationStarted(decision.Reason, decision.Instruction);

            // pickedMemory is the recollection the decision LLM picked (by index) to weave
            // into this beat (null when nothing fit). The speaking phase receives the
            // resolved snippet text — its contract is unchanged by the index-pick switch:
            // decision phase selects, speaking phase executes. The gut reaction travels
            // alongside so speaking refines a felt moment rather than re-deciding cold.
            var result = await RunSpeakingPhaseAsync(
                chatGroupGrain, chatGroupId, messageId, self, participantViews, history,
                decision.Instruction, decision.Reason, scenario, pickedMemory?.Snippet, stanceLines, persona.Name, inFlight, linkedCt);

            var appraisalJson = JsonSerializer.Serialize(new
            {
                personaId = persona.Id,
                instruction = decision.Instruction,
                reason = decision.Reason,
                stop = false,
                recall = recallLog
            }, WebOptions);

            await chatGroupGrain.EndTurnAsync(messageId, new TurnOutcome.Spoke(
                result.Message ?? string.Empty, result.Reasoning, appraisalJson, result.Metadata));
            followThrough?.Consider(partyId, chatGroupId, persona.Id);

            if (result.Metadata is not null)
            {
                turnSpan?.SetTag("llm.provider", result.Metadata.Provider);
                turnSpan?.SetTag("llm.model", result.Metadata.ModelName);
            }
            logger.LogInformation("Persona {PersonaName} completed response for message {MessageId}", persona.Name, messageId);
        }
        catch (OperationCanceledException)
        {
            // Distinguish race-cancel (→ in-character emote) from external cancel via
            // PartyGrain.CancelGenerationAsync (→ red error). The race sets RaceCancelled
            // before tripping the CTS so this check is reliable.
            var snap = inFlight.Snapshot();
            if (snap.RaceCancelled)
            {
                turnSpan?.SetTag("decision", "race-cancelled");
                logger.LogInformation(
                    "Persona {PersonaName} race-cancelled (msg {MessageId}, drafted {Chars} chars)",
                    persona.Name, messageId, snap.GeneratedText.Length);

                // Cut off while still deciding: nobody saw them start, so no canned "*starts to speak*" line (ADR 0021).
                if (string.IsNullOrWhiteSpace(snap.GeneratedText))
                {
                    await chatGroupGrain.EndTurnAsync(messageId, new TurnOutcome.Declined(JsonSerializer.Serialize(new
                    {
                        personaId = persona.Id,
                        instruction = (string?)null,
                        reason = string.IsNullOrWhiteSpace(snap.GutReaction) ? "Race-cancelled before deciding." : snap.GutReaction,
                        stop = false,
                        raceCancelled = true
                    }, WebOptions)));
                    return;
                }

                string emote;
                try
                {
                    var emoteService = new PersonaEmoteService(
                        grainFactory.GetGrain<ILlmRouterGrain>(0),
                        loggerFactory.CreateLogger<PersonaEmoteService>());
                    // Use the parent ct (not linkedCt — linked is already cancelled by the race).
                    // Stale draft, the race already paid the cost — we still want the emote.
                    emote = await emoteService.GenerateAbandonmentEmoteAsync(
                        self,
                        snap.GeneratedText,
                        snap.InterruptingMessage,
                        snap.InterruptingSenderName,
                        ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Persona {PersonaName} emote generation threw — using literal fallback", persona.Name);
                    emote = PersonaEmoteService.GenerationFailureFallback;
                }

                // Synthesize an appraisal so the thought-log entry shows the gut reaction
                // and notes the race outcome that produced this emote.
                var emoteAppraisal = JsonSerializer.Serialize(new
                {
                    personaId = persona.Id,
                    instruction = (string?)null,
                    reason = string.IsNullOrWhiteSpace(snap.GutReaction)
                        ? "Race-cancelled before deciding."
                        : snap.GutReaction,
                    stop = false,
                    raceCancelled = true
                }, WebOptions);

                await chatGroupGrain.EndTurnAsync(messageId, new TurnOutcome.Interrupted(emote, emoteAppraisal));
            }
            else
            {
                turnSpan?.SetTag("decision", "cancelled");
                logger.LogDebug("Persona {PersonaName} generation cancelled (external)", persona.Name);
                await chatGroupGrain.EndTurnAsync(messageId, new TurnOutcome.Failed("cancelled"));
            }
        }
        catch (Exception ex)
        {
            turnSpan?.SetStatus(ActivityStatusCode.Error, ex.Message);
            turnSpan?.SetTag("decision", "error");
            logger.LogError(ex, "Persona {PersonaName} generation failed", persona.Name);
            // FIXME: If at some point we go public, don't send exceptions over the wire
            await chatGroupGrain.EndTurnAsync(messageId, new TurnOutcome.Failed(ex.ToString()));
        }
        finally
        {
            store.Remove(chatGroupId, messageId);
        }
    }

    /// <summary>
    /// Initiative path (ADR 0018): the Persona opens their mouth unprompted — Life chose this
    /// Room/DM and an intent. Skips the Decision phase (the choice was already made), runs the
    /// Speaking phase with the intent as draft, and appends; the normal fan-out lets others react.
    /// </summary>
    /// <returns>The spoken line, or null when the Persona isn't LLM-Effective here or got cut off.</returns>
    public async Task<string?> InitiateAsync(
        Persona persona,
        Guid chatGroupId,
        IChatGroupGrain chatGroupGrain,
        InFlightStore store,
        string intent,
        string? feeling,
        CancellationToken ct)
    {
        var partyId = await chatGroupGrain.GetPartyIdAsync();
        var roomCast = await chatGroupGrain.GetCastAsync();
        if (roomCast.Speaker(persona.Id) is not { } self)
            return null;

        var participantViews = roomCast.Views();

        using var turnSpan = Tracing.Persona.StartActivity("persona.initiate", ActivityKind.Internal);
        turnSpan?.SetTag("persona.id", persona.Id);
        turnSpan?.SetTag("persona.name", persona.Name);
        turnSpan?.SetTag("chat_group.id", chatGroupId);

        var messageId = await chatGroupGrain.BeginTurnAsync(persona.Id);
        var newCts = new CancellationTokenSource();
        var inFlight = store.Register(chatGroupId, messageId, newCts);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, newCts.Token);
        var linkedCt = linkedCts.Token;

        var appraisalJson = JsonSerializer.Serialize(new
        {
            personaId = persona.Id,
            instruction = intent,
            reason = feeling,
            stop = false,
            initiative = true
        }, WebOptions);

        try
        {
            var history = await chatGroupGrain.GetMessagesUntilAsync(messageId, listener: persona.Id);
            var scenario = await chatGroupGrain.GetScenarioAsync();
            var presentIds = participantViews.Select(p => p.Id).ToList();

            var beat = await memoryRepository.RecallBeatAsync(
                new BeatAnchors(persona.Id, partyId, presentIds, intent, await PlaceOfAsync(roomCast, partyId, chatGroupId)),
                memoryLimit: 5, stanceLimit: 5, logger, linkedCt);
            beat = await FreshenAsync(beat, partyId, persona.Id);
            // Not a story told in the last few hours; carrying it counts as telling it (refractory, ADR 0021).
            var carried = beat.Memories.FirstOrDefault(r => r.Arm == RecallArm.Relevant
                && (r.LastRecalled is not { } at || (DateTimeOffset.UtcNow - at).TotalHours >= SalienceMath.RefractoryHours));
            var memory = carried?.Snippet;
            if (carried is { EdgeId: var edge } && edge != Guid.Empty) _ = StrengthenPickedMemoryAsync(edge, persona.Name);
            IReadOnlyList<string> stanceLines = beat.Stances.Select(StanceBlock.FormatLine).ToList();

            inFlight.MarkGenerationStarted(feeling, intent);
            var result = await RunSpeakingPhaseAsync(
                chatGroupGrain, chatGroupId, messageId, self, participantViews, history,
                intent, feeling, scenario, memory, stanceLines, persona.Name, inFlight, linkedCt);

            await chatGroupGrain.EndTurnAsync(messageId, new TurnOutcome.Spoke(
                result.Message ?? string.Empty, result.Reasoning, appraisalJson, result.Metadata));
            followThrough?.Consider(partyId, chatGroupId, persona.Id);
            return result.Message;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Persona {PersonaName} initiative in {ChatGroupId} was cut off", persona.Name, chatGroupId);
            await chatGroupGrain.EndTurnAsync(messageId, new TurnOutcome.Declined(appraisalJson));
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Persona {PersonaName} initiative failed", persona.Name);
            await chatGroupGrain.EndTurnAsync(messageId, new TurnOutcome.Failed(ex.Message));
            throw;
        }
        finally
        {
            store.Remove(chatGroupId, messageId);
        }
    }

    // Arm attribution as logged/papertrailed (ADR 0015 two-arm union observability).
    private static string ArmTag(RecallArm arm)
        => arm == RecallArm.Relevant ? "relevant" : "recent";

    /// <summary>
    /// Fire-and-forget strengthening write (ADR 0015): a pick increments the edge's
    /// recall_count and stamps last_recalled. Detached from the turn's cancellation scope
    /// on purpose — a race-cancel after the pick shouldn't lose the attention signal.
    /// </summary>
    private async Task StrengthenPickedMemoryAsync(Guid recollectionId, string? personaName)
    {
        try
        {
            await memoryRepository.StrengthenRecollectionAsync(recollectionId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Strengthening write failed for recollection {RecollectionId} (persona {PersonaName})",
                recollectionId, personaName);
        }
    }

    private async Task<SpeakingResult> RunSpeakingPhaseAsync(
        IChatGroupGrain chatGroupGrain,
        Guid chatGroupId,
        int messageId,
        SelfView self,
        IReadOnlyList<ParticipantView> fullParticipants,
        IReadOnlyList<ChatMessage> history,
        string? turnInstruction,
        string? gutReaction,
        string? scenario,
        string? memoryToReference,
        IReadOnlyList<string> stances,
        string personaName,
        InFlightGeneration inFlight,
        CancellationToken ct)
    {
        var router = grainFactory.GetGrain<ILlmRouterGrain>(0);
        var session = new SpeakingSession(router, fullParticipants);
        var presence = await PresenceAsync(chatGroupGrain, self, scenario, fullParticipants);
        if (LivedSheets.Lived(self.Id, self.SystemPrompt) is { } lived)
            self = self with { Bio = null, SystemPrompt = lived };
        var avoidWords = await StaleWordsAsync(chatGroupGrain, self.Id);
        // Only the live exchange (or what they just scrolled back to) is in mind; older talk comes through recall.
        var held = ChatAttention.Window(history, await ReadFromAsync(chatGroupGrain, chatGroupId, self.Id)).Held;

        // Wrap the streaming callback so each content chunk also feeds the in-flight
        // record. The race-trigger snapshot reads token count + accumulated text from
        // there to score salience and determine PNR.
        Task TrackingOnEvent(string eventType, string data, bool done)
        {
            if (eventType == LlmGenerationEvent.ContentChunk && !string.IsNullOrEmpty(data))
                inFlight.AppendChunk(data);
            return NotifyStreamAsync(chatGroupGrain, chatGroupId, messageId, eventType, data, done);
        }

        const int maxRetries = 2;
        SpeakingResult? result = null;
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Each retry restreams from scratch — reset the in-flight accumulator so
                // the race sees only the current attempt's text, not the discarded one.
                if (attempt > 0)
                    inFlight.ResetGeneratedText();

                result = await session.GenerateResponseOnlyAsync(
                    self,
                    held,
                    onEvent: TrackingOnEvent,
                    ct,
                    turnInstruction,
                    scenario,
                    memoryToReference,
                    stances,
                    gutReaction,
                    presence,
                    avoidWords);
                break;
            }
            catch (OperationCanceledException)
            {
                throw; // manual cancel — don't retry, don't emit retry event
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                logger.LogWarning(ex, "Persona {PersonaName} generation attempt {Attempt} failed, retrying", personaName, attempt + 1);
                // Tell the frontend to clear any partial chunks it buffered during this attempt
                // before the next attempt begins restreaming from scratch.
                await NotifyStreamAsync(chatGroupGrain, chatGroupId, messageId,
                    MessageStreamEvent.GenerationRetry,
                    JsonSerializer.Serialize(new { attempt = attempt + 1, nextAttemptInSeconds = 2 * (attempt + 1) }, WebOptions),
                    false);
                await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)), ct);
            }
        }

        _ = NoteSpokeAsync(chatGroupGrain, self, fullParticipants, history);
        return result!;
    }

    /// <summary>The Lived self block (ADR 0021): day, mood, battery, stale words, thermostat cue. Non-fatal; scenario Rooms own time and place, so no day there.</summary>
    private async Task<string?> PresenceAsync(IChatGroupGrain chatGroupGrain, SelfView self, string? scenario, IReadOnlyList<ParticipantView> present)
    {
        try
        {
            var partyId = await chatGroupGrain.GetPartyIdAsync();
            var key = WorldKeys.Life(partyId, self.Id);
            var presenceTask = grainFactory.GetGrain<IPersonaSelfGrain>(key)
                .GetPresenceAsync(withDay: string.IsNullOrWhiteSpace(scenario));
            var tiesTask = grainFactory.GetGrain<IPersonaTiesGrain>(key).RenderAsync(present.Select(p => p.Id).ToList());
            var cueTask = grainFactory.GetGrain<IConversationThermostatGrain>(chatGroupGrain.GetPrimaryKey()).GetCueAsync();
            var (presence, ties, cue) = (await presenceTask, await tiesTask, await cueTask);
            // Money and fresh gifts (ADR 0024) only in the World, where there's a wallet to speak of.
            if (string.IsNullOrWhiteSpace(scenario))
            {
                presence += World.Economy.Money.Render(await grainFactory.GetGrain<World.Economy.IWalletGrain>(key).GetAsync(), DateTimeOffset.UtcNow);
                if (responsibilities is not null)
                    presence += await responsibilities.RoleAsync(partyId, self.Id) + await responsibilities.RenderAsync(partyId, self.Id, DateTimeOffset.UtcNow);
            }
            var block = string.IsNullOrWhiteSpace(ties) ? presence : $"{presence}\n{ties}";
            return string.IsNullOrWhiteSpace(cue) ? block : $"{block}\n# The conversation\n{cue}\n";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Presence failed for {PersonaName}; speaking without it", self.Name);
            return null;
        }
    }

    /// <summary>A sleeping Body (ADR 0020) only wakes for a human; persona talk doesn't reach them.</summary>
    private async Task<bool> SleepsThroughAsync(
        IChatGroupGrain chatGroupGrain, SelfView self, IReadOnlyList<ChatMessage> history, IReadOnlyList<ParticipantView> participants)
    {
        try
        {
            var trigger = history.LastOrDefault(m => m.SenderId != self.Id && !string.IsNullOrWhiteSpace(m.Content));
            if (trigger is null || participants.Any(p => p.Id == trigger.SenderId && p.Driver == DriverKind.User)) return false;
            var partyId = await chatGroupGrain.GetPartyIdAsync();
            return await grainFactory.GetGrain<World.Places.IPlacesGrain>(partyId).IsAsleepAsync(self.Id);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Sleep check failed for {PersonaName}", self.Name);
            return false;
        }
    }

    private async Task<IReadOnlyList<string>> StaleWordsAsync(IChatGroupGrain chatGroupGrain, Guid personaId)
    {
        try
        {
            var partyId = await chatGroupGrain.GetPartyIdAsync();
            return await grainFactory.GetGrain<IWorldVoiceGrain>(partyId).GetStaleWordsAsync(personaId);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Stale words lookup failed");
            return Array.Empty<string>();
        }
    }

    /// <summary>One story per slot plus stale-word habituation on recall (ADR 0021); non-fatal.</summary>
    private async Task<RecallBeat> FreshenAsync(RecallBeat beat, Guid partyId, Guid personaId)
    {
        try
        {
            var names = (await grainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync()).Select(c => c.Name).ToList();
            return StaleRecall.Apply(RecallVariety.Apply(beat, names), await grainFactory.GetGrain<IWorldVoiceGrain>(partyId).GetStaleWordsAsync(personaId));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Stale recall filter failed");
            return beat;
        }
    }

    private async Task<bool> WindingDownAsync(IChatGroupGrain chatGroupGrain)
    {
        try { return await grainFactory.GetGrain<IConversationThermostatGrain>(chatGroupGrain.GetPrimaryKey()).IsWindingDownAsync(); }
        catch { return false; }
    }

    private async Task<int?> ReadFromAsync(IChatGroupGrain chatGroupGrain, Guid chatGroupId, Guid personaId)
    {
        try
        {
            var partyId = await chatGroupGrain.GetPartyIdAsync();
            var looks = await grainFactory.GetGrain<IPersonaAttentionGrain>(WorldKeys.Life(partyId, personaId)).GetLooksAsync();
            return ChatAttention.ReadFrom(looks.GetValueOrDefault(chatGroupId), DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Attention lookup failed for {PersonaId}", personaId);
            return null;
        }
    }

    private async Task NoteSpokeAsync(IChatGroupGrain chatGroupGrain, SelfView self, IReadOnlyList<ParticipantView> present, IReadOnlyList<ChatMessage> history)
    {
        try
        {
            var partyId = await chatGroupGrain.GetPartyIdAsync();
            var key = WorldKeys.Life(partyId, self.Id);
            await grainFactory.GetGrain<IPersonaSelfGrain>(key).NoteSpokeAsync(self.Chattiness);
            // An exchange needs both sides: only people who spoke lately in this conversation count.
            var spoke = history.TakeLast(10).Where(m => !string.IsNullOrWhiteSpace(m.Content)).Select(m => m.SenderId).ToHashSet();
            var others = present.Where(p => p.Id != self.Id && p.Driver != DriverKind.System && spoke.Contains(p.Id)).Select(p => p.Id).ToList();
            if (others.Count > 0)
                await grainFactory.GetGrain<IPersonaTiesGrain>(key).NoteExchangeAsync(others, direct: present.Count == 2);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Social battery update failed for {PersonaName}", self.Name);
        }
    }

    /// <summary>
    /// Mirrors <see cref="IChatGroupGrain.CountTrailingAssistantMessagesAsync"/> against an
    /// in-hand history snapshot. Skips empty-content stubs (reserved slots) and stops at the
    /// first non-assistant message.
    /// </summary>
    private static int CountTrailingAssistantMessages(IReadOnlyList<ChatMessage> history)
    {
        var count = 0;
        for (var i = history.Count - 1; i >= 0; i--)
        {
            var msg = history[i];
            if (msg.SenderType != "assistant") break;
            if (string.IsNullOrEmpty(msg.Content)) continue;
            count++;
        }
        return count;
    }

    private async Task<ShouldRespondResult> RunDecisionPhaseAsync(
        IChatGroupGrain chatGroupGrain,
        Guid chatGroupId,
        int messageId,
        SelfView self,
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ParticipantView> participants,
        bool isDirect,
        string? scenario,
        IReadOnlyList<string> recollections,
        CancellationToken ct,
        RepairHint? repairHint = null,
        IReadOnlyList<string>? stances = null)
    {
        var router = grainFactory.GetGrain<ILlmRouterGrain>(0);
        var decisionService = new PersonaDecisionService(router, loggerFactory.CreateLogger<PersonaDecisionService>());
        // Derive from the same snapshot as `history` so the decision sees a consistent
        // view — re-reading live grain state here could count a sibling persona's append
        // that isn't in `history`.
        var totalAiRounds = CountTrailingAssistantMessages(history);
        if (await SleepsThroughAsync(chatGroupGrain, self, history, participants))
            return new ShouldRespondResult { Respond = false, Instruction = string.Empty, Reason = "Asleep." };
        var presence = await PresenceAsync(chatGroupGrain, self, scenario, participants);
        if (LivedSheets.Lived(self.Id, self.SystemPrompt) is { } lived && LivedSheets.Bio(lived) is { } livedBio)
            self = self with { Bio = livedBio };
        var windingDown = await WindingDownAsync(chatGroupGrain);

        return await decisionService.ShouldRespondAsync(
            self,
            history,
            participants,
            totalAiRounds,
            onEvent: (eventType, data, done) => NotifyStreamAsync(chatGroupGrain, chatGroupId, messageId, eventType, data, done),
            cancellationToken: ct,
            scenario: scenario,
            repairHint: repairHint,
            recollections: recollections,
            stances: stances,
            isDirect: isDirect,
            presence: presence,
            windingDown: windingDown);
    }

    private static Task NotifyStreamAsync(
        IChatGroupGrain chatGroupGrain,
        Guid chatGroupId,
        int messageId,
        string eventType,
        string data,
        bool done)
        => chatGroupGrain.NotifyStreamChunkAsync(messageId, new MessageStreamEvent
        {
            ChatGroupId = chatGroupId,
            Event = eventType,
            Data = data,
            Done = done
        });

    /// <summary>A Place talk Room anchors recall on its Place; other Rooms don't.</summary>
    /// <summary>The talk Room's Place as a recall anchor; non-fatal like recall itself.</summary>
    private async Task<Guid?> PlaceOfAsync(RoomCast roomCast, Guid partyId, Guid chatGroupId)
    {
        if (roomCast.Kind != ChatGroupKinds.Place) return null;
        try
        {
            return await grainFactory.GetGrain<World.Places.IPlacesGrain>(partyId).GetPlaceOfTalkRoomAsync(chatGroupId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Place lookup failed for chat group {ChatGroupId}; recalling without it", chatGroupId);
            return null;
        }
    }
}
