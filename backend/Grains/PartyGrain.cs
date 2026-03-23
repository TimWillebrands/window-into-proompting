using System.Diagnostics;
using System.Text.Json;
using Orleans.EventSourcing;
using Orleans.Providers;
using Orleans.Streams;
using PartyTown.Grains.Generation;
using PartyTown.Logging;
using PartyTown.Model;
using PartyTown.Services.Generation;
using PartyTown.Services.Streaming;

namespace PartyTown.Grains;

// Party = root-of-universe. Owns chat groups (conversation threads), participants, and all messages.
// The Guid.Empty party is the default universe, auto-initialized on first use.
[LogConsistencyProvider(ProviderName = "PartyStateStorage")]
[StorageProvider(ProviderName = "parties")]
public sealed class PartyGrain(
    ILogger<PartyGrain> logger,
    ILoggerFactory loggerFactory)
    : JournaledGrain<PartyState, PartyEvent>, IPartyGrain
{
    private readonly Dictionary<(Guid ChatGroupId, int MessageId), CancellationTokenSource> activeGenerations = [];

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Grain activated");
        return base.OnActivateAsync(cancellationToken);
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        logger.LogInformation("Grain deactivating: {Reason}", reason.ReasonCode);
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    public Task<PartyInfo> GetParty()
        => Task.FromResult(new PartyInfo
        {
            Id = this.GetPrimaryKey(),
            Name = State.Name,
            Participants = [.. State.Participants]
        });

    public async Task SetParty(PartyInfo party)
    {
        RaiseEvent(new PartySetEvent
        {
            PartyId = this.GetPrimaryKey(),
            Name = party.Name
        });

        await ConfirmEvents();
    }

    public async Task SetParticipants(List<PartyParticipant> participants)
    {
        RaiseEvent(new ParticipantsSetEvent
        {
            Participants = [.. participants]
        });

        await ConfirmEvents();
    }

    public Task<List<ChatGroupInfo>> GetChatGroups()
        => Task.FromResult<List<ChatGroupInfo>>([.. State.ChatGroups]);

    public async Task<ChatGroupInfo> CreateChatGroup(string name)
    {
        var chatGroup = new ChatGroupInfo
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        RaiseEvent(new ChatGroupCreatedEvent { ChatGroup = chatGroup });
        await ConfirmEvents();
        return chatGroup;
    }

    public Task<List<ChatMessage>> DownloadMessages()
        => Task.FromResult(State.Messages.OrderBy(m => m.MessageId).Take(100).ToList());

    public async Task<int[]> SendPrompt(Guid chatGroupId, string prompt, Guid senderId, string senderName, string model, string provider, Guid? personaId)
    {
        var nextMessageId = State.GetNextMessageId(chatGroupId);
        var userMessage = new ChatMessage
        {
            ChatGroupId = chatGroupId,
            MessageId = nextMessageId + 1,
            Content = prompt,
            SenderType = "user",
            SenderId = senderId,
            SenderName = senderName,
            SendAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var assistantMessage = new ChatMessage
        {
            ChatGroupId = chatGroupId,
            MessageId = nextMessageId + 2,
            SenderType = "assistant",
            SenderId = personaId ?? Guid.Empty,
            SenderName = null,
            Content = null,
            SendAt = null,
            ModelEndpointStub = model
        };

        RaiseEvent(new PromptQueuedEvent
        {
            SenderId = senderId,
            SenderName = senderName,
            UserMessage = userMessage,
            AssistantMessage = assistantMessage
        });

        await ConfirmEvents();

        await PublishPartyEvent(new PartyStreamEvent { Type = "message", ChatGroupId = chatGroupId, Message = userMessage });
        await PublishPartyEvent(new PartyStreamEvent { Type = "messageStream", ChatGroupId = chatGroupId, MessageId = assistantMessage.MessageId });

        _ = StartGenerationAsync(chatGroupId, assistantMessage.MessageId, model, provider, personaId, senderId);

        return [userMessage.MessageId, assistantMessage.MessageId];
    }

    public async Task<int> Proceed(Guid chatGroupId, Guid senderId, string senderName, Guid? personaId, string model, string provider)
    {
        var nextMessageId = State.GetNextMessageId(chatGroupId);
        var assistantMessage = new ChatMessage
        {
            ChatGroupId = chatGroupId,
            MessageId = nextMessageId + 1,
            SenderType = "assistant",
            SenderId = personaId ?? Guid.Empty,
            SenderName = null,
            Content = null,
            SendAt = null,
            ModelEndpointStub = model
        };

        RaiseEvent(new AssistantQueuedEvent
        {
            SenderId = senderId,
            SenderName = senderName,
            AssistantMessage = assistantMessage
        });

        await ConfirmEvents();

        await PublishPartyEvent(new PartyStreamEvent { Type = "messageStream", ChatGroupId = chatGroupId, MessageId = assistantMessage.MessageId });
        _ = StartGenerationAsync(chatGroupId, assistantMessage.MessageId, model, provider, personaId, senderId);
        return assistantMessage.MessageId;
    }

    public async Task DeleteMessage(Guid chatGroupId, int messageId)
    {
        RaiseEvent(new MessageDeletedEvent { ChatGroupId = chatGroupId, MessageId = messageId });
        await ConfirmEvents();
        await PublishPartyEvent(new PartyStreamEvent { Type = "deleteMessage", ChatGroupId = chatGroupId, MessageId = messageId });
    }

    public async Task DeleteMessagesAfter(Guid chatGroupId, int messageId)
    {
        RaiseEvent(new MessagesAfterDeletedEvent { ChatGroupId = chatGroupId, MessageId = messageId });
        await ConfirmEvents();
        await PublishPartyEvent(new PartyStreamEvent { Type = "deleteMessagesAfter", ChatGroupId = chatGroupId, MessageId = messageId });
    }

    public Task CancelAllGenerations()
    {
        foreach (var cts in activeGenerations.Values)
        {
            cts.Cancel();
        }

        activeGenerations.Clear();
        return Task.CompletedTask;
    }

    public async Task DeleteParty()
    {
        foreach (var cts in activeGenerations.Values)
        {
            cts.Cancel();
        }

        activeGenerations.Clear();
        RaiseEvent(new PartyDeletedEvent());
        await ConfirmEvents();
    }

    private async Task StartGenerationAsync(Guid chatGroupId, int messageId, string model, string provider, Guid? personaId, Guid senderId)
    {
        using (logger.BeginMessageScope(chatGroupId, messageId))
        using (logger.BeginGenerationScope(model, provider, personaId))
        {
            logger.LogInformation("Starting generation for message {MessageId}", messageId);
            var sw = Stopwatch.StartNew();

            var cts = new CancellationTokenSource();
            var generationKey = (chatGroupId, messageId);
            activeGenerations[generationKey] = cts;

            var router = GrainFactory.GetGrain<ILlmRouterGrain>(0);
            var allModels = await router.GetModelsAsync(cts.Token);
            var modelInfo = allModels.FirstOrDefault(m => m.Name == model && m.ProviderType.Equals(provider, StringComparison.OrdinalIgnoreCase))
                ?? allModels.FirstOrDefault(m => m.Name == model)
                ?? throw new InvalidOperationException($"Model '{model}' not found on any provider");

            var endpointGrain = LlmEndpointGrainFactory.GetGrain(GrainFactory, modelInfo);
            var session = new GenerationSession(endpointGrain, loggerFactory.CreateLogger<GenerationSession>());

            try
            {
                var history = await GetMessagesUntil(chatGroupId, messageId);
                var participants = await GetParticipants();

                var result = await session.GenerateAsync(
                    history,
                    participants,
                    model,
                    this.GetPrimaryKey(),
                    personaId,
                    senderId,
                    onEvent: async (eventType, data, done) =>
                    {
                        await PublishMessageEvent(messageId, new MessageStreamEvent
                        {
                            ChatGroupId = chatGroupId,
                            Event = eventType,
                            Data = data,
                            Done = done
                        });
                    },
                    cts.Token);

                sw.Stop();

                if (result.Stop)
                {
                    logger.LogWarning("Generation stopped for message {MessageId} (overseer decision)", messageId);

                    RaiseEventWithLogging(new AssistantGenerationStoppedEvent
                    {
                        ChatGroupId = chatGroupId,
                        MessageId = messageId,
                        Overseer = result.Overseer is null ? null : JsonSerializer.Serialize(result.Overseer, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                        SendAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    });

                    await ConfirmEvents();

                    // Publish the updated message so frontend gets the final state including overseer data
                    var stoppedMessage = State.Messages.FirstOrDefault(m => m.ChatGroupId == chatGroupId && m.MessageId == messageId);
                    if (stoppedMessage is not null)
                    {
                        await PublishPartyEvent(new PartyStreamEvent { Type = "message", ChatGroupId = chatGroupId, Message = stoppedMessage });
                    }

                    return;
                }

                logger.LogInformation("Generation completed for message {MessageId} in {ElapsedMs}ms", messageId, sw.ElapsedMilliseconds);

                RaiseEventWithLogging(new AssistantGenerationCompletedEvent
                {
                    ChatGroupId = chatGroupId,
                    MessageId = messageId,
                    Message = result.Message,
                    Reasoning = result.Reasoning,
                    Overseer = result.Overseer is null ? null : JsonSerializer.Serialize(result.Overseer, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    SenderId = result.Persona?.Id ?? Guid.Empty,
                    SendAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });

                await ConfirmEvents();

                // Publish the updated message so frontend gets the final state including overseer data
                var updatedMessage = State.Messages.FirstOrDefault(m => m.ChatGroupId == chatGroupId && m.MessageId == messageId);
                if (updatedMessage is not null)
                {
                    await PublishPartyEvent(new PartyStreamEvent { Type = "message", ChatGroupId = chatGroupId, Message = updatedMessage });
                }

                if (result.Persona is not null)
                {
                    logger.LogInformation("Persona {PersonaName} will continue the conversation", result.Persona.Name);
                    _ = Proceed(chatGroupId, result.Persona.Id, result.Persona.Name, null, model, provider);
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogDebug("Generation cancelled for message {MessageId}", messageId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Generation failed for message {MessageId}", messageId);

                RaiseEventWithLogging(new AssistantGenerationFailedEvent
                {
                    ChatGroupId = chatGroupId,
                    MessageId = messageId,
                    Error = ex.Message,
                    SendAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });

                await ConfirmEvents();

                await PublishMessageEvent(messageId, new MessageStreamEvent
                {
                    ChatGroupId = chatGroupId,
                    Event = "error",
                    Data = ex.Message,
                    Done = true
                });
            }
            finally
            {
                if (activeGenerations.Remove(generationKey, out var generationCts))
                {
                    generationCts.Dispose();
                }
            }
        }
    }

    private void RaiseEventWithLogging<T>(T @event) where T : PartyEvent
    {
        logger.LogDebug("Raising event: {EventType}", typeof(T).Name);
        RaiseEvent(@event);
    }

    private Task<List<MessageWithSender>> GetMessagesUntil(Guid chatGroupId, int messageId)
    {
        var before = State.Messages
            .Where(m => m.ChatGroupId == chatGroupId && m.MessageId < messageId)
            .OrderBy(m => m.MessageId)
            .ToArray();

        var participantsById = State.Participants.ToDictionary(x => x.Id, x => x.Name);

        var messages = before.Select(message => new MessageWithSender
        {
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

    private async Task<List<GenerationParticipant>> GetParticipants()
    {
        var personaRoot = GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty);
        var personas = await personaRoot.GetAll();
        var personaMap = personas.ToDictionary(p => p.Id, p => p);

        var result = new List<GenerationParticipant>(State.Participants.Count);
        foreach (var participant in State.Participants)
        {
            if (participant.IsUser)
            {
                result.Add(new GenerationParticipant
                {
                    Id = participant.Id,
                    Name = participant.Name,
                    IsUser = true,
                    SystemPrompt = "this is a real human user and has no systemprompt",
                    Bio = null
                });

                continue;
            }

            if (!personaMap.TryGetValue(participant.Id, out var persona))
            {
                continue;
            }

            result.Add(new GenerationParticipant
            {
                Id = persona.Id,
                Name = persona.Name,
                Bio = persona.Bio,
                SystemPrompt = persona.SystemPrompt,
                IsUser = false
            });
        }

        return result;
    }

    private Task PublishPartyEvent(PartyStreamEvent evt)
    {
        logger.LogDebug("Publishing stream event: {EventType}", evt.Type);

        var streamProvider = this.GetStreamProvider(PartyStreamIds.Provider);
        var stream = streamProvider.GetStream<PartyStreamEvent>(PartyStreamIds.PartyEventsNamespace, PartyStreamIds.PartyEventId(this.GetPrimaryKey()));
        return stream.OnNextAsync(evt);
    }

    private Task PublishMessageEvent(int messageId, MessageStreamEvent evt)
    {
        logger.LogDebug("Publishing message stream event: {EventType} dataLen={DataLen} for message {MessageId}", evt.Event, evt.Data?.Length ?? 0, messageId);

        var streamProvider = this.GetStreamProvider(PartyStreamIds.Provider);
        var stream = streamProvider.GetStream<PartyStreamEvent>(
            PartyStreamIds.PartyEventsNamespace,
            PartyStreamIds.PartyEventId(this.GetPrimaryKey()));
        return stream.OnNextAsync(new PartyStreamEvent
        {
            Type = "messageEvent",
            ChatGroupId = evt.ChatGroupId,
            MessageId = messageId,
            MessageEvent = evt
        });
    }
}

[Alias("backend.IPartyGrain")]
public interface IPartyGrain : IGrainWithGuidKey
{
    [Alias("GetParty")]
    Task<PartyInfo> GetParty();

    [Alias("SetParty")]
    Task SetParty(PartyInfo party);

    [Alias("SetParticipants")]
    Task SetParticipants(List<PartyParticipant> participants);

    [Alias("GetChatGroups")]
    Task<List<ChatGroupInfo>> GetChatGroups();

    [Alias("CreateChatGroup")]
    Task<ChatGroupInfo> CreateChatGroup(string name);

    [Alias("DownloadMessages")]
    Task<List<ChatMessage>> DownloadMessages();

    [Alias("SendPrompt")]
    Task<int[]> SendPrompt(Guid chatGroupId, string prompt, Guid senderId, string senderName, string model, string provider, Guid? personaId);

    [Alias("Proceed")]
    Task<int> Proceed(Guid chatGroupId, Guid senderId, string senderName, Guid? personaId, string model, string provider);

    [Alias("DeleteMessage")]
    Task DeleteMessage(Guid chatGroupId, int messageId);

    [Alias("DeleteMessagesAfter")]
    Task DeleteMessagesAfter(Guid chatGroupId, int messageId);

    [Alias("CancelAllGenerations")]
    Task CancelAllGenerations();

    [Alias("DeleteParty")]
    Task DeleteParty();
}

[GenerateSerializer, Alias(nameof(PartyState))]
public sealed record class PartyState
{
    [Id(0)]
    public Guid Id { get; set; } = Guid.Empty;

    [Id(1)]
    public string Name { get; set; } = string.Empty;

    [Id(2)]
    public List<PartyParticipant> Participants { get; set; } = [];

    [Id(3)]
    public List<ChatMessage> Messages { get; set; } = [];

    [Id(4)]
    public Dictionary<Guid, int> NextMessageIdByChatGroup { get; set; } = [];

    [Id(5)]
    public Dictionary<Guid, string> UserNames { get; set; } = [];

    [Id(6)]
    public List<ChatGroupInfo> ChatGroups { get; set; } = [];

    public void Apply(ChatGroupCreatedEvent @event)
    {
        ChatGroups.Add(@event.ChatGroup);
    }

    public void Apply(PartySetEvent @event)
    {
        Id = @event.PartyId;
        Name = @event.Name;
    }

    public void Apply(ParticipantsSetEvent @event)
    {
        Participants = [.. @event.Participants];
    }

    public void Apply(PromptQueuedEvent @event)
    {
        UserNames[@event.SenderId] = @event.SenderName;
        Messages.Add(CloneMessage(@event.UserMessage));
        Messages.Add(CloneMessage(@event.AssistantMessage));
        SetNextMessageId(
            @event.UserMessage.ChatGroupId,
            Math.Max(@event.UserMessage.MessageId, @event.AssistantMessage.MessageId));
    }

    public void Apply(AssistantQueuedEvent @event)
    {
        UserNames[@event.SenderId] = @event.SenderName;
        Messages.Add(CloneMessage(@event.AssistantMessage));
        SetNextMessageId(@event.AssistantMessage.ChatGroupId, @event.AssistantMessage.MessageId);
    }

    public void Apply(MessageDeletedEvent @event)
        => Messages.RemoveAll(m => m.ChatGroupId == @event.ChatGroupId && m.MessageId == @event.MessageId);

    public void Apply(MessagesAfterDeletedEvent @event)
        => Messages.RemoveAll(m => m.ChatGroupId == @event.ChatGroupId && m.MessageId > @event.MessageId);

    public void Apply(AssistantGenerationStoppedEvent @event)
    {
        var target = Messages.FirstOrDefault(m => m.ChatGroupId == @event.ChatGroupId && m.MessageId == @event.MessageId);
        if (target is null)
        {
            return;
        }

        target.Content = null;
        target.Reasoning = null;
        target.Overseer = @event.Overseer;
        target.Error = null;
        target.SenderId = Guid.Empty;
        target.SendAt = @event.SendAt;
    }

    public void Apply(AssistantGenerationCompletedEvent @event)
    {
        var target = Messages.FirstOrDefault(m => m.ChatGroupId == @event.ChatGroupId && m.MessageId == @event.MessageId);
        if (target is null)
        {
            return;
        }

        target.Content = @event.Message;
        target.Reasoning = @event.Reasoning;
        target.Overseer = @event.Overseer;
        target.Error = null;
        target.SenderId = @event.SenderId;
        target.SenderName = Participants.FirstOrDefault(p => p.Id == @event.SenderId)?.Name;
        target.SendAt = @event.SendAt;
    }

    public void Apply(AssistantGenerationFailedEvent @event)
    {
        var target = Messages.FirstOrDefault(m => m.ChatGroupId == @event.ChatGroupId && m.MessageId == @event.MessageId);
        if (target is null)
        {
            return;
        }

        target.Error = @event.Error;
        target.SendAt = @event.SendAt;
    }

    public void Apply(PartyDeletedEvent _)
    {
        Id = Guid.Empty;
        Name = string.Empty;
        Participants = [];
        Messages = [];
        NextMessageIdByChatGroup = [];
        UserNames = [];
        ChatGroups = [];
    }

    public int GetNextMessageId(Guid chatGroupId)
        => NextMessageIdByChatGroup.TryGetValue(chatGroupId, out var nextMessageId)
            ? nextMessageId
            : 0;

    private void SetNextMessageId(Guid chatGroupId, int candidate)
    {
        var current = GetNextMessageId(chatGroupId);
        NextMessageIdByChatGroup[chatGroupId] = Math.Max(current, candidate);
    }

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

[GenerateSerializer, Alias(nameof(PartyEvent))]
public abstract record class PartyEvent;

[GenerateSerializer, Alias(nameof(PartySetEvent))]
public sealed record class PartySetEvent : PartyEvent
{
    [Id(0)]
    public Guid PartyId { get; set; }

    [Id(1)]
    public string Name { get; set; } = string.Empty;
}

[GenerateSerializer, Alias(nameof(ParticipantsSetEvent))]
public sealed record class ParticipantsSetEvent : PartyEvent
{
    [Id(0)]
    public List<PartyParticipant> Participants { get; set; } = [];
}

[GenerateSerializer, Alias(nameof(PromptQueuedEvent))]
public sealed record class PromptQueuedEvent : PartyEvent
{
    [Id(0)]
    public Guid SenderId { get; set; }

    [Id(1)]
    public string SenderName { get; set; } = string.Empty;

    [Id(2)]
    public ChatMessage UserMessage { get; set; } = new();

    [Id(3)]
    public ChatMessage AssistantMessage { get; set; } = new();
}

[GenerateSerializer, Alias(nameof(AssistantQueuedEvent))]
public sealed record class AssistantQueuedEvent : PartyEvent
{
    [Id(0)]
    public Guid SenderId { get; set; }

    [Id(1)]
    public string SenderName { get; set; } = string.Empty;

    [Id(2)]
    public ChatMessage AssistantMessage { get; set; } = new();
}

[GenerateSerializer, Alias(nameof(MessageDeletedEvent))]
public sealed record class MessageDeletedEvent : PartyEvent
{
    [Id(0)]
    public int MessageId { get; set; }

    [Id(1)]
    public Guid ChatGroupId { get; set; }
}

[GenerateSerializer, Alias(nameof(MessagesAfterDeletedEvent))]
public sealed record class MessagesAfterDeletedEvent : PartyEvent
{
    [Id(0)]
    public int MessageId { get; set; }

    [Id(1)]
    public Guid ChatGroupId { get; set; }
}

[GenerateSerializer, Alias(nameof(AssistantGenerationStoppedEvent))]
public sealed record class AssistantGenerationStoppedEvent : PartyEvent
{
    [Id(0)]
    public int MessageId { get; set; }

    [Id(1)]
    public string? Overseer { get; set; }

    [Id(2)]
    public long SendAt { get; set; }

    [Id(3)]
    public Guid ChatGroupId { get; set; }
}

[GenerateSerializer, Alias(nameof(AssistantGenerationCompletedEvent))]
public sealed record class AssistantGenerationCompletedEvent : PartyEvent
{
    [Id(0)]
    public int MessageId { get; set; }

    [Id(1)]
    public string? Message { get; set; }

    [Id(2)]
    public string? Reasoning { get; set; }

    [Id(3)]
    public string? Overseer { get; set; }

    [Id(4)]
    public Guid SenderId { get; set; }

    [Id(5)]
    public long SendAt { get; set; }

    [Id(6)]
    public Guid ChatGroupId { get; set; }
}

[GenerateSerializer, Alias(nameof(AssistantGenerationFailedEvent))]
public sealed record class AssistantGenerationFailedEvent : PartyEvent
{
    [Id(0)]
    public int MessageId { get; set; }

    [Id(1)]
    public string Error { get; set; } = string.Empty;

    [Id(2)]
    public long SendAt { get; set; }

    [Id(3)]
    public Guid ChatGroupId { get; set; }
}

[GenerateSerializer, Alias(nameof(ChatGroupCreatedEvent))]
public sealed record class ChatGroupCreatedEvent : PartyEvent
{
    [Id(0)]
    public ChatGroupInfo ChatGroup { get; set; } = new();
}

[GenerateSerializer, Alias(nameof(PartyDeletedEvent))]
public sealed record class PartyDeletedEvent : PartyEvent;
