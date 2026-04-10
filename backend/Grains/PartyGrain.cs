using Orleans.Concurrency;
using Orleans.EventSourcing;
using Orleans.Providers;
using PartyTown.Logging;
using PartyTown.Model;

namespace PartyTown.Grains;

// Party = root-of-universe. Owns chat group registry and participants.
// Messages and generation are owned by ChatGroupGrain and PersonaGrain respectively.
// The Guid.Empty party is the default universe, auto-initialized on first use.
[LogConsistencyProvider(ProviderName = "PartyStateStorage")]
[StorageProvider(ProviderName = "parties")]
public sealed class PartyGrain(ILogger<PartyGrain> logger)
    : JournaledGrain<PartyState, PartyEvent>, IPartyGrain
{
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

        // Propagate participants to all chat group grains
        var tasks = State.ChatGroups.Select(cg =>
            GrainFactory.GetGrain<IChatGroupGrain>(cg.Id)
                .SetParticipantsAsync(participants));
        await Task.WhenAll(tasks);
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

        // Register mapping so ChatGroupGrain can self-initialize on activation
        var registry = GrainFactory.GetGrain<IPartyRootGrain>(Guid.Empty);
        await registry.RegisterChatGroup(chatGroup.Id, this.GetPrimaryKey());

        return chatGroup;
    }

    public async Task<List<ChatMessage>> DownloadMessages()
    {
        var tasks = State.ChatGroups.Select(async cg =>
        {
            var grain = GrainFactory.GetGrain<IChatGroupGrain>(cg.Id);
            return await grain.GetMessagesAsync();
        });

        var allMessages = await Task.WhenAll(tasks);
        return allMessages
            .SelectMany(msgs => msgs)
            .OrderBy(m => m.MessageId)
            .Take(100)
            .ToList();
    }

    public Task CancelAllGenerations()
    {
        // Delegate cancellation to all chat group grains
        // TODO: Add cancellation support to ChatGroupGrain in a future iteration
        return Task.CompletedTask;
    }

    public async Task DeleteParty()
    {
        RaiseEvent(new PartyDeletedEvent());
        await ConfirmEvents();
    }
}

[Alias("backend.IPartyGrain")]
public interface IPartyGrain : IGrainWithGuidKey
{
    [AlwaysInterleave]
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

    public void Apply(PartyDeletedEvent _)
    {
        Id = Guid.Empty;
        Name = string.Empty;
        Participants = [];
        ChatGroups = [];
    }
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

[GenerateSerializer, Alias(nameof(ChatGroupCreatedEvent))]
public sealed record class ChatGroupCreatedEvent : PartyEvent
{
    [Id(0)]
    public ChatGroupInfo ChatGroup { get; set; } = new();
}

[GenerateSerializer, Alias(nameof(PartyDeletedEvent))]
public sealed record class PartyDeletedEvent : PartyEvent;
