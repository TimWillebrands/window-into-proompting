using System.Diagnostics;
using System.Text.Json;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Logging;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.Services.Papertrail;
using PartyTown.Services.Streaming;

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
    ILogger<ResponsePipeline> logger)
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

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
        var partyGrain = grainFactory.GetGrain<IPartyGrain>(partyId);
        var castTask = partyGrain.GetCastAsync();
        var overridesTask = chatGroupGrain.GetDriverOverridesAsync();
        await Task.WhenAll(castTask, overridesTask);
        var cast = castTask.Result;
        var overrides = overridesTask.Result;

        var selfCast = cast.FirstOrDefault(c => c.Id == persona.Id);
        if (selfCast is null)
        {
            logger.LogDebug(
                "Persona {PersonaName} not in cast for party {PartyId}; skipping notify",
                persona.Name, partyId);
            return;
        }

        var selfEffective = selfCast.EffectiveIn(overrides);
        // Only LLM-Effective Participants run the response pipeline. User-Effective is
        // human-typed; System-Effective (Narrator) is authored content that never
        // auto-speaks (ADR 0012). Both short-circuit here so the rule lives in one place.
        if (selfEffective != DriverKind.LLM)
        {
            logger.LogDebug(
                "Persona {PersonaName} is {Driver}-Effective in chat group {ChatGroupId}; skipping pipeline",
                persona.Name, selfEffective, chatGroupId);
            return;
        }

        var self = selfCast.ToSelfView(selfEffective);
        var participantViews = cast.Select(c => c.ToView(c.EffectiveIn(overrides))).ToList();

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
        var preHistory = await chatGroupGrain.GetMessagesUntilAsync(int.MaxValue);
        var preRounds = await chatGroupGrain.CountTrailingAssistantMessagesAsync();

        var preUrge = UrgeMath.CalculateResponseUrge(self, preHistory, preRounds);
        var preRecentSelf = UrgeMath.CountRecentSelfMessages(preHistory, persona.Id);
        turnSpan?.SetTag("urge.total", preUrge.Total);
        turnSpan?.SetTag("urge.mention", preUrge.MentionScore);
        turnSpan?.SetTag("urge.question", preUrge.QuestionScore);
        turnSpan?.SetTag("urge.silence_streak", preUrge.SilenceStreakScore);
        turnSpan?.SetTag("urge.cold_open", preUrge.ColdOpenScore);
        turnSpan?.SetTag("rounds.total_assistant", preRounds);
        turnSpan?.SetTag("rounds.recent_self", preRecentSelf);

        if (UrgeMath.IsObviousSkip(preUrge, preRounds, preRecentSelf))
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

        var messageId = await chatGroupGrain.GetNextMessageIdAsync(
            persona.Id, "assistant", triggeringMessage.MessageId);
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
            var history = await chatGroupGrain.GetMessagesUntilAsync(messageId);
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

            // ADR 0015 recall: top-10 salience-ranked Recollections for this Persona scoped
            // to the Party (cross-Room within one Party). Salience orders the candidates;
            // the decision LLM still judges beat-relevance in context. Recall failure is
            // non-fatal — log + continue with an empty list so a memory outage never blocks
            // the persona from responding.
            IReadOnlyList<RecalledMemory> recollections;
            try
            {
                recollections = await memoryRepository.RecallAsync(persona.Id, partyId, limit: 10, linkedCt);
            }
            catch (OperationCanceledException) when (linkedCt.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Recall failed for persona {PersonaName} in party {PartyId}; proceeding without recollections",
                    persona.Name, partyId);
                recollections = Array.Empty<RecalledMemory>();
            }
            turnSpan?.SetTag("recall.candidate_count", recollections.Count);

            // Observability (ADR 0015): the candidate set is what makes recall misses
            // visible — this is the embeddings-deferral contract's trigger instrument.
            if (recollections.Count > 0)
            {
                logger.LogInformation(
                    "Recall for persona {PersonaName} in party {PartyId}: {Candidates}",
                    persona.Name, partyId,
                    string.Join(", ", recollections.Select((r, i) => $"#{i + 1} {r.EdgeId:N}={r.Salience:F3}")));
            }

            // ADR 0016 stance floor: anchor-scoped, latest-wins Stance lines for the ambient
            // "# Where you stand" block. Anchors are the beat's live cast (present persona ids,
            // self included) + Concepts named in the triggering message. Pure DB read — zero
            // LLM calls on the beat path. Non-fatal like recall: a memory outage never mutes
            // the persona.
            IReadOnlyList<string> stanceLines;
            try
            {
                var presentPersonaIds = participantViews.Select(p => p.Id).ToList();
                var stances = await memoryRepository.RecallStancesAsync(
                    partyId, persona.Id, presentPersonaIds,
                    triggeringMessage.Content ?? string.Empty, limit: 5, linkedCt);
                stanceLines = stances.Select(s => s.Reasoning).ToList();
            }
            catch (OperationCanceledException) when (linkedCt.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Stance recall failed for persona {PersonaName} in party {PartyId}; proceeding without stances",
                    persona.Name, partyId);
                stanceLines = Array.Empty<string>();
            }
            turnSpan?.SetTag("stance.line_count", stanceLines.Count);

            var decision = await RunDecisionPhaseAsync(
                chatGroupGrain, chatGroupId, messageId, self, history, decisionParticipants, scenario,
                recollections.Select(r => r.Snippet).ToList(), linkedCt,
                repairHint, stanceLines);

            // Resolve the index pick (1-based into the numbered block; decision service
            // already range-clamped) back to the recalled memory: snippet → speaking phase,
            // edge id → strengthening write.
            var pickedMemory = decision.MemoryToReference is int pickIndex
                ? recollections[pickIndex - 1]
                : null;
            turnSpan?.SetTag("recall.picked_index", decision.MemoryToReference);
            if (pickedMemory is not null)
            {
                logger.LogInformation(
                    "Persona {PersonaName} picked memory #{Index} ({EdgeId}, salience {Salience:F3})",
                    persona.Name, decision.MemoryToReference, pickedMemory.EdgeId, pickedMemory.Salience);

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
                }).ToList(),
                Picked = decision.MemoryToReference,
                PickedId = pickedMemory?.EdgeId,
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
                await chatGroupGrain.MarkGenerationStoppedAsync(messageId, declinedAppraisal, triggeringMessage.MessageId);
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
            // decision phase selects, speaking phase executes.
            var result = await RunSpeakingPhaseAsync(
                chatGroupGrain, chatGroupId, messageId, self, participantViews, history,
                decision.Instruction, scenario, pickedMemory?.Snippet, stanceLines, persona.Name, inFlight, linkedCt);

            var appraisalJson = JsonSerializer.Serialize(new
            {
                personaId = persona.Id,
                instruction = decision.Instruction,
                reason = decision.Reason,
                stop = false,
                recall = recallLog
            }, WebOptions);

            await chatGroupGrain.AppendMessageAsync(
                messageId,
                result.Message ?? string.Empty,
                result.Reasoning,
                appraisalJson,
                result.Metadata,
                triggeringMessage.MessageId,
                linkedCt);

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

                await chatGroupGrain.MarkGenerationCancelledAsEmoteAsync(
                    messageId, emote, emoteAppraisal, triggeringMessage.MessageId);
            }
            else
            {
                turnSpan?.SetTag("decision", "cancelled");
                logger.LogDebug("Persona {PersonaName} generation cancelled (external)", persona.Name);
                await chatGroupGrain.MarkGenerationFailedAsync(messageId, "cancelled");
            }
        }
        catch (Exception ex)
        {
            turnSpan?.SetStatus(ActivityStatusCode.Error, ex.Message);
            turnSpan?.SetTag("decision", "error");
            logger.LogError(ex, "Persona {PersonaName} generation failed", persona.Name);
            // FIXME: If at some point we go public, don't send exceptions over the wire
            await chatGroupGrain.MarkGenerationFailedAsync(messageId, ex.ToString());
        }
        finally
        {
            store.Remove(chatGroupId, messageId);
        }
    }

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
        string? scenario,
        string? memoryToReference,
        IReadOnlyList<string> stances,
        string personaName,
        InFlightGeneration inFlight,
        CancellationToken ct)
    {
        var router = grainFactory.GetGrain<ILlmRouterGrain>(0);
        var session = new SpeakingSession(router, fullParticipants);

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
                    history,
                    onEvent: TrackingOnEvent,
                    ct,
                    turnInstruction,
                    scenario,
                    memoryToReference,
                    stances);
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

        return result!;
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
            stances: stances);
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
}
