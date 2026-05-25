using System.Text.Json;
using Orleans.EventSourcing;
using Orleans.Providers;
using Orleans.Streams;
using PartyTown.Logging;
using PartyTown.Model;
using PartyTown.Services.Streaming;

namespace PartyTown.Grains;

/// <summary>
/// Event-sourced grain that owns the message log for a single chat group.
/// Keyed by chat group GUID. Publishes stream events to the parent party's stream
/// so existing WebSocket subscriptions continue to work.
/// </summary>
[LogConsistencyProvider(ProviderName = "PartyStateStorage")]
[StorageProvider(ProviderName = "parties")]
public sealed class ChatGroupGrain(ILogger<ChatGroupGrain> logger)
    : JournaledGrain<ChatGroupState, ChatGroupEvent>, IChatGroupGrain
{
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

        if (State.Initialized)
        {
            logger.LogInformation("ChatGroupGrain activated (from replay): {ChatGroupId}", this.GetPrimaryKey());
            return;
        }

        // Self-initialize from registry
        var registry = GrainFactory.GetGrain<IPartyRootGrain>(Guid.Empty);
        var partyId = await registry.GetPartyForChatGroup(this.GetPrimaryKey());
        if (partyId is null)
            throw new InvalidOperationException(
                $"ChatGroupGrain {this.GetPrimaryKey()} not registered in any party.");

        var partyGrain = GrainFactory.GetGrain<IPartyGrain>(partyId.Value);
        var party = await partyGrain.GetParty();
        var chatGroups = await partyGrain.GetChatGroups();
        var thisChatGroup = chatGroups.FirstOrDefault(g => g.Id == this.GetPrimaryKey());

        RaiseEvent(new ChatGroupInitializedEvent
        {
            PartyId = partyId.Value,
            Participants = [.. party.Participants],
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

    public async Task<int> GetNextMessageIdAsync(
        Guid? senderId = null,
        string senderType = "assistant",
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

    public async Task SetParticipantsAsync(List<PartyParticipant> participants)
    {

        RaiseEvent(new ChatGroupParticipantsSetEvent { Participants = [.. participants] });
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
        var messageId = await GetNextMessageIdAsync(senderId, "user");

        var message = new ChatMessage
        {
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

        _ = NotifyAllParticipantsAsync(message, ct);

        return message;
    }

    public async Task AppendMessageAsync(
        int messageId,
        string content,
        string? reasoning,
        string? appraisal,
        ChatMessageMetadata? metadata,
        int? triggeredByMessageId = null,
        CancellationToken ct = default)
    {

        var chatGroupId = this.GetPrimaryKey();
        var sendAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        RaiseEvent(new ChatGroupGenerationCompletedEvent
        {
            MessageId = messageId,
            Content = content,
            Reasoning = reasoning,
            Appraisal = appraisal,
            SenderId = State.Messages.FirstOrDefault(m => m.MessageId == messageId)?.SenderId ?? Guid.Empty,
            SendAt = sendAt,
            Metadata = metadata,
            TriggeredByMessageId = triggeredByMessageId
        });
        await ConfirmEvents();
        _ = NotifyAllParticipantsAsync(State.Messages.FirstOrDefault(m => m.MessageId == messageId)!, ct);

        var updatedMessage = State.Messages.FirstOrDefault(m => m.MessageId == messageId);
        if (updatedMessage is not null)
        {
            await PublishPartyEvent(new PartyStreamEvent
            {
                Type = "message",
                ChatGroupId = chatGroupId,
                Message = updatedMessage
            });
        }
    }

    public async Task MarkGenerationStoppedAsync(int messageId, string? appraisal, int? triggeredByMessageId = null)
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

    public async Task MarkGenerationFailedAsync(int messageId, string error)
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

    /// <summary>
    /// Race-cancelled generation: instead of leaving an empty slot with a red "cancelled"
    /// error, ship a short in-character abandonment line ("emote") into the existing slot
    /// so the chat reads as a beat of character behaviour rather than an error. The message
    /// is marked <c>Kind="emote"</c> so the frontend can render it distinctly. Other personas
    /// see it in history and may react.
    /// </summary>
    public async Task MarkGenerationCancelledAsEmoteAsync(
        int messageId,
        string emoteContent,
        string? appraisal,
        int? triggeredByMessageId = null)
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

    public Task<List<ChatMessage>> GetMessagesUntilAsync(int messageId)
    {
        var before = State.Messages
            .Where(m => m.MessageId < messageId)
            .OrderBy(m => m.MessageId)
            .ToArray();

        var messages = before.Select(message => message with { }).ToList();

        return Task.FromResult(messages);
    }

    public Task<List<PartyParticipant>> GetParticipantsAsync()
        => Task.FromResult(new List<PartyParticipant>([.. State.Participants]));

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
    }

    public Task<IReadOnlyList<SkippedTurn>> GetSkippedTurnsAsync()
        => Task.FromResult<IReadOnlyList<SkippedTurn>>(State.SkippedTurns.ToList());

    /// <summary>
    /// Persist one stop-signal race outcome from <c>PersonaGrain.RunStopSignalRaceAsync</c>.
    /// Mirrors <see cref="RecordSkippedTurnAsync"/> shape so the thought-log papertrail
    /// can show race activity (cancel-decision / cancel-generation / past-pnr / continue)
    /// alongside go and skip entries.
    /// </summary>
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

        // Live update: surface the race outcome to subscribed UIs immediately. Persistence
        // (above) is for replay/reload — without the stream event the thought log wouldn't
        // refresh until the next page load.
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
    {
        var count = 0;
        for (var i = State.Messages.Count - 1; i >= 0; i--)
        {
            var msg = State.Messages[i];
            if (msg.SenderType != "assistant")
                break;
            if (string.IsNullOrEmpty(msg.Content))
                continue;
            count++;
        }
        return Task.FromResult(count);
    }

    /// <summary>
    /// Fan out a message notification to all participants in parallel.
    /// Each PersonaGrain independently decides whether to respond.
    /// Fire and forget - we don't await because PersonaGrains call back into this
    /// grain (GetMessagesUntilAsync) which would cause a deadlock on a non-reentrant grain.
    /// </summary>
    public Task NotifyAllParticipantsAsync(ChatMessage triggeringMessage, CancellationToken ct = default)
    {
        var chatGroupId = this.GetPrimaryKey();
        var participants = SelectAutoRespondTargets(State.Participants, triggeringMessage.SenderId);

        logger.LogInformation("Fanning out to {Count} AI participants in chat group {ChatGroupId}",
            participants.Count, chatGroupId);

        foreach (var p in participants)
        {
            _ = GrainFactory.GetGrain<IPersonaGrain>(p.Id)
                .NotifyMessageAsync(chatGroupId, triggeringMessage, ct);
        }

        return Task.CompletedTask;
    }

    // System driver is a hard guard, not chattiness=0 — Narrator must never auto-speak
    // (ADR 0012). Sender exclusion avoids the thought-log-per-turn no-op of re-reacting
    // to one's own message. Public-static so the guard is unit-testable without the
    // TestCluster fanout interceptor.
    public static List<PartyParticipant> SelectAutoRespondTargets(
        IEnumerable<PartyParticipant> participants,
        Guid? senderId)
        => participants
            .Where(p => p.Driver == DriverKind.LLM && p.Id != senderId)
            .ToList();

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
    [Alias("GetPartyIdAsync")]
    Task<Guid> GetPartyIdAsync();

    [Alias("GetScenarioAsync")]
    Task<string?> GetScenarioAsync();

    [Alias("SetScenarioAsync")]
    Task SetScenarioAsync(string? scenario);

    [Alias("GetMessagesAsync")]
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync();

    [Alias("GetNextMessageIdAsync")]
    Task<int> GetNextMessageIdAsync(
        Guid? senderId = null,
        string senderType = "assistant",
        int? triggeredByMessageId = null);

    [Alias("SetParticipantsAsync")]
    Task SetParticipantsAsync(List<PartyParticipant> participants);

    [Alias("SendNewMessageAsync")]
    Task<ChatMessage> SendNewMessageAsync(
        Guid senderId,
        string content,
        string? reasoning = null,
        string? appraisal = null,
        CancellationToken cancellationToken = default);

    [Alias("AppendMessageAsync")]
    Task AppendMessageAsync(
        int messageId,
        string content,
        string? reasoning = null,
        string? appraisal = null,
        ChatMessageMetadata? metadata = null,
        int? triggeredByMessageId = null,
        CancellationToken cancellationToken = default);

    [Alias("MarkGenerationStoppedAsync")]
    Task MarkGenerationStoppedAsync(int messageId, string? appraisal, int? triggeredByMessageId = null);

    [Alias("RecordSkippedTurnAsync")]
    Task RecordSkippedTurnAsync(
        Guid personaId,
        string personaName,
        int triggeredByMessageId,
        double urgeTotal,
        string reason);

    [Alias("GetSkippedTurnsAsync")]
    Task<IReadOnlyList<SkippedTurn>> GetSkippedTurnsAsync();

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

    [Alias("MarkGenerationFailedAsync")]
    Task MarkGenerationFailedAsync(int messageId, string error);

    [Alias("MarkGenerationCancelledAsEmoteAsync")]
    Task MarkGenerationCancelledAsEmoteAsync(
        int messageId,
        string emoteContent,
        string? appraisal,
        int? triggeredByMessageId = null);

    [Alias("DeleteMessageAsync")]
    Task DeleteMessageAsync(int messageId);

    [Alias("DeleteMessagesAfterAsync")]
    Task DeleteMessagesAfterAsync(int messageId);

    [Alias("NotifyStreamChunkAsync")]
    Task NotifyStreamChunkAsync(int messageId, MessageStreamEvent evt);

    [Alias("GetMessagesUntilAsync")]
    Task<List<ChatMessage>> GetMessagesUntilAsync(int messageId);

    [Alias("GetParticipantsAsync")]
    Task<List<PartyParticipant>> GetParticipantsAsync();

    [Alias("CountTrailingAssistantMessagesAsync")]
    Task<int> CountTrailingAssistantMessagesAsync();

    [Alias("NotifyAllParticipantsAsync")]
    Task NotifyAllParticipantsAsync(ChatMessage triggeringMessage, CancellationToken cancellationToken = default);
}

// ── State ──

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
    public List<PartyParticipant> Participants { get; set; } = [];

    [Id(4)]
    public bool Initialized { get; set; }

    [Id(5)]
    public string? Scenario { get; set; }

    /// <summary>Persona-turn skips (no slot reserved, no LLM call). Drives papertrail's
    /// reactions-under-each-message tree without bloating the message log.</summary>
    [Id(6)]
    public List<SkippedTurn> SkippedTurns { get; set; } = [];

    /// <summary>Stop-signal race outcomes per (persona, triggering message). Drives the
    /// race section of the thought-log: shows when an in-flight generation was cancelled,
    /// shipped past PNR, or let-it-ride. Includes salience scores when the race went
    /// through the LFM2 classifier.</summary>
    [Id(7)]
    public List<RaceEvaluation> RaceEvaluations { get; set; } = [];

    public void Apply(ChatGroupInitializedEvent @event)
    {
        PartyId = @event.PartyId;
        Participants = [.. @event.Participants];
        Scenario = @event.Scenario;
        Initialized = true;
    }

    public void Apply(ChatGroupParticipantsSetEvent @event)
    {
        Participants = [.. @event.Participants];
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
        // Slot-reserve already records this; preserve the slot value when present and
        // only overwrite if the event carries one (legacy events leave it null).
        if (@event.TriggeredByMessageId.HasValue)
            target.TriggeredByMessageId = @event.TriggeredByMessageId;
    }

    public void Apply(ChatGroupGenerationStoppedEvent @event)
    {
        var target = Messages.FirstOrDefault(m => m.MessageId == @event.MessageId);
        if (target is null) return;

        // Preserve the slot's reserved SenderId — papertrail needs it to resolve the
        // persona name for "declined" entries. (Earlier code wiped it to Guid.Empty.)
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

        // Treat the cancelled slot as a real (emote) message: real Content + Kind tag,
        // appraisal preserved for the thought log, no Error so the frontend doesn't show
        // the red error chrome.
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
        // Skips referencing now-deleted triggers would dangle; drop them.
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
}

/// <summary>A persona-turn that ended in <c>IsObviousSkip</c>: no slot, no LLM call.
/// Lives in <see cref="ChatGroupState.SkippedTurns"/> so the papertrail can show all
/// reactions to a triggering message, not just the ones that produced output.</summary>
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

/// <summary>One stop-signal race outcome from <c>PersonaGrain.RunStopSignalRaceAsync</c>.
/// Persisted alongside <see cref="SkippedTurn"/> so the thought-log can render the full
/// taxonomy of persona reactions: go (Message.Appraisal), decline (Message.Appraisal stop=true),
/// skip (SkippedTurn), race (this).</summary>
[GenerateSerializer, Alias(nameof(RaceEvaluation))]
public sealed record class RaceEvaluation
{
    [Id(0)] public Guid PersonaId { get; set; }
    [Id(1)] public string PersonaName { get; set; } = string.Empty;
    [Id(2)] public int TriggeredByMessageId { get; set; }
    /// <summary>One of: <c>cancel-decision</c>, <c>cancel-generation</c>, <c>past-pnr</c>, <c>continue</c>.</summary>
    [Id(3)] public string Outcome { get; set; } = string.Empty;
    /// <summary>LFM2 salience score (0..1). Null for branches that didn't call the classifier
    /// (decision-phase cancel, past-PNR).</summary>
    [Id(4)] public double? Salience { get; set; }
    /// <summary>Computed <c>salience × (1 − impulsivity) × (1 − commitmentProgress)</c>.
    /// Null for branches that didn't compute it.</summary>
    [Id(5)] public double? CancelScore { get; set; }
    [Id(6)] public long SendAt { get; set; }
}

// ── Events ──

[GenerateSerializer, Alias(nameof(ChatGroupEvent))]
public abstract record class ChatGroupEvent;

/// <summary>Raised once when a chat group is first created. Sets the parent party, initial participants, and marks the grain as initialized.</summary>
[GenerateSerializer, Alias(nameof(ChatGroupInitializedEvent))]
public sealed record class ChatGroupInitializedEvent : ChatGroupEvent
{
    [Id(0)] public Guid PartyId { get; set; }
    [Id(2)] public List<PartyParticipant> Participants { get; set; } = [];
    [Id(3)] public string? Scenario { get; set; }
}

/// <summary>Raised when the chat group's scenario (free-text setting/context) is set or updated.</summary>
[GenerateSerializer, Alias(nameof(ChatGroupScenarioSetEvent))]
public sealed record class ChatGroupScenarioSetEvent : ChatGroupEvent
{
    [Id(0)] public string? Scenario { get; set; }
}

/// <summary>Raised when the participant list is replaced wholesale (e.g. personas added/removed from the group).</summary>
[GenerateSerializer, Alias(nameof(ChatGroupParticipantsSetEvent))]
public sealed record class ChatGroupParticipantsSetEvent : ChatGroupEvent
{
    [Id(0)] public List<PartyParticipant> Participants { get; set; } = [];
}

/// <summary>Raised when a new message is sent (by a user or persona). Appends to the message log and advances the message counter.</summary>
[GenerateSerializer, Alias(nameof(ChatGroupUserMessageEvent))]
public sealed record class ChatGroupUserMessageEvent : ChatGroupEvent
{
    [Id(0)] public Guid SenderId { get; set; }
    [Id(2)] public ChatMessage Message { get; set; } = new();
}

/// <summary>Raised when an LLM generation finishes successfully. Fills in the content, reasoning, and appraisal fields on a previously sent message.</summary>
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
}

/// <summary>Raised when a persona decides not to respond after reserving a message slot. Clears the message content and resets the sender.</summary>
[GenerateSerializer, Alias(nameof(ChatGroupGenerationStoppedEvent))]
public sealed record class ChatGroupGenerationStoppedEvent : ChatGroupEvent
{
    [Id(0)] public int MessageId { get; set; }
    [Id(1)] public string? Appraisal { get; set; }
    [Id(2)] public long SendAt { get; set; }
    [Id(3)] public int? TriggeredByMessageId { get; set; }
}

/// <summary>Raised when LLM generation fails with an error. Records the error on the message for client display.</summary>
[GenerateSerializer, Alias(nameof(ChatGroupGenerationFailedEvent))]
public sealed record class ChatGroupGenerationFailedEvent : ChatGroupEvent
{
    [Id(0)] public int MessageId { get; set; }
    [Id(1)] public string Error { get; set; } = string.Empty;
    [Id(2)] public long SendAt { get; set; }
}

/// <summary>Raised when a single message is deleted by a user.</summary>
[GenerateSerializer, Alias(nameof(ChatGroupMessageDeletedEvent))]
public sealed record class ChatGroupMessageDeletedEvent : ChatGroupEvent
{
    [Id(0)] public int MessageId { get; set; }
}

/// <summary>Raised to atomically reserve a unique message ID slot for a persona about to generate.</summary>
[GenerateSerializer, Alias(nameof(ChatGroupMessageSlotReservedEvent))]
public sealed record class ChatGroupMessageSlotReservedEvent : ChatGroupEvent
{
    [Id(0)] public Guid? SenderId { get; set; }
    [Id(1)] public string? SenderType { get; set; }
    [Id(2)] public Guid ChatGroupId { get; set; }
    [Id(3)] public int? TriggeredByMessageId { get; set; }
}

/// <summary>Raised when all messages after a given ID are deleted (used by reprompt to trim and regenerate).</summary>
[GenerateSerializer, Alias(nameof(ChatGroupMessagesAfterDeletedEvent))]
public sealed record class ChatGroupMessagesAfterDeletedEvent : ChatGroupEvent
{
    [Id(0)] public int MessageId { get; set; }
}

/// <summary>Raised when a persona is notified of a message but pre-gate (<c>IsObviousSkip</c>)
/// elects not to deliberate. No message slot is reserved and no LLM call is made;
/// recording the skip preserves the causal record for the papertrail.</summary>
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

/// <summary>Raised when the stop-signal race in <c>PersonaGrain</c> evaluates a new message
/// against an in-flight generation. One event per (persona, in-flight generation, new
/// triggering message) — surfaces the race in the user-facing thought log instead of
/// leaving it in OTel spans only.</summary>
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

/// <summary>Raised when the stop-signal race elects to cancel an in-flight generation.
/// Replaces the would-be <c>ChatGroupGenerationFailedEvent("cancelled")</c> with an
/// in-character abandonment line ("emote") so the chat reads as character behaviour
/// rather than an error.</summary>
[GenerateSerializer, Alias(nameof(ChatGroupGenerationCancelledAsEmoteEvent))]
public sealed record class ChatGroupGenerationCancelledAsEmoteEvent : ChatGroupEvent
{
    [Id(0)] public int MessageId { get; set; }
    [Id(1)] public string Content { get; set; } = string.Empty;
    [Id(2)] public string? Appraisal { get; set; }
    [Id(3)] public int? TriggeredByMessageId { get; set; }
    [Id(4)] public long SendAt { get; set; }
}
