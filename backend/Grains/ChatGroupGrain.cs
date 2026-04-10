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
    /// <summary>
    /// Activates the grain and, if not already initialized, resolves its owning party from the registry and initializes the chat group state.
    /// </summary>
    /// <param name="cancellationToken">Token to observe while waiting for activation and initialization operations.</param>
    /// <exception cref="InvalidOperationException">Thrown when this chat group is not registered with any party in the registry.</exception>
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

        var party = await GrainFactory.GetGrain<IPartyGrain>(partyId.Value).GetParty();

        RaiseEvent(new ChatGroupInitializedEvent
        {
            PartyId = partyId.Value,
            Participants = [.. party.Participants]
        });
        await ConfirmEvents();

        logger.LogInformation("ChatGroupGrain self-initialized: {ChatGroupId} in party {PartyId}",
            this.GetPrimaryKey(), partyId.Value);
    }

    /// <summary>
/// Gets the owning party's identifier for this chat group.
/// </summary>
/// <returns>The GUID of the party that owns this chat group.</returns>
public Task<Guid> GetPartyIdAsync() => Task.FromResult(State.PartyId);

    /// <summary>
        /// Retrieve a snapshot list of this chat group's messages ordered by MessageId (ascending).
        /// </summary>
        /// <returns>A new list containing the chat group's messages ordered by MessageId (ascending).</returns>
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync()
        => Task.FromResult<IReadOnlyList<ChatMessage>>(State.Messages.OrderBy(m => m.MessageId).ToList());

    /// <summary>
    /// Reserves the next message slot for this chat group and returns the assigned message id.
    /// </summary>
    /// <param name="senderId">Optional id of the sender to associate with the reserved slot.</param>
    /// <param name="senderType">Type of the sender (e.g., "assistant" or "user"); defaults to "assistant".</param>
    /// <returns>The newly reserved message id.</returns>
    public async Task<int> GetNextMessageIdAsync(Guid? senderId = null, string senderType = "assistant")
    {

        RaiseEvent(new ChatGroupMessageSlotReservedEvent
        {
            SenderId = senderId,
            SenderType = senderType,
            ChatGroupId = this.GetPrimaryKey()
        });
        await ConfirmEvents();
        return State.NextMessageId;
    }

    /// <summary>
    /// Replaces the chat group's participants with the provided list and persists the change.
    /// </summary>
    /// <param name="participants">The participants to set for the chat group; a copy of this list is persisted into state.</param>
    public async Task SetParticipantsAsync(List<PartyParticipant> participants)
    {

        RaiseEvent(new ChatGroupParticipantsSetEvent { Participants = [.. participants] });
        await ConfirmEvents();
    }

    /// <summary>
    /// Creates a new user message in this chat group, persists the event, publishes it to the party stream, and schedules participant notifications.
    /// </summary>
    /// <param name="senderId">The user ID of the message sender.</param>
    /// <param name="content">The message text content.</param>
    /// <param name="reasoning">Optional reasoning metadata associated with the message.</param>
    /// <param name="appraisal">Optional appraisal metadata associated with the message.</param>
    /// <returns>The persisted <see cref="ChatMessage"/> including the assigned MessageId and SendAt timestamp.</returns>
    public async Task<ChatMessage> SendNewMessageAsync(
        Guid senderId,
        string content,
        string? reasoning,
        string? appraisal)
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

        _ = NotifyAllParticipantsAsync(message);

        return message;
    }

    /// <summary>
    /// Finalizes a previously reserved message slot by applying generated content and publishing the updated message to the party stream.
    /// </summary>
    /// <remarks>
    /// The method records the current UTC send timestamp and sets the message's `Content`, `Reasoning`, `Appraisal`, `SenderId` (taken from the existing message or `Guid.Empty` if missing), and `SendAt`, persists the change as an event, and, if the message exists after persistence, publishes a `PartyStreamEvent` of type "message".</remarks>
    /// <param name="messageId">The identifier of the reserved message slot to update.</param>
    /// <param name="content">The generated message content.</param>
    /// <param name="reasoning">Optional generation reasoning to attach to the message.</param>
    /// <param name="appraisal">Optional human or automated appraisal to attach to the message.</param>
    public async Task AppendMessageAsync(
        int messageId,
        string content,
        string? reasoning,
        string? appraisal)
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
            SendAt = sendAt
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

    /// <summary>
    /// Record that generation for a reserved message was stopped and persist the stop event.
    /// </summary>
    /// <param name="messageId">The identifier of the message whose generation was stopped.</param>
    /// <param name="appraisal">Optional appraisal text to attach to the stopped generation.</param>
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

    /// <summary>
    /// Marks the specified message as having failed generation and notifies the message stream with the error details.
    /// </summary>
    /// <param name="messageId">The identifier of the message whose generation failed.</param>
    /// <param name="error">A description of the failure or error details to include in the stream notification.</param>
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
    /// Delete the message with the specified message id from this chat group's event-sourced log and publish a corresponding party stream delete event.
    /// </summary>
    /// <param name="messageId">Identifier of the message to remove from the chat group's message log.</param>
    public async Task DeleteMessageAsync(int messageId)
    {

        var chatGroupId = this.GetPrimaryKey();
        RaiseEvent(new ChatGroupMessageDeletedEvent { MessageId = messageId });
        await ConfirmEvents();
        await PublishPartyEvent(new PartyStreamEvent { Type = "deleteMessage", ChatGroupId = chatGroupId, MessageId = messageId });
    }

    /// <summary>
    /// Deletes all chat messages whose MessageId is greater than the provided messageId and notifies the parent party stream of the deletion.
    /// </summary>
    /// <param name="messageId">Remove messages with MessageId greater than this value.</param>
    public async Task DeleteMessagesAfterAsync(int messageId)
    {

        var chatGroupId = this.GetPrimaryKey();
        RaiseEvent(new ChatGroupMessagesAfterDeletedEvent { MessageId = messageId });
        await ConfirmEvents();
        await PublishPartyEvent(new PartyStreamEvent { Type = "deleteMessagesAfter", ChatGroupId = chatGroupId, MessageId = messageId });
    }

    /// <summary>
    /// Publishes a message-level PartyStreamEvent for the specified message ID to the owning party's stream.
    /// </summary>
    /// <param name="messageId">The message identifier associated with the stream event.</param>
    /// <param name="evt">The message-level event payload to include in the stream message.</param>
    /// <returns>A task that completes when the stream publish operation finishes.</returns>
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

    /// <summary>
    /// Retrieves all chat messages with MessageId less than the specified messageId, ordered by MessageId ascending.
    /// </summary>
    /// <param name="messageId">Exclusive upper bound; messages returned have MessageId &lt; this value.</param>
    /// <returns>A new list of chat message copies ordered by MessageId ascending.</returns>
    public Task<List<ChatMessage>> GetMessagesUntilAsync(int messageId)
    {
        var before = State.Messages
            .Where(m => m.MessageId < messageId)
            .OrderBy(m => m.MessageId)
            .ToArray();

        var messages = before.Select(message => message with { }).ToList();

        return Task.FromResult(messages);
    }

    /// <summary>
        /// Retrieves a copy of the chat group's current participants.
        /// </summary>
        /// <returns>A new list containing the current <see cref="PartyParticipant"/> entries; modifying the returned list does not affect the grain's internal state.</returns>
        public Task<List<PartyParticipant>> GetParticipantsAsync()
        => Task.FromResult(new List<PartyParticipant>([.. State.Participants]));

    /// <summary>
    /// Counts consecutive assistant messages at the end of the message log that have non-empty content.
    /// </summary>
    /// <returns>The number of trailing messages whose `SenderType` is `"assistant"` and whose `Content` is not null or empty.</returns>
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
    /// <summary>
    /// Notifies every participant in the chat group about the provided triggering message.
    /// </summary>
    /// <param name="triggeringMessage">The chat message to deliver to each participant's persona grain.</param>
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

    /// <summary>
    /// Publishes a <see cref="PartyStreamEvent"/> to the party event stream for the current state's <see cref="ChatGroupState.PartyId"/>.
    /// </summary>
    /// <param name="evt">The party stream event to publish.</param>
    /// <returns>A <see cref="Task"/> that completes when the stream notification has been sent.</returns>
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
    /// <summary>
    /// Retrieves the identifier of the party that owns this chat group.
    /// </summary>
    /// <returns>The party's <see cref="Guid"/>.</returns>
    [Alias("GetPartyIdAsync")]
    Task<Guid> GetPartyIdAsync();

    /// <summary>
    /// Retrieve the chat group's messages in ascending order by MessageId.
    /// </summary>
    /// <returns>An IReadOnlyList of ChatMessage instances ordered by MessageId (ascending).</returns>
    [Alias("GetMessagesAsync")]
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync();

    /// <summary>
    /// Reserves a new message slot for the specified sender and returns the allocated message id.
    /// </summary>
    /// <param name="senderId">Optional id of the entity reserving the slot; may be null.</param>
    /// <param name="senderType">Optional type label for the sender (defaults to "assistant").</param>
    /// <returns>The allocated message id.</returns>
    [Alias("GetNextMessageIdAsync")]
    Task<int> GetNextMessageIdAsync(Guid? senderId = null, string senderType = "assistant");

    /// <summary>
    /// Replaces the chat group's participants with the provided list.
    /// </summary>
    /// <param name="participants">Participants to set for the chat group.</param>
    [Alias("SetParticipantsAsync")]
    Task SetParticipantsAsync(List<PartyParticipant> participants);

    /// <summary>
        /// Send a new user message to the chat group.
        /// </summary>
        /// <param name="senderId">The persona id of the message sender.</param>
        /// <param name="content">The textual content of the message.</param>
        /// <param name="reasoning">Optional internal reasoning metadata to attach to the message.</param>
        /// <param name="appraisal">Optional appraisal metadata to attach to the message.</param>
        /// <returns>The persisted <see cref="ChatMessage"/> with its assigned MessageId and SendAt timestamp.</returns>
        [Alias("SendNewMessageAsync")]
    Task<ChatMessage> SendNewMessageAsync(
        Guid senderId,
        string content,
        string? reasoning = null,
        string? appraisal = null);

    /// <summary>
        /// Finalizes a previously reserved message slot by writing generated content and optional metadata into the message log.
        /// </summary>
        /// <param name="messageId">The identifier of the reserved message slot to update.</param>
        /// <param name="content">The message content to store.</param>
        /// <param name="reasoning">Optional internal reasoning or chain-of-thought associated with the generation.</param>
        /// <param name="appraisal">Optional appraisal or quality label for the generated content.</param>
        [Alias("AppendMessageAsync")]
    Task AppendMessageAsync(
        int messageId,
        string content,
        string? reasoning = null,
        string? appraisal = null);

    /// <summary>
    /// Marks generation as stopped for the specified message and records an optional appraisal.
    /// </summary>
    /// <param name="messageId">The identifier of the message whose generation was stopped.</param>
    /// <param name="appraisal">An optional appraisal or reason associated with stopping generation.</param>
    [Alias("MarkGenerationStoppedAsync")]
    Task MarkGenerationStoppedAsync(int messageId, string? appraisal);

    /// <summary>
    /// Record a generation failure for the reserved message and notify the message stream.
    /// </summary>
    /// <param name="messageId">The identifier of the message whose generation failed.</param>
    /// <param name="error">A human-readable error message describing the failure.</param>
    [Alias("MarkGenerationFailedAsync")]
    Task MarkGenerationFailedAsync(int messageId, string error);

    /// <summary>
    /// Delete a message from the chat group's message log by its message id.
    /// </summary>
    /// <param name="messageId">The identifier of the message to remove.</param>
    [Alias("DeleteMessageAsync")]
    Task DeleteMessageAsync(int messageId);

    /// <summary>
    /// Deletes all messages in the chat group's message log with IDs greater than the specified messageId.
    /// </summary>
    /// <param name="messageId">The message id; all messages with MessageId greater than this value will be removed.</param>
    [Alias("DeleteMessagesAfterAsync")]
    Task DeleteMessagesAfterAsync(int messageId);

    /// <summary>
    /// Publish a message-level stream event for a specific message to the parent party stream.
    /// </summary>
    /// <param name="messageId">The identifier of the message the stream event pertains to.</param>
    /// <param name="evt">The message stream event payload to deliver.</param>
    /// <returns>A task that completes when the message-level stream event has been published to the party stream.</returns>
    [Alias("NotifyStreamChunkAsync")]
    Task NotifyStreamChunkAsync(int messageId, MessageStreamEvent evt);

    /// <summary>
    /// Retrieve all chat messages whose MessageId is less than the specified messageId, ordered by MessageId ascending.
    /// </summary>
    /// <param name="messageId">Exclusive upper bound; only messages with MessageId &lt; messageId are returned.</param>
    /// <returns>A list of chat messages (copies) with MessageId less than <paramref name="messageId"/>, ordered ascending by MessageId.</returns>
    [Alias("GetMessagesUntilAsync")]
    Task<List<ChatMessage>> GetMessagesUntilAsync(int messageId);

    /// <summary>
    /// Gets the current participants of the chat group.
    /// </summary>
    /// <returns>A new list containing the chat group's participants.</returns>
    [Alias("GetParticipantsAsync")]
    Task<List<PartyParticipant>> GetParticipantsAsync();

    /// <summary>
    /// Count consecutive messages at the end of the chat log whose SenderType is "assistant" and whose Content is not null or empty.
    /// </summary>
    /// <returns>The number of trailing assistant messages with non-empty content.</returns>
    [Alias("CountTrailingAssistantMessagesAsync")]
    Task<int> CountTrailingAssistantMessagesAsync();

    /// <summary>
    /// Notify every participant of the chat group about a newly posted message.
    /// </summary>
    /// <param name="triggeringMessage">The message to deliver to participants as the notification payload.</param>
    /// <returns>A task that completes once notification calls have been initiated for all participants.</returns>
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

    /// <summary>
    /// Applies a ChatGroupInitializedEvent to set the state's PartyId, replace Participants with a copy from the event, and mark the state as initialized.
    /// </summary>
    /// <param name="@event">The initialization event containing the PartyId and the initial participants list.</param>
    public void Apply(ChatGroupInitializedEvent @event)
    {
        PartyId = @event.PartyId;
        Participants = [.. @event.Participants];
        Initialized = true;
    }

    /// <summary>
    /// Replaces the state's participants with a shallow copy of the participants provided by the event.
    /// </summary>
    /// <param name="@event">The event containing the new participants list.</param>
    public void Apply(ChatGroupParticipantsSetEvent @event)
    {
        Participants = [.. @event.Participants];
    }

    /// <summary>
    /// Reserves the next message id and appends a stub chat message to the state's message log.
    /// </summary>
    /// <param name="event">Event carrying the reserver's information (SenderId, SenderType) and the ChatGroupId used to initialize the stub message.</param>
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
            SendAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        Messages.Add(stub);
    }

    /// <summary>
    /// Applies a user message event to the state by inserting the event's message into the message log or updating an existing entry with the same MessageId, and advances the NextMessageId if needed.
    /// </summary>
    /// <param name="event">The user message event containing the ChatMessage to upsert into state.</param>
    public void Apply(ChatGroupUserMessageEvent @event)
    {
        var idx = Messages.FindIndex(m => m.MessageId == @event.Message.MessageId);
        if (idx >= 0)
            Messages[idx] = @event.Message with { };
        else
            Messages.Add(@event.Message with { });
        NextMessageId = Math.Max(NextMessageId, @event.Message.MessageId);
    }

    /// <summary>
    /// Apply a generation-completed event by updating the matching message's content, reasoning, appraisal, sender, and send timestamp.
    /// </summary>
    /// <param name="@event">The event containing MessageId and the generation result fields to apply; no-op if no message with that MessageId exists.</param>
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

    /// <summary>
    /// Applies a generation-stopped event to the state by updating the matching message's appraisal, clearing its sender, and setting its send timestamp.
    /// </summary>
    /// <param name="@event">The event containing the MessageId, Appraisal, and SendAt to apply. If no message matches the MessageId, no state changes occur.</param>
    public void Apply(ChatGroupGenerationStoppedEvent @event)
    {
        var target = Messages.FirstOrDefault(m => m.MessageId == @event.MessageId);
        if (target is null) return;

        target.Appraisal = @event.Appraisal;
        target.SenderId = Guid.Empty;
        target.SendAt = @event.SendAt;
    }

    /// <summary>
    /// Apply a generation-failed event by recording the error and send timestamp on the matching message.
    /// </summary>
    /// <param name="@event">Event carrying the target MessageId, the error text, and the SendAt timestamp to set on the message.</param>
    public void Apply(ChatGroupGenerationFailedEvent @event)
    {
        var target = Messages.FirstOrDefault(m => m.MessageId == @event.MessageId);
        if (target is null) return;

        target.Error = @event.Error;
        target.SendAt = @event.SendAt;
    }

    /// <summary>
        /// Removes all messages whose MessageId equals the event's MessageId from the state's message list.
        /// </summary>
        /// <param name="@event">Event containing the MessageId of messages to delete.</param>
        public void Apply(ChatGroupMessageDeletedEvent @event)
        => Messages.RemoveAll(m => m.MessageId == @event.MessageId);

    /// <summary>
    /// Removes all messages with IDs greater than the event's MessageId and updates NextMessageId to the highest remaining message ID or 0 if none remain.
    /// </summary>
    /// <param name="@event">Event containing the MessageId; messages with MessageId greater than this value will be deleted.</param>
    public void Apply(ChatGroupMessagesAfterDeletedEvent @event)
    {
        Messages.RemoveAll(m => m.MessageId > @event.MessageId);
        NextMessageId = Messages.Count > 0 ? Messages.Max(m => m.MessageId) : 0;
    }
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
public sealed record class ChatGroupMessageSlotReservedEvent : ChatGroupEvent
{
    [Id(0)] public Guid? SenderId { get; set; }
    [Id(1)] public string? SenderType { get; set; }
    [Id(2)] public Guid ChatGroupId { get; set; }
}

/// <summary>Raised when all messages after a given ID are deleted (used by reprompt to trim and regenerate).</summary>
[GenerateSerializer, Alias(nameof(ChatGroupMessagesAfterDeletedEvent))]
public sealed record class ChatGroupMessagesAfterDeletedEvent : ChatGroupEvent
{
    [Id(0)] public int MessageId { get; set; }
}
