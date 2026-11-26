using System.Diagnostics;
using System.Text.Json;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Logging;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.Services.Papertrail;
using PartyTown.Services.Streaming;
using PartyTown.Services.SystemOne;
using PartyTown.World;
using PartyTown.World.Self;

namespace PartyTown.Services.ResponsePipeline;

public sealed class ResponsePipeline(
    IGrainFactory grainFactory,
    IMemoryRepository memoryRepository,
    ILoggerFactory loggerFactory,
    ILogger<ResponsePipeline> logger,
    ISystemOneJudge judge,
    World.Self.FollowThrough? followThrough = null,
    World.Work.Responsibilities? responsibilities = null,
    World.Reflex.ReflexGate? reflex = null,
    SpeakingBackoff? backoff = null)
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
        if (triggeringMessage.SenderId == persona.Id)
            return;

        var partyId = await chatGroupGrain.GetPartyIdAsync();
        var roomCast = await chatGroupGrain.GetCastAsync();

        if (roomCast.Speaker(persona.Id) is not { } self)
        {
            logger.LogDebug(
                "Persona {PersonaName} is {Driver}-Effective in chat group {ChatGroupId}; skipping pipeline",
                persona.Name, roomCast.EffectiveOf(persona.Id)?.ToString() ?? "absent", chatGroupId);
            return;
        }

        using var turnSpan = Tracing.Persona.StartActivity("persona.turn", ActivityKind.Internal);
        turnSpan?.SetTag("persona.id", persona.Id);
        turnSpan?.SetTag("persona.name", persona.Name);
        turnSpan?.SetTag("chat_group.id", chatGroupId);
        turnSpan?.AddBaggage("posthog.ai.session_id", $"chat-{chatGroupId}");
        turnSpan?.SetTag("triggered_by.message_id", triggeringMessage.MessageId);
        turnSpan?.SetTag("triggered_by.sender_id", triggeringMessage.SenderId);

        logger.LogInformation(
            "Persona {PersonaName} notified of message {MessageId} in chat group {ChatGroupId}",
            persona.Name, triggeringMessage.MessageId, chatGroupId);

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

        var dmExhausted = isDirect && preRounds >= MaxDirectAiRounds;
        if (dmExhausted || UrgeMath.IsObviousSkip(preUrge, preRounds, preRecentSelf))
        {
            turnSpan?.SetTag("decision", dmExhausted ? "skip-dm-cap" : "skip-obvious");
            var skipReason = dmExhausted
                ? $"dm-cap (rounds={preRounds}, max={MaxDirectAiRounds})"
                : $"obvious-skip (rounds={preRounds}, recentSelf={preRecentSelf})";
            logger.LogDebug(
                "Persona {PersonaName} silently skipped (urge={Urge:F2}, rounds={Rounds}, recentSelf={Self})",
                persona.Name, preUrge.Total, preRounds, preRecentSelf);
            await RecordSkipAsync(chatGroupGrain, persona, triggeringMessage, preUrge.Total, skipReason);
            return;
        }

        var deliberate = false;
        var coldOpen = preUrge.ColdOpenScore > 0 && !isDirect;
        if (reflex is not null && !coldOpen)
        {
            var verdict = await reflex.PerceiveAsync(partyId, persona.Id, PerceptOf(triggeringMessage, roomCast, preHistory, preUrge, chatGroupId));
            turnSpan?.SetTag("reflex.proceed", verdict.Proceed);
            turnSpan?.SetTag("reflex.note", verdict.Note);
            if (!verdict.Proceed)
            {
                await RecordSkipAsync(chatGroupGrain, persona, triggeringMessage, preUrge.Total, $"reflex: {verdict.Note}");
                return;
            }
            if (verdict.LagMs > 0 && !await store.ReadAsync(chatGroupId, TimeSpan.FromMilliseconds(verdict.LagMs), ct))
            {
                await RecordSkipAsync(chatGroupGrain, persona, triggeringMessage, preUrge.Total, "reflex: read on to a newer line");
                return;
            }
            deliberate = verdict.Deliberate;
        }

        await TurnAsync(persona, chatGroupId, triggeringMessage, chatGroupGrain, store, partyId, roomCast, self, turnSpan,
            preUrge.Total, deliberate, answer: false, ct);
    }

    public async Task AnswerAsync(
        Persona persona,
        Guid chatGroupId,
        ChatMessage line,
        IChatGroupGrain chatGroupGrain,
        InFlightStore store,
        CancellationToken ct)
    {
        if (line.SenderId == persona.Id) return;
        var partyId = await chatGroupGrain.GetPartyIdAsync();
        var roomCast = await chatGroupGrain.GetCastAsync();
        if (roomCast.Speaker(persona.Id) is not { } self) return;

        using var turnSpan = Tracing.Persona.StartActivity("persona.answer", ActivityKind.Internal);
        turnSpan?.SetTag("persona.id", persona.Id);
        turnSpan?.SetTag("persona.name", persona.Name);
        turnSpan?.SetTag("chat_group.id", chatGroupId);
        turnSpan?.AddBaggage("posthog.ai.session_id", $"chat-{chatGroupId}");
        turnSpan?.SetTag("triggered_by.message_id", line.MessageId);
        turnSpan?.SetTag("triggered_by.sender_id", line.SenderId);
        logger.LogInformation("Persona {PersonaName} answers message {MessageId} nobody answered in chat group {ChatGroupId}",
            persona.Name, line.MessageId, chatGroupId);

        await TurnAsync(persona, chatGroupId, line, chatGroupGrain, store, partyId, roomCast, self, turnSpan,
            urge: null, deliberate: false, answer: true, ct);
    }

    private async Task TurnAsync(
        Persona persona,
        Guid chatGroupId,
        ChatMessage triggeringMessage,
        IChatGroupGrain chatGroupGrain,
        InFlightStore store,
        Guid partyId,
        RoomCast roomCast,
        SelfView self,
        Activity? turnSpan,
        double? urge,
        bool deliberate,
        bool answer,
        CancellationToken ct)
    {
        var participantViews = roomCast.Views();
        var isDirect = roomCast.IsDirect;
        var messageId = await chatGroupGrain.BeginTurnAsync(persona.Id, triggeringMessage.MessageId, urge);
        turnSpan?.SetTag("result.message_id", messageId);

        var newCts = new CancellationTokenSource();
        var inFlight = store.Register(chatGroupId, messageId, newCts);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, newCts.Token);
        var linkedCt = linkedCts.Token;

        try
        {
            var history = await chatGroupGrain.GetMessagesUntilAsync(messageId, listener: persona.Id);

            var decisionParticipants = participantViews;

            await NotifyStreamAsync(chatGroupGrain, chatGroupId, messageId,
                MessageStreamEvent.PersonaEvaluatingResponse, persona.Id.ToString(), false);

            RepairHint? repairHint = store.ConsumeRepairHint(chatGroupId);
            if (repairHint is not null)
                turnSpan?.SetTag("repair.missed_message_id", repairHint.Value.MissedMessageId);

            var presentPersonaIds = participantViews.Select(p => p.Id).ToList();
            var beat = await PersonaRecall.ForBeatAsync(memoryRepository, grainFactory,
                new BeatAnchors(persona.Id, partyId, presentPersonaIds, triggeringMessage.Content ?? string.Empty,
                    await PlaceOfAsync(roomCast, partyId, chatGroupId)),
                memoryLimit: 10, stanceLimit: 5, logger, linkedCt);
            var recollections = beat.Memories;
            turnSpan?.SetTag("recall.candidate_count", recollections.Count);

            if (recollections.Count > 0)
            {
                logger.LogInformation(
                    "Recall for persona {PersonaName} in party {PartyId}: {Candidates}",
                    persona.Name, partyId,
                    string.Join(", ", recollections.Select((r, i) =>
                        $"#{i + 1} {r.EdgeId:N}={r.Salience:F3} ({ArmTag(r.Arm)})")));
            }

            IReadOnlyList<string> stanceLines = beat.Stances.Select(StanceBlock.FormatLine).ToList();
            var ambivalenceCount = beat.Stances.Count(s => !string.IsNullOrEmpty(s.Contrast));
            turnSpan?.SetTag("stance.line_count", stanceLines.Count);
            turnSpan?.SetTag("stance.ambivalence_count", ambivalenceCount);

            var decision = await RunDecisionPhaseAsync(
                chatGroupGrain, chatGroupId, messageId, self, history, decisionParticipants, isDirect,
                recollections.Select(r => r.Snippet).ToList(), linkedCt,
                repairHint, stanceLines, deliberate, roomCast.Kind == ChatGroupKinds.Place);
            if (answer && !decision.Respond)
            {
                turnSpan?.SetTag("decision.declined_but_owed", true);
                decision = Answering(decision);
            }

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

                if (pickedMemory.EdgeId != Guid.Empty)
                {
                    _ = StrengthenPickedMemoryAsync(pickedMemory.EdgeId, persona.Name);
                }
            }

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

            inFlight.MarkGenerationStarted(decision.Reason, decision.Instruction);

            var result = await RunSpeakingPhaseAsync(
                chatGroupGrain, chatGroupId, messageId, self, participantViews, history,
                decision.Shortcut ? null : decision.Instruction, decision.Shortcut ? null : decision.Reason,
                pickedMemory?.Snippet, stanceLines, persona.Name, inFlight, linkedCt,
                roomCast.Kind == ChatGroupKinds.Place, triggeringMessage, answer);

            var appraisalJson = JsonSerializer.Serialize(new
            {
                personaId = persona.Id,
                instruction = decision.Instruction,
                reason = decision.Reason,
                stop = false,
                recall = recallLog
            }, WebOptions);

            if (string.IsNullOrWhiteSpace(result.Message))
            {
                turnSpan?.SetTag("decision", "unsayable");
                logger.LogWarning("Persona {PersonaName} produced no sayable line for message {MessageId}; declining", persona.Name, messageId);
                await chatGroupGrain.EndTurnAsync(messageId, new TurnOutcome.Declined(appraisalJson));
                return;
            }

            await chatGroupGrain.EndTurnAsync(messageId, new TurnOutcome.Spoke(
                result.Message ?? string.Empty, result.Reasoning, appraisalJson, result.Metadata));
            followThrough?.Consider(partyId, chatGroupId, persona.Id);
            if (reflex is not null) await reflex.NoteSpokeAsync(partyId, persona.Id, chatGroupId);

            if (result.Metadata is not null)
            {
                turnSpan?.SetTag("llm.provider", result.Metadata.Provider);
                turnSpan?.SetTag("llm.model", result.Metadata.ModelName);
            }
            logger.LogInformation("Persona {PersonaName} completed response for message {MessageId}", persona.Name, messageId);
        }
        catch (OperationCanceledException)
        {
            var snap = inFlight.Snapshot();
            if (snap.RaceCancelled)
            {
                turnSpan?.SetTag("decision", "race-cancelled");
                logger.LogInformation(
                    "Persona {PersonaName} race-cancelled (msg {MessageId}, drafted {Chars} chars)",
                    persona.Name, messageId, snap.GeneratedText.Length);

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
            await chatGroupGrain.EndTurnAsync(messageId, new TurnOutcome.Failed(ex.ToString()));
        }
        finally
        {
            store.Remove(chatGroupId, messageId);
        }
    }

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
        turnSpan?.AddBaggage("posthog.ai.session_id", $"chat-{chatGroupId}");

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
            var presentIds = participantViews.Select(p => p.Id).ToList();

            var beat = await PersonaRecall.ForBeatAsync(memoryRepository, grainFactory,
                new BeatAnchors(persona.Id, partyId, presentIds, intent, await PlaceOfAsync(roomCast, partyId, chatGroupId)),
                memoryLimit: 0, stanceLimit: 5, logger, linkedCt);
            string? memory = null;
            IReadOnlyList<string> stanceLines = beat.Stances.Select(StanceBlock.FormatLine).ToList();

            inFlight.MarkGenerationStarted(feeling, intent);
            var result = await RunSpeakingPhaseAsync(
                chatGroupGrain, chatGroupId, messageId, self, participantViews, history,
                intent, feeling, memory, stanceLines, persona.Name, inFlight, linkedCt,
                roomCast.Kind == ChatGroupKinds.Place);

            if (string.IsNullOrWhiteSpace(result.Message))
            {
                logger.LogWarning("Persona {PersonaName} initiative in {ChatGroupId} produced no sayable line", persona.Name, chatGroupId);
                await chatGroupGrain.EndTurnAsync(messageId, new TurnOutcome.Declined(appraisalJson));
                return null;
            }

            await chatGroupGrain.EndTurnAsync(messageId, new TurnOutcome.Spoke(
                result.Message ?? string.Empty, result.Reasoning, appraisalJson, result.Metadata));
            followThrough?.Consider(partyId, chatGroupId, persona.Id);
            if (reflex is not null) await reflex.NoteSpokeAsync(partyId, persona.Id, chatGroupId);
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

    internal static ShouldRespondResult Answering(ShouldRespondResult decision)
        => decision.Respond ? decision : decision with { Respond = true, Shortcut = decision.Reason.StartsWith("Fallback", StringComparison.Ordinal) };

    private async Task RecordSkipAsync(IChatGroupGrain chatGroupGrain, Persona persona, ChatMessage trigger, double urge, string reason)
    {
        try
        {
            await chatGroupGrain.RecordSkippedTurnAsync(persona.Id, persona.Name ?? string.Empty, trigger.MessageId, urge, reason);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to record skip for persona {PersonaName}", persona.Name);
        }
    }

    private static World.Reflex.Percept PerceptOf(ChatMessage m, RoomCast cast, IReadOnlyList<ChatMessage> history, ResponseUrge urge, Guid roomId)
    {
        var views = cast.Views();
        string Name(Guid id) => views.Where(v => v.Id == id).Select(v => v.Name).FirstOrDefault() ?? "Someone";
        var kind = cast.IsDirect ? World.Reflex.PerceptKind.Direct
            : cast.Kind == ChatGroupKinds.Place ? World.Reflex.PerceptKind.Speech
            : World.Reflex.PerceptKind.Line;
        var context = history.Where(h => h.MessageId != m.MessageId && !string.IsNullOrWhiteSpace(h.Content)).TakeLast(5)
            .Select(h => $"{Name(h.SenderId)}: {WorldStream.Clip(h.Content, 160)}").ToList();
        return new World.Reflex.Percept(kind, m.SenderId, Name(m.SenderId), cast.EffectiveOf(m.SenderId) == DriverKind.User,
            m.Content ?? string.Empty, roomId, Named: !cast.IsDirect && urge.MentionScore > 0, Context: context);
    }

    private static string ArmTag(RecallArm arm)
        => arm == RecallArm.Relevant ? "relevant" : "recent";

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
        string? memoryToReference,
        IReadOnlyList<string> stances,
        string personaName,
        InFlightGeneration inFlight,
        CancellationToken ct,
        bool inPerson = false,
        ChatMessage? answering = null,
        bool owed = false)
    {
        var router = grainFactory.GetGrain<ILlmRouterGrain>(0);
        var session = new SpeakingSession(router, fullParticipants);
        var livedSheet = LivedSheets.Lived(self.Id, self.SystemPrompt);
        var weighing = Gravity.WeighAsync(judge.For(self.Id), self.Name,
            Gravity.State(self.Name, Gravity.Bio(self, livedSheet), self.Id, fullParticipants, history, answering, turnInstruction, owed, inPerson),
            logger, ct);
        var presence = await PresenceAsync(chatGroupGrain, self, fullParticipants, history);
        if (livedSheet is { } lived)
            self = self with { Bio = null, SystemPrompt = lived };
        var avoidWords = await StaleWordsAsync(chatGroupGrain, self.Id);
        var held = ChatAttention.Window(history, await ReadFromAsync(chatGroupGrain, chatGroupId, self.Id)).Held;
        var gravity = await weighing;

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
                if (attempt > 0)
                    inFlight.ResetGeneratedText();

                result = await session.GenerateResponseOnlyAsync(
                    self,
                    held,
                    onEvent: TrackingOnEvent,
                    ct,
                    turnInstruction,
                    memoryToReference,
                    stances,
                    gutReaction,
                    presence,
                    avoidWords,
                    inPerson,
                    gravity,
                    opening: answering is null);
                logger.LogInformation("Persona {PersonaName} speaks with gravity {Gravity} and drew a {Length} line for message {MessageId}",
                    personaName, gravity is { } g ? g.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : "unweighed", result.Length, messageId);
                break;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                logger.LogWarning(ex, "Persona {PersonaName} generation attempt {Attempt} failed, retrying", personaName, attempt + 1);
                var wait = (backoff ?? SpeakingBackoff.Default).After(attempt + 1);
                await NotifyStreamAsync(chatGroupGrain, chatGroupId, messageId,
                    MessageStreamEvent.GenerationRetry,
                    JsonSerializer.Serialize(new { attempt = attempt + 1, nextAttemptInSeconds = (int)wait.TotalSeconds }, WebOptions),
                    false);
                await Task.Delay(wait, ct);
            }
        }

        _ = NoteSpokeAsync(chatGroupGrain, self, fullParticipants, history);
        return result!;
    }

    private const int SkillWindow = 4;

    private async Task<string?> PresenceAsync(IChatGroupGrain chatGroupGrain, SelfView self, IReadOnlyList<ParticipantView> present, IReadOnlyList<ChatMessage> history)
    {
        var knowHow = await KnowHowAsync(chatGroupGrain, self.Id, history);
        try
        {
            var partyId = await chatGroupGrain.GetPartyIdAsync();
            var key = WorldKeys.Life(partyId, self.Id);
            var presenceTask = grainFactory.GetGrain<IPersonaSelfGrain>(key)
                .GetPresenceAsync(withDay: true);
            var tiesTask = grainFactory.GetGrain<IPersonaTiesGrain>(key).RenderAsync(present.Select(p => p.Id).ToList());
            var cueTask = grainFactory.GetGrain<IConversationThermostatGrain>(chatGroupGrain.GetPrimaryKey()).GetCueAsync();
            var (presence, ties, cue) = (await presenceTask, await tiesTask, await cueTask);
            presence += World.Economy.Money.Render(await grainFactory.GetGrain<World.Economy.IWalletGrain>(key).GetAsync(), DateTimeOffset.UtcNow);
            presence += await grainFactory.GetGrain<World.Places.IPlacesGrain>(partyId).GetKeyLinesAsync(self.Id);
            if (responsibilities is not null)
                presence += await responsibilities.RoleAsync(partyId, self.Id) + await responsibilities.RenderAsync(partyId, self.Id, DateTimeOffset.UtcNow);
            var block = string.IsNullOrWhiteSpace(ties) ? presence : $"{presence}\n{ties}";
            if (reflex is not null) block += await reflex.MindAsync(partyId, self.Id);
            block += knowHow;
            return string.IsNullOrWhiteSpace(cue) ? block : $"{block}\n# The conversation\n{cue}\n";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Presence failed for {PersonaName}; speaking without it", self.Name);
            return string.IsNullOrEmpty(knowHow) ? null : knowHow;
        }
    }

    private async Task<string> KnowHowAsync(IChatGroupGrain chatGroupGrain, Guid personaId, IReadOnlyList<ChatMessage> history)
    {
        try
        {
            var live = string.Join("\n", history.TakeLast(SkillWindow).Select(m => m.Content).Where(c => !string.IsNullOrWhiteSpace(c)));
            if (live.Length == 0) return string.Empty;
            var key = WorldKeys.Life(await chatGroupGrain.GetPartyIdAsync(), personaId);
            var skills = await grainFactory.GetGrain<World.Skills.ISkillsGrain>(key).GetAsync();
            return World.Skills.SkillRules.Render(World.Skills.SkillRules.Match(skills, live));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Skills lookup failed for {PersonaId}", personaId);
            return string.Empty;
        }
    }

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

    private async Task<ShouldRespondResult> RunDecisionPhaseAsync(
        IChatGroupGrain chatGroupGrain,
        Guid chatGroupId,
        int messageId,
        SelfView self,
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ParticipantView> participants,
        bool isDirect,
        IReadOnlyList<string> recollections,
        CancellationToken ct,
        RepairHint? repairHint = null,
        IReadOnlyList<string>? stances = null,
        bool deliberate = false,
        bool inPerson = false)
    {
        var router = grainFactory.GetGrain<ILlmRouterGrain>(0);
        var decisionService = new PersonaDecisionService(router, loggerFactory.CreateLogger<PersonaDecisionService>());
        var totalAiRounds = UrgeMath.CountExchangeRounds(history);
        if (await SleepsThroughAsync(chatGroupGrain, self, history, participants))
            return new ShouldRespondResult { Respond = false, Instruction = string.Empty, Reason = "Asleep." };
        var presence = await PresenceAsync(chatGroupGrain, self, participants, history);
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
            repairHint: repairHint,
            recollections: recollections,
            stances: stances,
            isDirect: isDirect,
            presence: presence,
            windingDown: windingDown,
            deliberate: deliberate,
            inPerson: inPerson);
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

public sealed record SpeakingBackoff(Func<int, TimeSpan> After)
{
    public static readonly SpeakingBackoff Default = new(failed => TimeSpan.FromSeconds(2 * failed));
    public static readonly SpeakingBackoff None = new(_ => TimeSpan.Zero);
}
