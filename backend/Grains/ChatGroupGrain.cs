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
    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("ChatGroupGrain activated: {ChatGroupId}", this.GetPrimaryKey());
        return base.OnActivateAsync(cancellationToken);
    }

    public async Task InitializeAsync(Guid partyId, List<PartyParticipant> participants)
    {
        if (State.Initialized)
            return;

        RaiseEvent(new ChatGroupInitializedEvent
        {
            PartyId = partyId,
            Participants = [.. participants]
        });
        await ConfirmEvents();
    }

    public Task<Guid> GetPartyIdAsync() => Task.FromResult(State.PartyId);

    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync()
        => Task.FromResult<IReadOnlyList<ChatMessage>>(State.Messages.OrderBy(m => m.MessageId).ToList());

    public async Task<int> GetNextMessageIdAsync()
    {
        RaiseEvent(new ChatGroupMessageSlotReservedEvent());
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
        string? appraisal)
    {
        var chatGroupId = this.GetPrimaryKey();
        var messageId = await GetNextMessageIdAsync();

        var message = new ChatMessage
        {
            ChatGroupId = chatGroupId,
            MessageId = messageId,
            Content = content,
            SenderType = "user",
            SenderId = senderId,
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

        _ = NotifyAllParticipantsAsync(message);

        return message;
    }

    public async Task AppendMessageAsync(
        int messageId,
        string content,
        string? reasoning,
        string? appraisal)
    {
        var chatGroupId = this.GetPrimaryKey();
        var message = State.Messages.FirstOrDefault(m => m.MessageId == messageId)
            ?? throw new ArgumentException($"Message with id {messageId} not found", nameof(messageId));

        RaiseEvent(new ChatGroupGenerationCompletedEvent
        {
            MessageId = messageId,
            Content = content,
            Reasoning = reasoning,
            Appraisal = appraisal,
            SenderId = message.SenderId,
            SendAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
        await ConfirmEvents();

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

    public async Task MarkGenerationStoppedAsync(int messageId, string? appraisal)
    {
        var chatGroupId = this.GetPrimaryKey();

        RaiseEvent(new ChatGroupGenerationStoppedEvent
        {
            MessageId = messageId,
            Appraisal = appraisal,
            SendAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
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

    public Task<int> CountTrailingAssistantMessagesAsync()
    {
        var count = 0;
        for (var i = State.Messages.Count - 1; i >= 0; i--)
        {
            if (State.Messages[i].SenderType != "assistant")
                break;
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
    public Task NotifyAllParticipantsAsync(ChatMessage triggeringMessage)
    {
        var chatGroupId = this.GetPrimaryKey();
        var participants = State.Participants.ToList();

        logger.LogInformation("Fanning out to {Count} participants in chat group {ChatGroupId}",
            participants.Count, chatGroupId);

        foreach (var p in participants)
        {
            _ = GrainFactory.GetGrain<IPersonaGrain>(p.Id)
                .NotifyMessageAsync(chatGroupId, triggeringMessage);
        }

        return Task.CompletedTask;
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
    [Alias("InitializeAsync")]
    Task InitializeAsync(Guid partyId, List<PartyParticipant> participants);

    [Alias("GetPartyIdAsync")]
    Task<Guid> GetPartyIdAsync();

    [Alias("GetMessagesAsync")]
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync();

    [Alias("GetNextMessageIdAsync")]
    Task<int> GetNextMessageIdAsync();

    [Alias("SetParticipantsAsync")]
    Task SetParticipantsAsync(List<PartyParticipant> participants);

    [Alias("SendNewMessageAsync")]
    Task<ChatMessage> SendNewMessageAsync(
        Guid senderId,
        string content,
        string? reasoning = null,
        string? appraisal = null);

    [Alias("AppendMessageAsync")]
    Task AppendMessageAsync(
        int messageId,
        string content,
        string? reasoning = null,
        string? appraisal = null);

    [Alias("MarkGenerationStoppedAsync")]
    Task MarkGenerationStoppedAsync(int messageId, string? appraisal);

    [Alias("MarkGenerationFailedAsync")]
    Task MarkGenerationFailedAsync(int messageId, string error);

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
    Task NotifyAllParticipantsAsync(ChatMessage triggeringMessage);
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

    public void Apply(ChatGroupInitializedEvent @event)
    {
        PartyId = @event.PartyId;
        Participants = [.. @event.Participants];
        Initialized = true;
    }

    public void Apply(ChatGroupParticipantsSetEvent @event)
    {
        Participants = [.. @event.Participants];
    }

    public void Apply(ChatGroupMessageSlotReservedEvent _)
    {
        NextMessageId++;
    }

    public void Apply(ChatGroupUserMessageEvent @event)
    {
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
    }

    public void Apply(ChatGroupGenerationStoppedEvent @event)
    {
        var target = Messages.FirstOrDefault(m => m.MessageId == @event.MessageId);
        if (target is null) return;

        target.Appraisal = @event.Appraisal;
        target.SenderId = Guid.Empty;
        target.SendAt = @event.SendAt;
    }

    public void Apply(ChatGroupGenerationFailedEvent @event)
    {
        var target = Messages.FirstOrDefault(m => m.MessageId == @event.MessageId);
        if (target is null) return;

        target.Error = @event.Error;
        target.SendAt = @event.SendAt;
    }

    public void Apply(ChatGroupMessageDeletedEvent @event)
        => Messages.RemoveAll(m => m.MessageId == @event.MessageId);

    public void Apply(ChatGroupMessagesAfterDeletedEvent @event)
        => Messages.RemoveAll(m => m.MessageId > @event.MessageId);
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
}

/// <summary>Raised when a persona decides not to respond after reserving a message slot. Clears the message content and resets the sender.</summary>
[GenerateSerializer, Alias(nameof(ChatGroupGenerationStoppedEvent))]
public sealed record class ChatGroupGenerationStoppedEvent : ChatGroupEvent
{
    [Id(0)] public int MessageId { get; set; }
    [Id(1)] public string? Appraisal { get; set; }
    [Id(2)] public long SendAt { get; set; }
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
public sealed record class ChatGroupMessageSlotReservedEvent : ChatGroupEvent;

/// <summary>Raised when all messages after a given ID are deleted (used by reprompt to trim and regenerate).</summary>
[GenerateSerializer, Alias(nameof(ChatGroupMessagesAfterDeletedEvent))]
public sealed record class ChatGroupMessagesAfterDeletedEvent : ChatGroupEvent
{
    [Id(0)] public int MessageId { get; set; }
}
