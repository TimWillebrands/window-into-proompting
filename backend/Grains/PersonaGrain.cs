using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Orleans.Concurrency;
using PartyTown.Grains.Generation;
using PartyTown.Logging;
using PartyTown.Model;
using PartyTown.Services.Generation;
using PartyTown.Services.Memory;
using PartyTown.Services.Streaming;

namespace PartyTown.Grains;

/// <summary>
/// Grain that stores a single persona's data and reacts to messages.
/// Marked [Reentrant] so multiple concurrent NotifyMessageAsync calls don't deadlock,
/// and so CancelGenerationAsync can interrupt an in-flight NotifyMessageAsync.
/// </summary>
[Reentrant]
public sealed class PersonaGrain(
    [PersistentState(stateName: "persona", storageName: "personas")]
    IPersistentState<Persona> state,
    ILoggerFactory loggerFactory,
    IMemoryRepository memoryRepository,
    ILogger<PersonaGrain> logger)
    : Grain, IPersonaGrain
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    // Stop-signal race tunables. PNR is the absolute generated-token count past which an
    // in-flight generation cannot be cancelled (only repaired on the next turn). Cancel
    // threshold is the cancelScore above which the race elects to cancel.
    // See plans/well-i-controll-henk-eventual-stearns.md for derivation.
    private const int PnrTokens = 80;
    private const double CancelThreshold = 0.5;

    // One CTS per *in-flight generation* (chatGroup, messageId), not per chat group.
    // Earlier (per-chat-group) keying caused message N+1 to cancel message N's still-running
    // decision/speaking, surfacing as a phantom "cancelled" appraisal on legitimate work
    // and an empty assistant slot for any persona slow enough to overlap a follow-up.
    // CancelGenerationAsync still cancels every in-flight generation for this persona.
    private readonly ConcurrentDictionary<(Guid chatGroupId, int messageId), CancellationTokenSource> _ctsByGeneration = new();

    // Parallel structure to _ctsByGeneration carrying race-relevant state for each
    // in-flight generation: which phase (decision/speaking), the gut reaction +
    // wouldSay preview captured after decision, and the streaming text + token count
    // updated during the speaking phase. Read by RunStopSignalRaceAsync to score new
    // messages against the in-flight utterance.
    private readonly ConcurrentDictionary<(Guid chatGroupId, int messageId), InFlightGeneration> _inFlight = new();

    // Levelt-style repair hints, keyed by chat group. Set when a new message arrives
    // during in-flight generation and the race elects NOT to cancel (either past PNR
    // or salience didn't justify interruption). Consumed once on the next decision pass
    // for that chat group, then cleared regardless of decision outcome.
    private readonly ConcurrentDictionary<Guid, RepairHint> _pendingRepairByGroup = new();

    public Task CancelGenerationAsync()
    {
        foreach (var cts in _ctsByGeneration.Values)
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        }
        return Task.CompletedTask;
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "PersonaGrain activated - Name: '{PersonaName}' - Id: '{PersonaId}'",
            state.State.Name,
            this.GetPrimaryKey());
        return base.OnActivateAsync(cancellationToken);
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "PersonaGrain deactivating: {Reason} - Id: '{PersonaId}'",
            reason.ReasonCode,
            this.GetPrimaryKey());
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    public Task SetPersona(Persona persona) =>
        SetPersona(persona.Name, persona.SystemPrompt, persona.Bio);

    public async Task SetPersona(string name, string systemPrompt, string? bio)
        => await UpdateStateAsync(current => current with
        {
            Id = this.GetPrimaryKey(),
            Name = name,
            SystemPrompt = systemPrompt,
            Bio = bio
        });

    public async Task SetName(string name)
        => await UpdateStateAsync(current => current with
        {
            Id = this.GetPrimaryKey(),
            Name = name
        });

    public async Task SetSystemPrompt(string systemPrompt)
        => await UpdateStateAsync(current => current with
        {
            Id = this.GetPrimaryKey(),
            SystemPrompt = systemPrompt
        });

    public async Task SetBio(string? bio)
        => await UpdateStateAsync(current => current with
        {
            Id = this.GetPrimaryKey(),
            Bio = bio
        });

    public Task<Persona> GetPersona() =>
        Task.FromResult(state.State with
        {
            Id = this.GetPrimaryKey()
        });

    public Task DeletePersona() =>
        state.ClearStateAsync();

    /// <summary>
    /// Called by ChatGroupGrain when a new message arrives in the chat group.
    /// The persona independently decides whether to respond and generates if yes.
    /// </summary>
    public async Task NotifyMessageAsync(Guid chatGroupId, ChatMessage triggeringMessage, CancellationToken ct = default)
    {
        var personaId = this.GetPrimaryKey();
        var persona = state.State with { Id = personaId };

        // Defense-in-depth: also drop self-fan-out here. ChatGroupGrain filters first,
        // but a stray direct-call shouldn't make Vlad ruminate on Vlad's last line.
        if (triggeringMessage.SenderId == personaId)
            return;

        // One root span per persona per triggering message. The fan-out at ChatGroupGrain
        // does NOT wrap these in a parent span on purpose — each persona reaction is an
        // independent root so the Aspire timeline shows them as siblings, mirroring reality.
        using var turnSpan = Tracing.Persona.StartActivity("persona.turn", ActivityKind.Internal);
        turnSpan?.SetTag("persona.id", personaId);
        turnSpan?.SetTag("persona.name", persona.Name);
        turnSpan?.SetTag("chat_group.id", chatGroupId);
        turnSpan?.SetTag("triggered_by.message_id", triggeringMessage.MessageId);
        turnSpan?.SetTag("triggered_by.sender_id", triggeringMessage.SenderId);

        logger.LogInformation("Persona {PersonaName} notified of message {MessageId} in chat group {ChatGroupId}",
            persona.Name, triggeringMessage.MessageId, chatGroupId);

        var chatGroupGrain = GrainFactory.GetGrain<IChatGroupGrain>(chatGroupId);

        // Stop-signal race: if any in-flight generation exists for this persona in this
        // group, decide cancel-vs-continue against the new message. Runs BEFORE the
        // pre-gate so the in-flight work is interrupted even when this persona would
        // ultimately skip the new message itself (correctness > saved compute).
        await RunStopSignalRaceAsync(chatGroupId, triggeringMessage, persona, chatGroupGrain, ct);

        // Pre-gate: snapshot history before reserving a slot so an obvious-skip persona
        // leaves no trace in the chat (no message slot → no thought-log entry → no LLM call).
        // Reading int.MaxValue gives us the full current history without claiming a slot.
        var preHistory = await chatGroupGrain.GetMessagesUntilAsync(int.MaxValue);
        var preRounds = await chatGroupGrain.CountTrailingAssistantMessagesAsync();

        var self = new GenerationParticipant
        {
            Id = personaId,
            Name = persona.Name,
            Bio = persona.Bio,
            SystemPrompt = persona.SystemPrompt,
            IsUser = false,
            Chattiness = persona.Chattiness,
            Impulsivity = persona.Impulsivity
        };

        var preUrge = PersonaDecisionService.CalculateResponseUrge(self, preHistory, preRounds);
        var preRecentSelf = PersonaDecisionService.CountRecentSelfMessages(preHistory, personaId);
        turnSpan?.SetTag("urge.total", preUrge.Total);
        turnSpan?.SetTag("urge.mention", preUrge.MentionScore);
        turnSpan?.SetTag("urge.question", preUrge.QuestionScore);
        turnSpan?.SetTag("urge.silence_streak", preUrge.SilenceStreakScore);
        turnSpan?.SetTag("urge.cold_open", preUrge.ColdOpenScore);
        turnSpan?.SetTag("rounds.total_assistant", preRounds);
        turnSpan?.SetTag("rounds.recent_self", preRecentSelf);

        if (PersonaDecisionService.IsObviousSkip(preUrge, preRounds, preRecentSelf))
        {
            turnSpan?.SetTag("decision", "skip-obvious");
            var skipReason = $"obvious-skip (rounds={preRounds}, recentSelf={preRecentSelf})";
            logger.LogDebug(
                "Persona {PersonaName} silently skipped (urge={Urge:F2}, rounds={Rounds}, recentSelf={Self})",
                persona.Name, preUrge.Total, preRounds, preRecentSelf);
            // Persist the skip into the chat group so the papertrail can surface every
            // persona's reaction to a triggering message, not just the ones that produced text.
            try
            {
                await chatGroupGrain.RecordSkippedTurnAsync(
                    personaId, persona.Name ?? string.Empty,
                    triggeringMessage.MessageId, preUrge.Total, skipReason);
            }
            catch (Exception ex)
            {
                // Never let papertrail bookkeeping break the silence path.
                logger.LogDebug(ex, "Failed to record skip for persona {PersonaName}", persona.Name);
            }
            return;
        }

        var messageId = await chatGroupGrain.GetNextMessageIdAsync(
            personaId, "assistant", triggeringMessage.MessageId);
        turnSpan?.SetTag("result.message_id", messageId);

        var newCts = new CancellationTokenSource();
        _ctsByGeneration[(chatGroupId, messageId)] = newCts;

        // Race-trigger state: register before any awaits so a concurrent NotifyMessageAsync
        // can race against this one. Mutated in-place as decision → generation → done.
        var inFlight = new InFlightGeneration(newCts);
        _inFlight[(chatGroupId, messageId)] = inFlight;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, newCts.Token);
        var linkedCt = linkedCts.Token;

        try
        {
            // Single history snapshot, shared by decision + generation (issue #4).
            // Taking the snapshot at `messageId` includes any messages other personas
            // committed between our slot reservation and our read.
            var history = await chatGroupGrain.GetMessagesUntilAsync(messageId);
            var participants = await chatGroupGrain.GetParticipantsAsync();
            var scenario = await chatGroupGrain.GetScenarioAsync();

            var decisionParticipants = BuildDecisionParticipants(participants, personaId, self);

            await NotifyStreamAsync(chatGroupGrain, chatGroupId, messageId,
                MessageStreamEvent.PersonaEvaluatingResponse, personaId.ToString(), false);

            // Consume any pending repair hint for this group — one-shot, cleared regardless
            // of decision outcome so it can't double-fire.
            RepairHint? repairHint = _pendingRepairByGroup.TryRemove(chatGroupId, out var hint) ? hint : null;
            if (repairHint is not null)
                turnSpan?.SetTag("repair.missed_message_id", repairHint.Value.MissedMessageId);

            // ADR 0009 MVP recall: top-10 most recent Recollections for this Persona scoped
            // to the Party (cross-Room within one Party). The decision LLM judges relevance
            // in context. Recall failure is non-fatal — log + continue with an empty list so
            // a memory outage never blocks the persona from responding.
            var partyId = await chatGroupGrain.GetPartyIdAsync();
            IReadOnlyList<string> recollections;
            try
            {
                recollections = await memoryRepository.RecallRecentSnippetsAsync(personaId, partyId, limit: 10, linkedCt);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Recall failed for persona {PersonaName} in party {PartyId}; proceeding without recollections", persona.Name, partyId);
                recollections = Array.Empty<string>();
            }
            turnSpan?.SetTag("recall.snippet_count", recollections.Count);

            var decision = await RunDecisionPhaseAsync(
                chatGroupGrain, chatGroupId, messageId, self, history, decisionParticipants, scenario, recollections, linkedCt,
                repairHint);

            if (!decision.Respond)
            {
                turnSpan?.SetTag("decision", "skip-llm");
                turnSpan?.SetTag("decision.reason", decision.Reason);
                logger.LogInformation("Persona {PersonaName} decided NOT to respond: {Reason}", persona.Name, decision.Reason);
                // Mirror the success-path appraisal shape so the papertrail renderer can
                // surface the gut reaction uniformly. Plain-string appraisal would fail
                // TryParseAppraisal silently and drop the reason from the rendered output.
                var declinedAppraisal = JsonSerializer.Serialize(new
                {
                    personaId,
                    instruction = (string?)null,
                    reason = decision.Reason,
                    stop = true
                }, WebOptions);
                await chatGroupGrain.MarkGenerationStoppedAsync(messageId, declinedAppraisal, triggeringMessage.MessageId);
                await NotifyStreamAsync(chatGroupGrain, chatGroupId, messageId,
                    MessageStreamEvent.PersonaDeclinedResponse,
                    JsonSerializer.Serialize(new { personaId, reason = decision.Reason }, WebOptions),
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
                    personaId,
                    instruction = decision.Instruction,
                    reason = decision.Reason,
                    stop = false
                }, WebOptions),
                false);

            // Decision committed to "speak". Promote the in-flight record so the race
            // sees it as Speaking phase and has the gut/preview to feed salience.
            // (The InFlightPhase enum still spells the value `Generation` — see ADR 0010
            // legacy-spelling note; rename deferred to a structural-cleanup PR.)
            inFlight.MarkGenerationStarted(decision.Reason, decision.Instruction);

            var fullParticipants = await BuildGenerationParticipantsAsync(participants, personaId);
            // decision.MemoryToReference is the recollection the decision LLM picked to weave
            // into this beat (null when nothing fit). Passing it as a dedicated argument keeps
            // the contract explicit: decision phase selects, speaking phase executes.
            var result = await RunSpeakingPhaseAsync(
                chatGroupGrain, chatGroupId, messageId, self, fullParticipants, history,
                decision.Instruction, scenario, decision.MemoryToReference, persona.Name, inFlight, linkedCt);

            var appraisalJson = JsonSerializer.Serialize(new
            {
                personaId,
                instruction = decision.Instruction,
                reason = decision.Reason,
                stop = false
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
                        GrainFactory.GetGrain<ILlmRouterGrain>(0),
                        loggerFactory.CreateLogger<PersonaEmoteService>());
                    var selfForEmote = new GenerationParticipant
                    {
                        Id = personaId,
                        Name = persona.Name,
                        Bio = persona.Bio,
                        SystemPrompt = persona.SystemPrompt,
                        IsUser = false,
                        Chattiness = persona.Chattiness,
                        Impulsivity = persona.Impulsivity
                    };
                    // Use the parent ct (not linkedCt — linked is already cancelled by the race).
                    // Stale draft, the race already paid the cost — we still want the emote.
                    emote = await emoteService.GenerateAbandonmentEmoteAsync(
                        selfForEmote,
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
                    personaId,
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
            _ctsByGeneration.TryRemove(new KeyValuePair<(Guid, int), CancellationTokenSource>((chatGroupId, messageId), newCts));
            _inFlight.TryRemove(new KeyValuePair<(Guid, int), InFlightGeneration>((chatGroupId, messageId), inFlight));
            newCts.Dispose();
        }
    }

    private static List<GenerationParticipant> BuildDecisionParticipants(
        IReadOnlyList<PartyParticipant> participants,
        Guid personaId,
        GenerationParticipant self)
        => participants.Select(p => p.Id == personaId
            ? self
            : new GenerationParticipant
            {
                Id = p.Id,
                Name = p.Name,
                IsUser = p.IsUser,
                Bio = null,
                SystemPrompt = null
            }).ToList();

    private async Task<List<GenerationParticipant>> BuildGenerationParticipantsAsync(
        IReadOnlyList<PartyParticipant> participants,
        Guid personaId)
    {
        var personaRoot = GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty);
        var allPersonas = await personaRoot.GetAll();
        var personaMap = allPersonas.ToDictionary(p => p.Id, p => p);

        return participants.Select(p =>
        {
            if (p.IsUser)
            {
                // SystemPrompt left null for users: IsUser carries the distinction, and a literal
                // marker string risked leaking into concatenated prompts downstream (issue #9).
                return new GenerationParticipant { Id = p.Id, Name = p.Name, IsUser = true, SystemPrompt = null, Bio = null };
            }
            if (personaMap.TryGetValue(p.Id, out var pm))
                return new GenerationParticipant { Id = pm.Id, Name = pm.Name, Bio = pm.Bio, SystemPrompt = pm.SystemPrompt, IsUser = false };
            return new GenerationParticipant { Id = p.Id, Name = p.Name, IsUser = false };
        }).ToList();
    }

    private async Task<ShouldRespondResult> RunDecisionPhaseAsync(
        IChatGroupGrain chatGroupGrain,
        Guid chatGroupId,
        int messageId,
        GenerationParticipant self,
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<GenerationParticipant> participants,
        string? scenario,
        IReadOnlyList<string> recollections,
        CancellationToken ct,
        RepairHint? repairHint = null)
    {
        var router = GrainFactory.GetGrain<ILlmRouterGrain>(0);
        var decisionService = new PersonaDecisionService(router, loggerFactory.CreateLogger<PersonaDecisionService>());
        var totalAiRounds = await chatGroupGrain.CountTrailingAssistantMessagesAsync();

        return await decisionService.ShouldRespondAsync(
            self,
            history,
            participants,
            totalAiRounds,
            onEvent: (eventType, data, done) => NotifyStreamAsync(chatGroupGrain, chatGroupId, messageId, eventType, data, done),
            cancellationToken: ct,
            scenario: scenario,
            repairHint: repairHint,
            recollections: recollections);
    }

    private async Task<GenerationResult> RunSpeakingPhaseAsync(
        IChatGroupGrain chatGroupGrain,
        Guid chatGroupId,
        int messageId,
        GenerationParticipant self,
        List<GenerationParticipant> fullParticipants,
        IReadOnlyList<ChatMessage> history,
        string? turnInstruction,
        string? scenario,
        string? memoryToReference,
        string personaName,
        InFlightGeneration inFlight,
        CancellationToken ct)
    {
        var router = GrainFactory.GetGrain<ILlmRouterGrain>(0);
        var session = new GenerationSession(router, fullParticipants);

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
        GenerationResult? result = null;
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
                    memoryToReference);
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
                // before the next attempt begins restreaming from scratch (issue #8).
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
    /// Stop-signal race: when a new message arrives, walk this persona's in-flight
    /// generations in this chat group and decide cancel-vs-continue per generation.
    ///   • Decision phase → always cancel (cheap; no public artifact yet).
    ///   • Speaking phase past PNR → cannot cancel; record a repair hint for next turn.
    ///   • Speaking phase pre-PNR → score salience via LFM2; cancel if cancelScore > 0.5,
    ///     otherwise record a repair hint.
    /// Salience or routing failures default to "let it ride" (no cancel, no repair) —
    /// preserves current behavior rather than introducing a new failure surface.
    /// </summary>
    private async Task RunStopSignalRaceAsync(
        Guid chatGroupId,
        ChatMessage triggeringMessage,
        Persona persona,
        IChatGroupGrain chatGroupGrain,
        CancellationToken ct)
    {
        var snapshot = _inFlight
            .Where(kv => kv.Key.chatGroupId == chatGroupId)
            .Select(kv => (kv.Key.messageId, kv.Value))
            .ToList();

        if (snapshot.Count == 0)
            return;

        string? senderName = null;
        async Task<string> ResolveSenderNameAsync()
        {
            if (senderName is not null) return senderName;
            try
            {
                var participants = await chatGroupGrain.GetParticipantsAsync();
                senderName = participants.FirstOrDefault(p => p.Id == triggeringMessage.SenderId)?.Name
                             ?? triggeringMessage.SenderId.ToString();
            }
            catch
            {
                senderName = triggeringMessage.SenderId.ToString();
            }
            return senderName;
        }

        foreach (var (inFlightMessageId, gen) in snapshot)
        {
            using var raceSpan = Tracing.Persona.StartActivity("persona.race", ActivityKind.Internal);
            raceSpan?.SetTag("persona.id", this.GetPrimaryKey());
            raceSpan?.SetTag("persona.name", persona.Name);
            raceSpan?.SetTag("in_flight.message_id", inFlightMessageId);
            raceSpan?.SetTag("triggered_by.message_id", triggeringMessage.MessageId);

            var snap = gen.Snapshot();
            raceSpan?.SetTag("in_flight.phase", snap.Phase.ToString());
            raceSpan?.SetTag("in_flight.tokens", snap.GeneratedTokens);

            if (snap.Phase == InFlightPhase.Decision)
            {
                raceSpan?.SetTag("race.outcome", "cancel-decision");
                logger.LogInformation(
                    "Race: persona {Name} cancelling in-flight DECISION (msg {Mid}) on new {NewMid}",
                    persona.Name, inFlightMessageId, triggeringMessage.MessageId);
                gen.MarkRaceCancelled(
                    triggeringMessage.Content ?? string.Empty,
                    await ResolveSenderNameAsync());
                try { gen.Cts.Cancel(); } catch (ObjectDisposedException) { }
                await RecordRaceOutcomeAsync(chatGroupGrain, persona,
                    triggeringMessage.MessageId, "cancel-decision", null, null);
                continue;
            }

            // Speaking phase (InFlightPhase.Generation is the legacy enum spelling per ADR 0010)
            if (snap.GeneratedTokens >= PnrTokens)
            {
                // Past point of no return. Stash repair hint without burning a salience call —
                // the message will ship regardless, and the next decision pass will see the
                // hint and the new message in history.
                raceSpan?.SetTag("race.outcome", "past-pnr");
                _pendingRepairByGroup[chatGroupId] = new RepairHint(
                    triggeringMessage.MessageId,
                    await ResolveSenderNameAsync(),
                    triggeringMessage.Content ?? string.Empty);
                await RecordRaceOutcomeAsync(chatGroupGrain, persona,
                    triggeringMessage.MessageId, "past-pnr", null, null);
                continue;
            }

            // Pre-PNR: race
            SalienceScore salience;
            try
            {
                var salienceService = new PersonaSalienceService(
                    GrainFactory.GetGrain<ILlmRouterGrain>(0),
                    loggerFactory.CreateLogger<PersonaSalienceService>());
                var selfParticipant = new GenerationParticipant
                {
                    Id = this.GetPrimaryKey(),
                    Name = persona.Name,
                    Bio = persona.Bio,
                    SystemPrompt = persona.SystemPrompt,
                    IsUser = false,
                    Chattiness = persona.Chattiness,
                    Impulsivity = persona.Impulsivity
                };
                salience = await salienceService.ScoreAsync(
                    selfParticipant,
                    snap.GutReaction,
                    snap.WouldSayPreview,
                    snap.GeneratedText,
                    triggeringMessage,
                    await ResolveSenderNameAsync(),
                    ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Race salience call failed for persona {Name}", persona.Name);
                salience = SalienceScore.LetItRide;
            }

            var commitmentProgress = Math.Min(1.0, snap.GeneratedTokens / (double)PnrTokens);
            var cancelScore = salience.Value * (1.0 - persona.Impulsivity) * (1.0 - commitmentProgress);

            raceSpan?.SetTag("race.salience", salience.Value);
            raceSpan?.SetTag("race.salience.kind", salience.Kind);
            raceSpan?.SetTag("race.impulsivity", persona.Impulsivity);
            raceSpan?.SetTag("race.commitment_progress", commitmentProgress);
            raceSpan?.SetTag("race.cancel_score", cancelScore);

            if (cancelScore > CancelThreshold)
            {
                raceSpan?.SetTag("race.outcome", "cancel-generation");
                logger.LogInformation(
                    "Race: persona {Name} cancelling in-flight GENERATION (msg {Mid}, tokens {Tok}, salience {Sal:F2}, cancelScore {Cs:F2}) on new {NewMid}",
                    persona.Name, inFlightMessageId, snap.GeneratedTokens, salience.Value, cancelScore, triggeringMessage.MessageId);
                gen.MarkRaceCancelled(
                    triggeringMessage.Content ?? string.Empty,
                    await ResolveSenderNameAsync());
                try { gen.Cts.Cancel(); } catch (ObjectDisposedException) { }
                await RecordRaceOutcomeAsync(chatGroupGrain, persona,
                    triggeringMessage.MessageId, "cancel-generation", salience.Value, cancelScore);
            }
            else
            {
                raceSpan?.SetTag("race.outcome", "continue");
                _pendingRepairByGroup[chatGroupId] = new RepairHint(
                    triggeringMessage.MessageId,
                    await ResolveSenderNameAsync(),
                    triggeringMessage.Content ?? string.Empty);
                await RecordRaceOutcomeAsync(chatGroupGrain, persona,
                    triggeringMessage.MessageId, "continue", salience.Value, cancelScore);
            }
        }
    }

    /// <summary>Persist a race outcome to the chat group's thought-log papertrail. Wraps
    /// the call so a transient persistence failure can't bring down the race itself —
    /// the cancel/continue decision has already been applied by this point.</summary>
    private async Task RecordRaceOutcomeAsync(
        IChatGroupGrain chatGroupGrain,
        Persona persona,
        int triggeredByMessageId,
        string outcome,
        double? salience,
        double? cancelScore)
    {
        try
        {
            await chatGroupGrain.RecordRaceEvaluationAsync(
                this.GetPrimaryKey(),
                persona.Name ?? string.Empty,
                triggeredByMessageId,
                outcome,
                salience,
                cancelScore);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex,
                "Failed to record race outcome {Outcome} for persona {PersonaName}",
                outcome, persona.Name);
        }
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

    private async Task UpdateStateAsync(Func<Persona, Persona> update)
    {
        var current = state.State ?? new Persona();
        state.State = update(current);
        await state.WriteStateAsync();
    }
}

