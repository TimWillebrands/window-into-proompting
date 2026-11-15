using System.Text.Json;
using Orleans.EventSourcing;
using Orleans.Providers;
using Orleans.Streams;
using PartyTown.Logging;
using PartyTown.Model;
using PartyTown.Services.Streaming;

namespace PartyTown.Grains;

[LogConsistencyProvider(ProviderName = "PartyStateStorage")]
[StorageProvider(ProviderName = "parties")]
public sealed class ChatGroupGrain(ILogger<ChatGroupGrain> logger)
    : JournaledGrain<ChatGroupState, ChatGroupEvent>, IChatGroupGrain
{
    private string? _kind;

    private static readonly TimeSpan OwedFor = TimeSpan.FromMinutes(10);

    private const string Promised = "reflex: " + World.Reflex.ReflexVerdict.Holding;

    private readonly OwedAnswers _owed = new(OwedFor);

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

        if (State.Initialized)
        {
            logger.LogInformation("ChatGroupGrain activated (from replay): {ChatGroupId}", this.GetPrimaryKey());
            return;
        }

        var registry = GrainFactory.GetGrain<IPartyRootGrain>(Guid.Empty);
        var partyId = await registry.GetPartyForChatGroup(this.GetPrimaryKey());
        if (partyId is null)
            throw new InvalidOperationException(
                $"ChatGroupGrain {this.GetPrimaryKey()} not registered in any party.");

        var partyGrain = GrainFactory.GetGrain<IPartyGrain>(partyId.Value);
        var party = await partyGrain.GetParty();
        var chatGroups = await partyGrain.GetChatGroups();
        var thisChatGroup = chatGroups.FirstOrDefault(g => g.Id == this.GetPrimaryKey());

        var seedIds = new HashSet<Guid>(party.Participants.Select(p => p.Id));

        RaiseEvent(new ChatGroupInitializedEvent
        {
            PartyId = partyId.Value,
            ParticipantIds = seedIds,
            Scenario = thisChatGroup?.Scenario
        });
        await ConfirmEvents();

        logger.LogInformation("ChatGroupGrain self-initialized: {ChatGroupId} in party {PartyId}",
            this.GetPrimaryKey(), partyId.Value);
    }

    public Task<Guid> GetPartyIdAsync() => Task.FromResult(State.PartyId);

    public Task<string?> GetScenarioAsync() => Task.FromResult(State.Scenario);

    public async Task SetScenarioAsync(string? scenario)
    {
        var normalized = string.IsNullOrWhiteSpace(scenario) ? null : scenario.Trim();
        if (normalized is { Length: > ChatGroupLimits.MaxScenarioLength })
        {
            logger.LogWarning(
                "Scenario length {Length} exceeded cap {Cap} for chat group {ChatGroupId}; truncating",
                normalized.Length, ChatGroupLimits.MaxScenarioLength, this.GetPrimaryKey());
            normalized = normalized[..ChatGroupLimits.MaxScenarioLength];
        }
        RaiseEvent(new ChatGroupScenarioSetEvent { Scenario = normalized });
        await ConfirmEvents();
    }

    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync()
        => Task.FromResult<IReadOnlyList<ChatMessage>>(State.Messages.OrderBy(m => m.MessageId).ToList());

    public Task<bool> IsAnsweringAsync(Guid senderId)
    {
        var since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (long)OpenTurnHolds.TotalMilliseconds;
        return Task.FromResult(State.Messages.Any(m => m.SenderId == senderId && IsOpen(m) && (m.SendAt ?? 0) >= since));
    }

    private static readonly TimeSpan OpenTurnHolds = TimeSpan.FromMinutes(3);

    public Task<List<ChatMessage>> GetRecentMessagesAsync(int count)
        => Task.FromResult(State.Messages
            .Where(m => !string.IsNullOrWhiteSpace(m.Content) && m.Error is null)
            .OrderBy(m => m.MessageId)
            .TakeLast(count)
            .ToList());

    public Task<int> BeginTurnAsync(Guid personaId, int? triggeredByMessageId = null, double? urge = null)
    {
        if (triggeredByMessageId is { } line) _owed.Urge(line, personaId, urge);
        return ReserveSlotAsync(personaId, "assistant", triggeredByMessageId);
    }

    public async Task EndTurnAsync(int turnId, TurnOutcome outcome)
    {
        var slot = State.Messages.FirstOrDefault(m => m.MessageId == turnId);
        if (slot is null || !IsOpen(slot))
        {
            logger.LogDebug("Turn {TurnId} in chat group {ChatGroupId} not open; dropping {Outcome}",
                turnId, this.GetPrimaryKey(), outcome.GetType().Name);
            return;
        }

        var trigger = slot.TriggeredByMessageId;
        switch (outcome)
        {
            case TurnOutcome.Spoke s:
                await AppendMessageAsync(turnId, s.Content, s.Reasoning, s.Appraisal, s.Metadata, trigger);
                break;
            case TurnOutcome.Declined d:
                await MarkGenerationStoppedAsync(turnId, d.Appraisal, trigger);
                break;
            case TurnOutcome.Interrupted i:
                await MarkGenerationCancelledAsEmoteAsync(turnId, i.Emote, i.Appraisal, trigger);
                break;
            case TurnOutcome.Failed f:
                await MarkGenerationFailedAsync(turnId, f.Error);
                break;
        }

        if (trigger is { } line) _owed.Ended(line, slot.SenderId, failed: outcome is TurnOutcome.Failed);
        await AnswerOwedAsync();
    }

    public Task ContinueAsync()
    {
        var last = State.Messages.OrderBy(m => m.MessageId).LastOrDefault();
        return last is null ? Task.CompletedTask : NotifyAllParticipantsAsync(last);
    }

    private static bool IsOpen(ChatMessage slot)
        => string.IsNullOrEmpty(slot.Content) && slot.Error is null && slot.Appraisal is null;

    private async Task<int> ReserveSlotAsync(
        Guid? senderId,
        string senderType,
        int? triggeredByMessageId = null)
    {

        RaiseEvent(new ChatGroupMessageSlotReservedEvent
        {
            SenderId = senderId,
            SenderType = senderType,
            ChatGroupId = this.GetPrimaryKey(),
            TriggeredByMessageId = triggeredByMessageId
        });
        await ConfirmEvents();
        return State.NextMessageId;
    }

    public async Task SetParticipantIdsAsync(IReadOnlySet<Guid> participantIds)
    {
        var deduped = new HashSet<Guid>(participantIds.Where(id => id != Guid.Empty));
        if (State.ParticipantIds.SetEquals(deduped)) return;
        RaiseEvent(new ChatGroupParticipantIdsSetEvent { ParticipantIds = deduped });
        await ConfirmEvents();
    }

    public async Task<IReadOnlySet<Guid>> JoinAsync(IReadOnlyList<Guid> personaIds)
    {
        var party = await GrainFactory.GetGrain<IPartyGrain>(State.PartyId).GetParty();
        var cast = party.Participants.Select(p => p.Id).ToHashSet();
        var next = new HashSet<Guid>(State.ParticipantIds);
        next.UnionWith(personaIds.Where(cast.Contains));
        await SetParticipantIdsAsync(next);
        return State.ParticipantIds;
    }

    public Task<IReadOnlySet<Guid>> GetParticipantIdsAsync()
        => Task.FromResult<IReadOnlySet<Guid>>(State.ParticipantIds);

    public Task<IReadOnlyDictionary<Guid, DriverKind>> GetDriverOverridesAsync()
        => Task.FromResult<IReadOnlyDictionary<Guid, DriverKind>>(State.DriverOverrides);

    public async Task<RoomCast> GetCastAsync()
    {
        var cast = await GrainFactory.GetGrain<IPartyGrain>(State.PartyId).GetCastAsync();
        return RoomCast.Create(cast, State.ParticipantIds, State.DriverOverrides, await KindAsync());
    }

    private async Task<string> KindAsync()
        => _kind ??= (await GrainFactory.GetGrain<IPartyGrain>(State.PartyId).GetChatGroups())
            .FirstOrDefault(g => g.Id == this.GetPrimaryKey())?.Kind ?? ChatGroupKinds.Room;

    public async Task SetDriverOverrideAsync(Guid personaId, DriverKind kind)
    {
        if (State.DriverOverrides.TryGetValue(personaId, out var existing) && existing == kind) return;
        RaiseEvent(new ChatGroupDriverOverrideSetEvent { PersonaId = personaId, Kind = kind });
        await ConfirmEvents();
    }

    public async Task ClearDriverOverrideAsync(Guid personaId)
    {
        if (!State.DriverOverrides.ContainsKey(personaId)) return;
        RaiseEvent(new ChatGroupDriverOverrideClearedEvent { PersonaId = personaId });
        await ConfirmEvents();
    }

    public async Task SetDriverOverridesAsync(IReadOnlyDictionary<Guid, DriverKind> overrides)
    {
        var deduped = overrides
            .Where(kv => kv.Key != Guid.Empty)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        if (State.DriverOverrides.Count == deduped.Count
            && deduped.All(kv => State.DriverOverrides.TryGetValue(kv.Key, out var v) && v == kv.Value))
            return;
        RaiseEvent(new ChatGroupDriverOverridesReplacedEvent { Overrides = deduped });
        await ConfirmEvents();
    }

    public async Task<ChatMessage> SendNewMessageAsync(
        Guid senderId,
        string content,
        string? reasoning,
        string? appraisal,
        CancellationToken ct = default)
    {

        var chatGroupId = this.GetPrimaryKey();
        var messageId = await ReserveSlotAsync(senderId, "user");
        var earshot = await EarshotOfAsync(senderId);

        var message = new ChatMessage
        {
            HeardBy = earshot?.HeardBy,
            Volume = earshot?.Volume,
            ChatGroupId = chatGroupId,
            MessageId = messageId,
            Content = content,
            SenderType = "user",
            SenderId = senderId,
            Reasoning = reasoning,
            Appraisal = appraisal,
            SendAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        RaiseEvent(new ChatGroupUserMessageEvent
        {
            SenderId = senderId,
            Message = message
        });
        await ConfirmEvents();

        await PublishPartyEvent(new PartyStreamEvent
        {
            Type = "message",
            ChatGroupId = chatGroupId,
            Message = message
        });

        _owed.Landed(messageId, senderId, message.HeardBy);
        await OweAsync(message);
        _ = NotifyAllParticipantsAsync(message, ct);

        return message;
    }

    private async Task OweAsync(ChatMessage line)
    {
        try
        {
            var party = await GrainFactory.GetGrain<IPartyGrain>(State.PartyId).GetParty();
            if (party.Participants.All(p => p.Id != line.SenderId)) return;
            var claimed = await GrainFactory.GetGrain<World.IWorldClockGrain>(State.PartyId).GetClaimedAsync();
            if (!Services.Auth.AccessRules.AtTheKeyboard(line.SenderId, party.Participants, claimed)) return;
            var cast = await GetCastAsync();
            _owed.Expect(line.MessageId, line.SenderId, ListenersOf(line).Where(id => cast.Speaker(id) is not null),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't tell whether line {MessageId} in {ChatGroupId} is owed an answer", line.MessageId, this.GetPrimaryKey());
        }
    }

    private async Task AnswerOwedAsync()
    {
        if (!_owed.AnyDue) return;
        try
        {
            var cast = await GetCastAsync();
            var present = cast.Members.Select(m => cast.Speaker(m.Id)).OfType<SelfView>().ToList();
            foreach (var (lineId, who) in _owed.Due(TurnOpenAfter, present))
            {
                if (State.Messages.FirstOrDefault(m => m.MessageId == lineId) is not { } line) continue;
                logger.LogInformation("Nobody answered line {MessageId} in {ChatGroupId}; {PersonaId} answers it", lineId, this.GetPrimaryKey(), who);
                _ = GrainFactory.GetGrain<IPersonaGrain>(who).AnswerAsync(this.GetPrimaryKey(), line with { });
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Answering owed lines in {ChatGroupId} failed", this.GetPrimaryKey());
        }
    }

    private bool TurnOpenAfter(int lineId)
    {
        var since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (long)OpenTurnHolds.TotalMilliseconds;
        return State.Messages.Any(m => m.MessageId > lineId && IsOpen(m) && (m.SendAt ?? 0) >= since);
    }

    private async Task AppendMessageAsync(
        int messageId,
        string content,
        string? reasoning,
        string? appraisal,
        ChatMessageMetadata? metadata,
        int? triggeredByMessageId)
    {

        var chatGroupId = this.GetPrimaryKey();
        var sendAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var senderId = State.Messages.FirstOrDefault(m => m.MessageId == messageId)?.SenderId ?? Guid.Empty;
        var earshot = await EarshotOfAsync(senderId);

        RaiseEvent(new ChatGroupGenerationCompletedEvent
        {
            MessageId = messageId,
            Content = content,
            Reasoning = reasoning,
            Appraisal = appraisal,
            SenderId = senderId,
            HeardBy = earshot?.HeardBy,
            Volume = earshot?.Volume,
            SendAt = sendAt,
            Metadata = metadata,
            TriggeredByMessageId = triggeredByMessageId
        });
        await ConfirmEvents();
        _owed.Landed(messageId, senderId, earshot?.HeardBy);

        var updatedMessage = State.Messages.FirstOrDefault(m => m.MessageId == messageId);
        if (updatedMessage is not null)
        {
            _ = NotifyAllParticipantsAsync(updatedMessage);
            await PublishPartyEvent(new PartyStreamEvent
            {
                Type = "message",
                ChatGroupId = chatGroupId,
                Message = updatedMessage
            });
        }
    }

    private async Task MarkGenerationStoppedAsync(int messageId, string? appraisal, int? triggeredByMessageId)
    {

        var chatGroupId = this.GetPrimaryKey();

        RaiseEvent(new ChatGroupGenerationStoppedEvent
        {
            MessageId = messageId,
            Appraisal = appraisal,
            SendAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            TriggeredByMessageId = triggeredByMessageId
        });
        await ConfirmEvents();

        var stoppedMessage = State.Messages.FirstOrDefault(m => m.MessageId == messageId);
        if (stoppedMessage is not null)
        {
            await PublishPartyEvent(new PartyStreamEvent
            {
                Type = "message",
                ChatGroupId = chatGroupId,
                Message = stoppedMessage
            });
        }
    }

    private async Task MarkGenerationFailedAsync(int messageId, string error)
    {

        var chatGroupId = this.GetPrimaryKey();

        RaiseEvent(new ChatGroupGenerationFailedEvent
        {
            MessageId = messageId,
            Error = error,
            SendAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
        await ConfirmEvents();

        await NotifyStreamChunkAsync(messageId, new MessageStreamEvent
        {
            ChatGroupId = chatGroupId,
            Event = MessageStreamEvent.GenerationError,
            Data = error,
            Done = true
        });
    }

    private async Task MarkGenerationCancelledAsEmoteAsync(
        int messageId,
        string emoteContent,
        string? appraisal,
        int? triggeredByMessageId)
    {
        var chatGroupId = this.GetPrimaryKey();

        RaiseEvent(new ChatGroupGenerationCancelledAsEmoteEvent
        {
            MessageId = messageId,
            Content = emoteContent,
            Appraisal = appraisal,
            TriggeredByMessageId = triggeredByMessageId,
            SendAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
        await ConfirmEvents();

        var updated = State.Messages.FirstOrDefault(m => m.MessageId == messageId);
        if (updated is not null)
        {
            _owed.Landed(messageId, updated.SenderId, updated.HeardBy);
            await PublishPartyEvent(new PartyStreamEvent
            {
                Type = "message",
                ChatGroupId = chatGroupId,
                Message = updated
            });
        }
    }

    public async Task DeleteMessageAsync(int messageId)
    {

        var chatGroupId = this.GetPrimaryKey();
        RaiseEvent(new ChatGroupMessageDeletedEvent { MessageId = messageId });
        await ConfirmEvents();
        await PublishPartyEvent(new PartyStreamEvent { Type = "deleteMessage", ChatGroupId = chatGroupId, MessageId = messageId });
    }

    public async Task DeleteMessagesAfterAsync(int messageId)
    {

        var chatGroupId = this.GetPrimaryKey();
        RaiseEvent(new ChatGroupMessagesAfterDeletedEvent { MessageId = messageId });
        await ConfirmEvents();
        _owed.Forget(messageId);
        await PublishPartyEvent(new PartyStreamEvent { Type = "deleteMessagesAfter", ChatGroupId = chatGroupId, MessageId = messageId });
    }

    public Task NotifyStreamChunkAsync(int messageId, MessageStreamEvent evt)
    {

        var chatGroupId = this.GetPrimaryKey();
        var streamProvider = this.GetStreamProvider(PartyStreamIds.Provider);
        var stream = streamProvider.GetStream<PartyStreamEvent>(
            PartyStreamIds.PartyEventsNamespace,
            PartyStreamIds.PartyEventId(State.PartyId));

        return stream.OnNextAsync(new PartyStreamEvent
        {
            Type = "messageEvent",
            ChatGroupId = chatGroupId,
            MessageId = messageId,
            MessageEvent = evt
        });
    }

    public Task<List<ChatMessage>> GetMessagesUntilAsync(int messageId, Guid? listener = null)
    {
        var before = State.Messages
            .Where(m => m.MessageId < messageId)
            .Where(m => listener is not { } who || m.HeardBy is not { } heard || heard.Contains(who))
            .OrderBy(m => m.MessageId)
            .ToArray();

        var messages = before.Select(message => message with { }).ToList();

        return Task.FromResult(messages);
    }

    public async Task RecordSkippedTurnAsync(
        Guid personaId,
        string personaName,
        int triggeredByMessageId,
        double urgeTotal,
        string reason)
    {
        RaiseEvent(new ChatGroupPersonaSkippedEvent
        {
            PersonaId = personaId,
            PersonaName = personaName,
            TriggeredByMessageId = triggeredByMessageId,
            UrgeTotal = urgeTotal,
            Reason = reason,
            SendAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
        await ConfirmEvents();

        if (State.SkippedTurns.LastOrDefault() is { } skipped)
            await PublishPartyEvent(new PartyStreamEvent { Type = "skippedTurn", ChatGroupId = this.GetPrimaryKey(), SkippedTurn = skipped });

        _owed.Urge(triggeredByMessageId, personaId, urgeTotal);
        if (reason.StartsWith(Promised, StringComparison.Ordinal)) _owed.Answered(triggeredByMessageId);
        else _owed.Ended(triggeredByMessageId, personaId);
        await AnswerOwedAsync();
    }

    public Task<IReadOnlyList<SkippedTurn>> GetSkippedTurnsAsync()
        => Task.FromResult<IReadOnlyList<SkippedTurn>>(State.SkippedTurns.ToList());

    public async Task RecordRaceEvaluationAsync(
        Guid personaId,
        string personaName,
        int triggeredByMessageId,
        string outcome,
        double? salience,
        double? cancelScore)
    {
        var sendAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        RaiseEvent(new ChatGroupPersonaRaceEvaluationEvent
        {
            PersonaId = personaId,
            PersonaName = personaName,
            TriggeredByMessageId = triggeredByMessageId,
            Outcome = outcome,
            Salience = salience,
            CancelScore = cancelScore,
            SendAt = sendAt
        });
        await ConfirmEvents();

        var raced = State.RaceEvaluations.LastOrDefault();
        if (raced is not null)
        {
            await PublishPartyEvent(new PartyStreamEvent
            {
                Type = "raceEvaluation",
                ChatGroupId = this.GetPrimaryKey(),
                RaceEvaluation = raced
            });
        }
    }

    public Task<IReadOnlyList<RaceEvaluation>> GetRaceEvaluationsAsync()
        => Task.FromResult<IReadOnlyList<RaceEvaluation>>(State.RaceEvaluations.ToList());

    public Task<int> CountTrailingAssistantMessagesAsync()
        => Task.FromResult(Services.ResponsePipeline.UrgeMath.CountExchangeRounds(State.Messages));

    private Task NotifyAllParticipantsAsync(ChatMessage triggeringMessage, CancellationToken ct = default)
    {
        var chatGroupId = this.GetPrimaryKey();
        var targets = ListenersOf(triggeringMessage);

        logger.LogInformation("Fanning out to {Count} participants in chat group {ChatGroupId}",
            targets.Count, chatGroupId);

        foreach (var id in targets)
        {
            _ = GrainFactory.GetGrain<IPersonaGrain>(id)
                .NotifyMessageAsync(chatGroupId, triggeringMessage, ct);
        }

        return Task.CompletedTask;
    }

    private List<Guid> ListenersOf(ChatMessage line)
        => State.ParticipantIds
            .Where(id => id != line.SenderId)
            .Where(id => line.HeardBy is not { } heard || heard.Contains(id))
            .ToList();

    private async Task<World.Places.EarshotResult?> EarshotOfAsync(Guid senderId)
    {
        if (await KindAsync() != ChatGroupKinds.Place) return null;
        return await GrainFactory.GetGrain<World.Places.IPlacesGrain>(State.PartyId).EarshotAsync(this.GetPrimaryKey(), senderId);
    }

    private Task PublishPartyEvent(PartyStreamEvent evt)
    {
        logger.LogDebug("Publishing stream event: {EventType}", evt.Type);
        var streamProvider = this.GetStreamProvider(PartyStreamIds.Provider);
        var stream = streamProvider.GetStream<PartyStreamEvent>(
            PartyStreamIds.PartyEventsNamespace,
            PartyStreamIds.PartyEventId(State.PartyId));
        return stream.OnNextAsync(evt);
    }
}

[Alias("backend.IChatGroupGrain")]
public interface IChatGroupGrain : IGrainWithGuidKey
{
    [Alias("GetRecentMessagesAsync")]
    Task<List<ChatMessage>> GetRecentMessagesAsync(int count);

    [Alias("GetPartyIdAsync")]
    Task<Guid> GetPartyIdAsync();

    [Alias("GetScenarioAsync")]
    Task<string?> GetScenarioAsync();

    [Alias("SetScenarioAsync")]
    Task SetScenarioAsync(string? scenario);

    [Alias("GetMessagesAsync")]
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync();

    [Alias("BeginTurnAsync")]
    Task<int> BeginTurnAsync(Guid personaId, int? triggeredByMessageId = null, double? urge = null);

    [Alias("EndTurnAsync")]
    Task EndTurnAsync(int turnId, TurnOutcome outcome);

    [Alias("ContinueAsync")]
    Task ContinueAsync();

    [Alias("SetParticipantIdsAsync")]
    Task SetParticipantIdsAsync(IReadOnlySet<Guid> participantIds);

    [Alias("GetParticipantIdsAsync")]
    Task<IReadOnlySet<Guid>> GetParticipantIdsAsync();

    [Alias("JoinAsync")]
    Task<IReadOnlySet<Guid>> JoinAsync(IReadOnlyList<Guid> personaIds);

    [Alias("GetDriverOverridesAsync")]
    Task<IReadOnlyDictionary<Guid, DriverKind>> GetDriverOverridesAsync();

    [Alias("GetCastAsync")]
    Task<RoomCast> GetCastAsync();

    [Alias("SetDriverOverrideAsync")]
    Task SetDriverOverrideAsync(Guid personaId, DriverKind kind);

    [Alias("ClearDriverOverrideAsync")]
    Task ClearDriverOverrideAsync(Guid personaId);

    [Alias("SetDriverOverridesAsync")]
    Task SetDriverOverridesAsync(IReadOnlyDictionary<Guid, DriverKind> overrides);

    [Alias("SendNewMessageAsync")]
    Task<ChatMessage> SendNewMessageAsync(
        Guid senderId,
        string content,
        string? reasoning = null,
        string? appraisal = null,
        CancellationToken cancellationToken = default);


    [Alias("RecordSkippedTurnAsync")]
    Task RecordSkippedTurnAsync(
        Guid personaId,
        string personaName,
        int triggeredByMessageId,
        double urgeTotal,
        string reason);

    [Alias("GetSkippedTurnsAsync")]
    Task<IReadOnlyList<SkippedTurn>> GetSkippedTurnsAsync();

    [Alias("IsAnsweringAsync")]
    Task<bool> IsAnsweringAsync(Guid senderId);

    [Alias("RecordRaceEvaluationAsync")]
    Task RecordRaceEvaluationAsync(
        Guid personaId,
        string personaName,
        int triggeredByMessageId,
        string outcome,
        double? salience,
        double? cancelScore);

    [Alias("GetRaceEvaluationsAsync")]
    Task<IReadOnlyList<RaceEvaluation>> GetRaceEvaluationsAsync();

    [Alias("DeleteMessageAsync")]
    Task DeleteMessageAsync(int messageId);

    [Alias("DeleteMessagesAfterAsync")]
    Task DeleteMessagesAfterAsync(int messageId);

    [Alias("NotifyStreamChunkAsync")]
    Task NotifyStreamChunkAsync(int messageId, MessageStreamEvent evt);

    [Alias("GetMessagesUntilAsync")]
    Task<List<ChatMessage>> GetMessagesUntilAsync(int messageId, Guid? listener = null);

    [Alias("CountTrailingAssistantMessagesAsync")]
    Task<int> CountTrailingAssistantMessagesAsync();
}

[GenerateSerializer, Alias(nameof(ChatGroupState))]
public sealed record class ChatGroupState
{
    [Id(0)]
    public Guid PartyId { get; set; }

    [Id(1)]
    public List<ChatMessage> Messages { get; set; } = [];

    [Id(2)]
    public int NextMessageId { get; set; }

    [Id(3)]
    public HashSet<Guid> ParticipantIds { get; set; } = new();

    [Id(4)]
    public bool Initialized { get; set; }

    [Id(5)]
    public string? Scenario { get; set; }

    [Id(6)]
    public List<SkippedTurn> SkippedTurns { get; set; } = [];

    [Id(7)]
    public List<RaceEvaluation> RaceEvaluations { get; set; } = [];

    [Id(8)]
    public Dictionary<Guid, DriverKind> DriverOverrides { get; set; } = new();

    public void Apply(ChatGroupInitializedEvent @event)
    {
        PartyId = @event.PartyId;
        ParticipantIds = new HashSet<Guid>(@event.ParticipantIds);
        Scenario = @event.Scenario;
        Initialized = true;
    }

    public void Apply(ChatGroupParticipantIdsSetEvent @event)
    {
        ParticipantIds = new HashSet<Guid>(@event.ParticipantIds);
    }

    public void Apply(ChatGroupDriverOverrideSetEvent @event)
    {
        DriverOverrides[@event.PersonaId] = @event.Kind;
    }

    public void Apply(ChatGroupDriverOverrideClearedEvent @event)
    {
        DriverOverrides.Remove(@event.PersonaId);
    }

    public void Apply(ChatGroupDriverOverridesReplacedEvent @event)
    {
        DriverOverrides = new Dictionary<Guid, DriverKind>(@event.Overrides);
    }

    public void Apply(ChatGroupScenarioSetEvent @event)
    {
        Scenario = @event.Scenario;
    }

    public void Apply(ChatGroupMessageSlotReservedEvent @event)
    {
        NextMessageId++;

        var stub = new ChatMessage
        {
            MessageId = NextMessageId,
            SenderType = @event.SenderType ?? "assistant",
            SenderId = @event.SenderId ?? Guid.Empty,
            ChatGroupId = @event.ChatGroupId,
            Content = string.Empty,
            SendAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            TriggeredByMessageId = @event.TriggeredByMessageId
        };
        Messages.Add(stub);
    }

    public void Apply(ChatGroupUserMessageEvent @event)
    {
        var idx = Messages.FindIndex(m => m.MessageId == @event.Message.MessageId);
        if (idx >= 0)
            Messages[idx] = @event.Message with { };
        else
            Messages.Add(@event.Message with { });
        NextMessageId = Math.Max(NextMessageId, @event.Message.MessageId);
    }

    public void Apply(ChatGroupGenerationCompletedEvent @event)
    {
        var target = Messages.FirstOrDefault(m => m.MessageId == @event.MessageId);
        if (target is null) return;

        target.Content = @event.Content;
        target.Reasoning = @event.Reasoning;
        target.Appraisal = @event.Appraisal;
        target.SenderId = @event.SenderId;
        target.SendAt = @event.SendAt;
        target.Metadata = @event.Metadata;
        if (@event.TriggeredByMessageId.HasValue)
            target.TriggeredByMessageId = @event.TriggeredByMessageId;
        target.HeardBy = @event.HeardBy;
        target.Volume = @event.Volume;
    }

    public void Apply(ChatGroupGenerationStoppedEvent @event)
    {
        var target = Messages.FirstOrDefault(m => m.MessageId == @event.MessageId);
        if (target is null) return;

        target.Appraisal = @event.Appraisal;
        target.SendAt = @event.SendAt;
        if (@event.TriggeredByMessageId.HasValue)
            target.TriggeredByMessageId = @event.TriggeredByMessageId;
    }

    public void Apply(ChatGroupGenerationFailedEvent @event)
    {
        var target = Messages.FirstOrDefault(m => m.MessageId == @event.MessageId);
        if (target is null) return;

        target.Error = @event.Error;
        target.SendAt = @event.SendAt;
    }

    public void Apply(ChatGroupGenerationCancelledAsEmoteEvent @event)
    {
        var target = Messages.FirstOrDefault(m => m.MessageId == @event.MessageId);
        if (target is null) return;

        target.Content = @event.Content;
        target.Kind = "emote";
        target.Appraisal = @event.Appraisal;
        target.SendAt = @event.SendAt;
        target.Error = null;
        if (@event.TriggeredByMessageId.HasValue)
            target.TriggeredByMessageId = @event.TriggeredByMessageId;
    }

    public void Apply(ChatGroupMessageDeletedEvent @event)
        => Messages.RemoveAll(m => m.MessageId == @event.MessageId);

    public void Apply(ChatGroupMessagesAfterDeletedEvent @event)
    {
        Messages.RemoveAll(m => m.MessageId > @event.MessageId);
        NextMessageId = Messages.Count > 0 ? Messages.Max(m => m.MessageId) : 0;
        SkippedTurns.RemoveAll(s => s.TriggeredByMessageId > @event.MessageId);
    }

    public void Apply(ChatGroupPersonaSkippedEvent @event)
    {
        SkippedTurns.Add(new SkippedTurn
        {
            PersonaId = @event.PersonaId,
            PersonaName = @event.PersonaName,
            TriggeredByMessageId = @event.TriggeredByMessageId,
            UrgeTotal = @event.UrgeTotal,
            Reason = @event.Reason,
            SendAt = @event.SendAt
        });
    }

    public void Apply(ChatGroupPersonaRaceEvaluationEvent @event)
    {
        RaceEvaluations.Add(new RaceEvaluation
        {
            PersonaId = @event.PersonaId,
            PersonaName = @event.PersonaName,
            TriggeredByMessageId = @event.TriggeredByMessageId,
            Outcome = @event.Outcome,
            Salience = @event.Salience,
            CancelScore = @event.CancelScore,
            SendAt = @event.SendAt
        });
    }

    public void Apply(ChatGroupMessagesImportedEvent @event)
    {
        foreach (var imported in @event.Messages)
        {
            NextMessageId++;
            Messages.Add(new ChatMessage
            {
                ChatGroupId = imported.ChatGroupId,
                MessageId = NextMessageId,
                Content = imported.Content,
                SenderType = imported.SenderType,
                SenderId = imported.SenderId,
                SendAt = imported.SendAt,
                Kind = imported.Kind,
                Metadata = imported.Metadata
            });
        }
    }
}

[GenerateSerializer, Alias(nameof(SkippedTurn))]
public sealed record class SkippedTurn
{
    [Id(0)] public Guid PersonaId { get; set; }
    [Id(1)] public string PersonaName { get; set; } = string.Empty;
    [Id(2)] public int TriggeredByMessageId { get; set; }
    [Id(3)] public double UrgeTotal { get; set; }
    [Id(4)] public string Reason { get; set; } = string.Empty;
    [Id(5)] public long SendAt { get; set; }
}

[GenerateSerializer, Alias(nameof(RaceEvaluation))]
public sealed record class RaceEvaluation
{
    [Id(0)] public Guid PersonaId { get; set; }
    [Id(1)] public string PersonaName { get; set; } = string.Empty;
    [Id(2)] public int TriggeredByMessageId { get; set; }
    [Id(3)] public string Outcome { get; set; } = string.Empty;
    [Id(4)] public double? Salience { get; set; }
    [Id(5)] public double? CancelScore { get; set; }
    [Id(6)] public long SendAt { get; set; }
}

[GenerateSerializer, Alias(nameof(ChatGroupEvent))]
public abstract record class ChatGroupEvent;

[GenerateSerializer, Alias(nameof(ChatGroupInitializedEvent))]
public sealed record class ChatGroupInitializedEvent : ChatGroupEvent
{
    [Id(0)] public Guid PartyId { get; set; }
    [Id(2)] public HashSet<Guid> ParticipantIds { get; set; } = new();
    [Id(3)] public string? Scenario { get; set; }
}

[GenerateSerializer, Alias(nameof(ChatGroupScenarioSetEvent))]
public sealed record class ChatGroupScenarioSetEvent : ChatGroupEvent
{
    [Id(0)] public string? Scenario { get; set; }
}

[GenerateSerializer, Alias(nameof(ChatGroupParticipantIdsSetEvent))]
public sealed record class ChatGroupParticipantIdsSetEvent : ChatGroupEvent
{
    [Id(0)] public HashSet<Guid> ParticipantIds { get; set; } = new();
}

[GenerateSerializer, Alias(nameof(ChatGroupDriverOverrideSetEvent))]
public sealed record class ChatGroupDriverOverrideSetEvent : ChatGroupEvent
{
    [Id(0)] public Guid PersonaId { get; set; }
    [Id(1)] public DriverKind Kind { get; set; }
}

[GenerateSerializer, Alias(nameof(ChatGroupDriverOverrideClearedEvent))]
public sealed record class ChatGroupDriverOverrideClearedEvent : ChatGroupEvent
{
    [Id(0)] public Guid PersonaId { get; set; }
}

[GenerateSerializer, Alias(nameof(ChatGroupDriverOverridesReplacedEvent))]
public sealed record class ChatGroupDriverOverridesReplacedEvent : ChatGroupEvent
{
    [Id(0)] public Dictionary<Guid, DriverKind> Overrides { get; set; } = new();
}

[GenerateSerializer, Alias(nameof(ChatGroupUserMessageEvent))]
public sealed record class ChatGroupUserMessageEvent : ChatGroupEvent
{
    [Id(0)] public Guid SenderId { get; set; }
    [Id(2)] public ChatMessage Message { get; set; } = new();
}

[GenerateSerializer, Alias(nameof(ChatGroupGenerationCompletedEvent))]
public sealed record class ChatGroupGenerationCompletedEvent : ChatGroupEvent
{
    [Id(0)] public int MessageId { get; set; }
    [Id(1)] public string? Content { get; set; }
    [Id(2)] public string? Reasoning { get; set; }
    [Id(3)] public string? Appraisal { get; set; }
    [Id(4)] public Guid SenderId { get; set; }
    [Id(5)] public long SendAt { get; set; }
    [Id(6)] public ChatMessageMetadata? Metadata { get; set; }
    [Id(7)] public int? TriggeredByMessageId { get; set; }
    [Id(8)] public List<Guid>? HeardBy { get; set; }
    [Id(9)] public string? Volume { get; set; }
}

[GenerateSerializer, Alias(nameof(ChatGroupGenerationStoppedEvent))]
public sealed record class ChatGroupGenerationStoppedEvent : ChatGroupEvent
{
    [Id(0)] public int MessageId { get; set; }
    [Id(1)] public string? Appraisal { get; set; }
    [Id(2)] public long SendAt { get; set; }
    [Id(3)] public int? TriggeredByMessageId { get; set; }
}

[GenerateSerializer, Alias(nameof(ChatGroupGenerationFailedEvent))]
public sealed record class ChatGroupGenerationFailedEvent : ChatGroupEvent
{
    [Id(0)] public int MessageId { get; set; }
    [Id(1)] public string Error { get; set; } = string.Empty;
    [Id(2)] public long SendAt { get; set; }
}

[GenerateSerializer, Alias(nameof(ChatGroupMessageDeletedEvent))]
public sealed record class ChatGroupMessageDeletedEvent : ChatGroupEvent
{
    [Id(0)] public int MessageId { get; set; }
}

[GenerateSerializer, Alias(nameof(ChatGroupMessageSlotReservedEvent))]
public sealed record class ChatGroupMessageSlotReservedEvent : ChatGroupEvent
{
    [Id(0)] public Guid? SenderId { get; set; }
    [Id(1)] public string? SenderType { get; set; }
    [Id(2)] public Guid ChatGroupId { get; set; }
    [Id(3)] public int? TriggeredByMessageId { get; set; }
}

[GenerateSerializer, Alias(nameof(ChatGroupMessagesAfterDeletedEvent))]
public sealed record class ChatGroupMessagesAfterDeletedEvent : ChatGroupEvent
{
    [Id(0)] public int MessageId { get; set; }
}

[GenerateSerializer, Alias(nameof(ChatGroupPersonaSkippedEvent))]
public sealed record class ChatGroupPersonaSkippedEvent : ChatGroupEvent
{
    [Id(0)] public Guid PersonaId { get; set; }
    [Id(1)] public string PersonaName { get; set; } = string.Empty;
    [Id(2)] public int TriggeredByMessageId { get; set; }
    [Id(3)] public double UrgeTotal { get; set; }
    [Id(4)] public string Reason { get; set; } = string.Empty;
    [Id(5)] public long SendAt { get; set; }
}

[GenerateSerializer, Alias(nameof(ChatGroupPersonaRaceEvaluationEvent))]
public sealed record class ChatGroupPersonaRaceEvaluationEvent : ChatGroupEvent
{
    [Id(0)] public Guid PersonaId { get; set; }
    [Id(1)] public string PersonaName { get; set; } = string.Empty;
    [Id(2)] public int TriggeredByMessageId { get; set; }
    [Id(3)] public string Outcome { get; set; } = string.Empty;
    [Id(4)] public double? Salience { get; set; }
    [Id(5)] public double? CancelScore { get; set; }
    [Id(6)] public long SendAt { get; set; }
}

[GenerateSerializer, Alias(nameof(ChatGroupGenerationCancelledAsEmoteEvent))]
public sealed record class ChatGroupGenerationCancelledAsEmoteEvent : ChatGroupEvent
{
    [Id(0)] public int MessageId { get; set; }
    [Id(1)] public string Content { get; set; } = string.Empty;
    [Id(2)] public string? Appraisal { get; set; }
    [Id(3)] public int? TriggeredByMessageId { get; set; }
    [Id(4)] public long SendAt { get; set; }
}

[GenerateSerializer, Alias(nameof(ImportedMessage))]
public sealed record class ImportedMessage
{
    [Id(0)] public Guid SenderId { get; set; }
    [Id(1)] public string SenderType { get; set; } = "user";
    [Id(2)] public string Content { get; set; } = string.Empty;
    [Id(3)] public long SendAt { get; set; }
    [Id(4)] public string? Kind { get; set; }
    [Id(5)] public Guid ChatGroupId { get; set; }
    [Id(6)] public ChatMessageMetadata? Metadata { get; set; }
}

[GenerateSerializer, Alias(nameof(ChatGroupMessagesImportedEvent))]
public sealed record class ChatGroupMessagesImportedEvent : ChatGroupEvent
{
    [Id(0)] public List<ImportedMessage> Messages { get; set; } = [];
}
