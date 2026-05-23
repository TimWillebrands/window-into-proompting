using System.Diagnostics;
using System.Text.Json;
using Orleans.Concurrency;
using PartyTown.Grains.Generation;
using PartyTown.Logging;
using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;
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
    RaceTrigger raceTrigger,
    ILogger<PersonaGrain> logger)
    : Grain, IPersonaGrain
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private readonly InFlightStore _store = new();

    public Task CancelGenerationAsync() => _store.CancelAllAsync();

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
        await raceTrigger.EvaluateAsync(persona, chatGroupId, triggeringMessage, chatGroupGrain, _store, ct);

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

        var preUrge = UrgeMath.CalculateResponseUrge(self, preHistory, preRounds);
        var preRecentSelf = UrgeMath.CountRecentSelfMessages(preHistory, personaId);
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

        // Race-trigger state: register before any awaits so a concurrent NotifyMessageAsync
        // can race against this one. Mutated in-place as decision → generation → done.
        var newCts = new CancellationTokenSource();
        var inFlight = _store.Register(chatGroupId, messageId, newCts);

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
            RepairHint? repairHint = _store.ConsumeRepairHint(chatGroupId);
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
            _store.Remove(chatGroupId, messageId);
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

    private async Task<SpeakingResult> RunSpeakingPhaseAsync(
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