/// <summary>
/// Race-relevant state for one in-flight generation. Mutated in place as the work
/// progresses through Decision → Generation → done. <see cref="Snapshot"/> takes a
/// consistent read under lock for the race trigger; mutations from the streaming
/// loop also acquire the lock so concurrent reads see coherent (gut, preview, text,
/// tokens) tuples.
/// </summary>
internal sealed class InFlightGeneration(CancellationTokenSource cts)
{
    // Char-to-token approximation (~4 chars per token, English-leaning). Crude but stable
    // — the race math only needs this for "are we past PNR yet?" not exact accounting.
    // Replace with a real tokenizer only if traces show wrong PNR triggers.
    private const int CharsPerTokenEstimate = 4;

    public CancellationTokenSource Cts { get; } = cts;

    private readonly object _lock = new();
    private InFlightPhase _phase = InFlightPhase.Decision;
    private string _gutReaction = string.Empty;
    private string _wouldSayPreview = string.Empty;
    private readonly StringBuilder _generatedText = new();
    private int _generatedChars;

    // Set by the race when it elects to cancel this generation; consumed in the
    // OperationCanceledException catch to distinguish race-cancel (→ emote) from
    // external cancel via PartyGrain.CancelGenerationAsync (→ red error).
    private bool _raceCancelled;
    private string _interruptingMessage = string.Empty;
    private string _interruptingSenderName = string.Empty;

