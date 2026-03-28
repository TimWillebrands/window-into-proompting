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

    public async Task InitializeAsync(Guid partyId, ChatGroupInfo info, List<PartyParticipant> participants)
    {
        if (State.Initialized)
            return;

        RaiseEvent(new ChatGroupInitializedEvent
        {
            PartyId = partyId,
            Info = info,
            Participants = [.. participants]
        });
        await ConfirmEvents();
    }

    public Task<Guid> GetPartyIdAsync() => Task.FromResult(State.PartyId);

    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync()
        => Task.FromResult<IReadOnlyList<ChatMessage>>(State.Messages.OrderBy(m => m.MessageId).ToList());

    public Task<int> GetNextMessageIdAsync() => Task.FromResult(State.NextMessageId);

    public async Task SetParticipantsAsync(List<PartyParticipant> participants)
    {
        RaiseEvent(new ChatGroupParticipantsSetEvent { Participants = [.. participants] });
        await ConfirmEvents();
    }

    public async Task<ChatMessage> SendUserMessageAsync(Guid senderId, string senderName, string content)
    {
        var chatGroupId = this.GetPrimaryKey();
        var messageId = State.NextMessageId + 1;

        var userMessage = new ChatMessage
        {
            ChatGroupId = chatGroupId,
            MessageId = messageId,
            Content = content,
            SenderType = "user",
            SenderId = senderId,
            SenderName = senderName,
            SendAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        RaiseEvent(new ChatGroupUserMessageEvent
        {
            SenderId = senderId,
            SenderName = senderName,
            Message = userMessage
        });
        await ConfirmEvents();

        await PublishPartyEvent(new PartyStreamEvent
        {
            Type = "message",
            ChatGroupId = chatGroupId,
            Message = userMessage
        });

        return userMessage;
    }

    public async Task<ChatMessage> ReserveAssistantMessageAsync(string model)
    {
        var chatGroupId = this.GetPrimaryKey();
        var messageId = State.NextMessageId + 1;

        var assistantMessage = new ChatMessage
        {
            ChatGroupId = chatGroupId,
            MessageId = messageId,
            SenderType = "assistant",
            SenderId = Guid.Empty,
            SenderName = null,
            Content = null,
            SendAt = null,
            ModelEndpointStub = model
        };

        RaiseEvent(new ChatGroupAssistantReservedEvent { Message = assistantMessage });
        await ConfirmEvents();

        await PublishPartyEvent(new PartyStreamEvent
        {
            Type = "messageStream",
            ChatGroupId = chatGroupId,
            MessageId = assistantMessage.MessageId
        });

        return assistantMessage;
    }

    public async Task AppendPersonaResponseAsync(int messageId, string content, string? reasoning, string? overseer, Guid senderId)
    {
        var chatGroupId = this.GetPrimaryKey();

        RaiseEvent(new ChatGroupGenerationCompletedEvent
        {
            MessageId = messageId,
            Content = content,
            Reasoning = reasoning,
            Overseer = overseer,
            SenderId = senderId,
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

    public async Task MarkGenerationStoppedAsync(int messageId, string? overseer)
    {
        var chatGroupId = this.GetPrimaryKey();

        RaiseEvent(new ChatGroupGenerationStoppedEvent
        {
            MessageId = messageId,
            Overseer = overseer,
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
            Event = "error",
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

    public Task<List<MessageWithSender>> GetMessagesUntilAsync(int messageId)
    {
        var before = State.Messages
            .Where(m => m.MessageId < messageId)
            .OrderBy(m => m.MessageId)
            .ToArray();

        var participantsById = State.Participants.ToDictionary(x => x.Id, x => x.Name);

        var messages = before.Select(message => new MessageWithSender
        {
            ChatGroupId = message.ChatGroupId,
            MessageId = message.MessageId,
            Content = message.Content,
            Reasoning = message.Reasoning,
            Overseer = message.Overseer,
            Error = message.Error,
            SenderType = message.SenderType,
            SenderId = message.SenderId,
            SendAt = message.SendAt,
            ModelEndpointStub = message.ModelEndpointStub,
            SenderName = participantsById.TryGetValue(message.SenderId, out var name)
                ? name
                : State.UserNames.TryGetValue(message.SenderId, out var userName)
                    ? userName
                    : message.SenderId.ToString()
        }).ToList();

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
    /// Fan out a message notification to all AI participants in parallel.
    /// Each PersonaGrain independently decides whether to respond.
    /// </summary>
    public async Task NotifyAllParticipantsAsync(ChatMessage triggeringMessage, string model, string provider)
    {
        var chatGroupId = this.GetPrimaryKey();
        var aiParticipants = State.Participants.Where(p => !p.IsUser).ToList();

        logger.LogInformation("Fanning out to {Count} AI participants in chat group {ChatGroupId}",
            aiParticipants.Count, chatGroupId);

        var tasks = aiParticipants.Select(p =>
            GrainFactory.GetGrain<IPersonaGrain>(p.Id)
                .NotifyMessageAsync(chatGroupId, triggeringMessage, model, provider));

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Sends a user message and fans out to all AI participants.
    /// Used by the controller for the parallel generation flow.
    /// </summary>
    public async Task<ChatMessage> SendUserMessageAndNotifyAsync(Guid senderId, string senderName, string content, string model, string provider)
    {
        var userMessage = await SendUserMessageAsync(senderId, senderName, content);

        // Fire-and-forget fan-out to all AI participants
        _ = NotifyAllParticipantsAsync(userMessage, model, provider);

        return userMessage;
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

// ── Interface ──

[Alias("backend.IChatGroupGrain")]
public interface IChatGroupGrain : IGrainWithGuidKey
{
    [Alias("InitializeAsync")]
    Task InitializeAsync(Guid partyId, ChatGroupInfo info, List<PartyParticipant> participants);

    [Alias("GetPartyIdAsync")]
    Task<Guid> GetPartyIdAsync();

    [Alias("GetMessagesAsync")]
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync();

    [Alias("GetNextMessageIdAsync")]
    Task<int> GetNextMessageIdAsync();

    [Alias("SetParticipantsAsync")]
    Task SetParticipantsAsync(List<PartyParticipant> participants);

    [Alias("SendUserMessageAsync")]
    Task<ChatMessage> SendUserMessageAsync(Guid senderId, string senderName, string content);

    [Alias("ReserveAssistantMessageAsync")]
    Task<ChatMessage> ReserveAssistantMessageAsync(string model);

    [Alias("AppendPersonaResponseAsync")]
    Task AppendPersonaResponseAsync(int messageId, string content, string? reasoning, string? overseer, Guid senderId);

    [Alias("MarkGenerationStoppedAsync")]
    Task MarkGenerationStoppedAsync(int messageId, string? overseer);

    [Alias("MarkGenerationFailedAsync")]
    Task MarkGenerationFailedAsync(int messageId, string error);

    [Alias("DeleteMessageAsync")]
    Task DeleteMessageAsync(int messageId);

    [Alias("DeleteMessagesAfterAsync")]
    Task DeleteMessagesAfterAsync(int messageId);

    [Alias("NotifyStreamChunkAsync")]
    Task NotifyStreamChunkAsync(int messageId, MessageStreamEvent evt);

    [Alias("GetMessagesUntilAsync")]
    Task<List<MessageWithSender>> GetMessagesUntilAsync(int messageId);

    [Alias("GetParticipantsAsync")]
    Task<List<PartyParticipant>> GetParticipantsAsync();

    [Alias("CountTrailingAssistantMessagesAsync")]
    Task<int> CountTrailingAssistantMessagesAsync();

    [Alias("NotifyAllParticipantsAsync")]
    Task NotifyAllParticipantsAsync(ChatMessage triggeringMessage, string model, string provider);

    [Alias("SendUserMessageAndNotifyAsync")]
    Task<ChatMessage> SendUserMessageAndNotifyAsync(Guid senderId, string senderName, string content, string model, string provider);
}

// ── State ──

[GenerateSerializer, Alias(nameof(ChatGroupState))]
public sealed record class ChatGroupState
{
    [Id(0)]
    public Guid PartyId { get; set; }

    [Id(1)]
    public ChatGroupInfo Info { get; set; } = new();

    [Id(2)]
    public List<ChatMessage> Messages { get; set; } = [];

    [Id(3)]
    public int NextMessageId { get; set; }

    [Id(4)]
    public Dictionary<Guid, string> UserNames { get; set; } = [];

    [Id(5)]
    public List<PartyParticipant> Participants { get; set; } = [];

    [Id(6)]
    public bool Initialized { get; set; }

    public void Apply(ChatGroupInitializedEvent @event)
    {
        PartyId = @event.PartyId;
        Info = @event.Info;
        Participants = [.. @event.Participants];
        Initialized = true;
    }

    public void Apply(ChatGroupParticipantsSetEvent @event)
    {
        Participants = [.. @event.Participants];
    }

    public void Apply(ChatGroupUserMessageEvent @event)
    {
        UserNames[@event.SenderId] = @event.SenderName;
        Messages.Add(CloneMessage(@event.Message));
        NextMessageId = Math.Max(NextMessageId, @event.Message.MessageId);
    }

    public void Apply(ChatGroupAssistantReservedEvent @event)
    {
        Messages.Add(CloneMessage(@event.Message));
        NextMessageId = Math.Max(NextMessageId, @event.Message.MessageId);
    }

    public void Apply(ChatGroupGenerationCompletedEvent @event)
    {
        var target = Messages.FirstOrDefault(m => m.MessageId == @event.MessageId);
        if (target is null) return;

        target.Content = @event.Content;
        target.Reasoning = @event.Reasoning;
        target.Overseer = @event.Overseer;
        target.Error = null;
        target.SenderId = @event.SenderId;
        target.SenderName = Participants.FirstOrDefault(p => p.Id == @event.SenderId)?.Name;
        target.SendAt = @event.SendAt;
    }

    public void Apply(ChatGroupGenerationStoppedEvent @event)
    {
        var target = Messages.FirstOrDefault(m => m.MessageId == @event.MessageId);
        if (target is null) return;

        target.Content = null;
        target.Reasoning = null;
        target.Overseer = @event.Overseer;
        target.Error = null;
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

    private static ChatMessage CloneMessage(ChatMessage source)
        => new()
        {
            ChatGroupId = source.ChatGroupId,
            MessageId = source.MessageId,
            Content = source.Content,
            Reasoning = source.Reasoning,
            Overseer = source.Overseer,
            Error = source.Error,
            SenderType = source.SenderType,
            SenderId = source.SenderId,
            SenderName = source.SenderName,
            SendAt = source.SendAt,
            ModelEndpointStub = source.ModelEndpointStub
        };
}

// ── Events ──

[GenerateSerializer, Alias(nameof(ChatGroupEvent))]
public abstract record class ChatGroupEvent;

[GenerateSerializer, Alias(nameof(ChatGroupInitializedEvent))]
public sealed record class ChatGroupInitializedEvent : ChatGroupEvent
{
    [Id(0)] public Guid PartyId { get; set; }
    [Id(1)] public ChatGroupInfo Info { get; set; } = new();
    [Id(2)] public List<PartyParticipant> Participants { get; set; } = [];
}

[GenerateSerializer, Alias(nameof(ChatGroupParticipantsSetEvent))]
public sealed record class ChatGroupParticipantsSetEvent : ChatGroupEvent
{
    [Id(0)] public List<PartyParticipant> Participants { get; set; } = [];
}

[GenerateSerializer, Alias(nameof(ChatGroupUserMessageEvent))]
public sealed record class ChatGroupUserMessageEvent : ChatGroupEvent
{
    [Id(0)] public Guid SenderId { get; set; }
    [Id(1)] public string SenderName { get; set; } = string.Empty;
    [Id(2)] public ChatMessage Message { get; set; } = new();
}

[GenerateSerializer, Alias(nameof(ChatGroupAssistantReservedEvent))]
public sealed record class ChatGroupAssistantReservedEvent : ChatGroupEvent
{
    [Id(0)] public ChatMessage Message { get; set; } = new();
}

[GenerateSerializer, Alias(nameof(ChatGroupGenerationCompletedEvent))]
public sealed record class ChatGroupGenerationCompletedEvent : ChatGroupEvent
{
    [Id(0)] public int MessageId { get; set; }
    [Id(1)] public string? Content { get; set; }
    [Id(2)] public string? Reasoning { get; set; }
    [Id(3)] public string? Overseer { get; set; }
    [Id(4)] public Guid SenderId { get; set; }
    [Id(5)] public long SendAt { get; set; }
}

[GenerateSerializer, Alias(nameof(ChatGroupGenerationStoppedEvent))]
public sealed record class ChatGroupGenerationStoppedEvent : ChatGroupEvent
{
    [Id(0)] public int MessageId { get; set; }
    [Id(1)] public string? Overseer { get; set; }
    [Id(2)] public long SendAt { get; set; }
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

[GenerateSerializer, Alias(nameof(ChatGroupMessagesAfterDeletedEvent))]
public sealed record class ChatGroupMessagesAfterDeletedEvent : ChatGroupEvent
{
    [Id(0)] public int MessageId { get; set; }
}