    public void MarkGenerationStarted(string gutReaction, string wouldSayPreview)
    {
        lock (_lock)
        {
            _phase = InFlightPhase.Generation;
            _gutReaction = gutReaction ?? string.Empty;
            _wouldSayPreview = wouldSayPreview ?? string.Empty;
        }
    }

    public void AppendChunk(string chunk)
    {
        lock (_lock)
        {
            _generatedText.Append(chunk);
            _generatedChars = _generatedText.Length;
        }
    }

    public void ResetGeneratedText()
    {
        lock (_lock)
        {
            _generatedText.Clear();
            _generatedChars = 0;
        }
    }

    /// <summary>Mark this generation as race-cancelled before triggering the CTS, so the
    /// catch can route to the emote path. Captures the interrupting message context for
    /// the emote-generation prompt.</summary>
    public void MarkRaceCancelled(string interruptingMessage, string interruptingSenderName)
    {
        lock (_lock)
        {
            _raceCancelled = true;
            _interruptingMessage = interruptingMessage ?? string.Empty;
            _interruptingSenderName = interruptingSenderName ?? string.Empty;
        }
    }

    public InFlightSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new InFlightSnapshot(
                _phase,
                _gutReaction,
                _wouldSayPreview,
                _generatedText.ToString(),
                _generatedChars / CharsPerTokenEstimate,
                _raceCancelled,
                _interruptingMessage,
                _interruptingSenderName);
        }
    }
}

internal enum InFlightPhase { Decision, Generation }

internal readonly record struct InFlightSnapshot(
    InFlightPhase Phase,
    string GutReaction,
    string WouldSayPreview,
    string GeneratedText,
    int GeneratedTokens,
    bool RaceCancelled,
    string InterruptingMessage,
    string InterruptingSenderName);

/// <summary>
/// Grain contract for managing a single persona.
/// </summary>
[Alias("backend.IPersonaGrain")]
public interface IPersonaGrain : IGrainWithGuidKey
{
    [Alias("SetPersonaFromModel")]
    Task SetPersona(Persona persona);

    [Alias("SetPersona")]
    Task SetPersona(string name, string systemPrompt, string? bio);

    [Alias("SetName")]
    Task SetName(string name);

    [Alias("SetSystemPrompt")]
    Task SetSystemPrompt(string systemPrompt);

    [Alias("SetBio")]
    Task SetBio(string? bio);

    [Alias("GetPersona")]
    Task<Persona> GetPersona();

    [Alias("DeletePersona")]
    Task DeletePersona();

    [Alias("NotifyMessageAsync")]
    Task NotifyMessageAsync(Guid chatGroupId, ChatMessage triggeringMessage, CancellationToken ct);

    [Alias("CancelGenerationAsync")]
    Task CancelGenerationAsync();
}
