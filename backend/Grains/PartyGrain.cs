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
            Participants = State.Participants.ToList()
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
        // Party-level participants are the seed cast for newly created chat groups
        // (see ChatGroupGrain.OnActivateAsync). Existing chat groups own their own
        // participants list per-room and are not mutated here.
        RaiseEvent(new ParticipantsSetEvent
        {
            Participants = [.. participants]
        });

        await ConfirmEvents();
    }

    public Task<List<ChatGroupInfo>> GetChatGroups()
        => Task.FromResult(State.ChatGroups.ToList());

    public async Task<ChatGroupInfo> CreateChatGroup(string name, string? scenario = null)
    {
        var normalizedScenario = string.IsNullOrWhiteSpace(scenario) ? null : scenario.Trim();
        if (normalizedScenario is { Length: > ChatGroupLimits.MaxScenarioLength })
        {
            logger.LogWarning(
                "Scenario length {Length} exceeded cap {Cap} on CreateChatGroup; truncating",
                normalizedScenario.Length, ChatGroupLimits.MaxScenarioLength);
            normalizedScenario = normalizedScenario[..ChatGroupLimits.MaxScenarioLength];
        }

        var chatGroup = new ChatGroupInfo
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Scenario = normalizedScenario
        };

        RaiseEvent(new ChatGroupCreatedEvent { ChatGroup = chatGroup });
        await ConfirmEvents();

        // Register mapping so ChatGroupGrain can self-initialize on activation
        var registry = GrainFactory.GetGrain<IPartyRootGrain>(Guid.Empty);
        await registry.RegisterChatGroup(chatGroup.Id, this.GetPrimaryKey());

        return chatGroup;
    }

    public async Task UpdateChatGroupScenario(Guid chatGroupId, string? scenario)
    {
        // Guard membership before raising the event — otherwise an unknown chatGroupId
        // would persist a no-op event and activate an orphan ChatGroupGrain.
        if (!State.ChatGroups.Any(g => g.Id == chatGroupId))
            return;

        var normalized = string.IsNullOrWhiteSpace(scenario) ? null : scenario.Trim();
        if (normalized is { Length: > ChatGroupLimits.MaxScenarioLength })
        {
            logger.LogWarning(
                "Scenario length {Length} exceeded cap {Cap} on UpdateChatGroupScenario for chat group {ChatGroupId}; truncating",
                normalized.Length, ChatGroupLimits.MaxScenarioLength, chatGroupId);
            normalized = normalized[..ChatGroupLimits.MaxScenarioLength];
        }

        RaiseEvent(new ChatGroupScenarioUpdatedEvent
        {
            ChatGroupId = chatGroupId,
            Scenario = normalized
        });
        await ConfirmEvents();

        await GrainFactory.GetGrain<IChatGroupGrain>(chatGroupId).SetScenarioAsync(normalized);
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

    public async Task CancelAllGenerations(CancellationToken ct = default)
    {
        var tasks = State.Participants
            .Where(p => !p.IsUser)
            .Select(p => GrainFactory.GetGrain<IPersonaGrain>(p.Id).CancelGenerationAsync());
        await Task.WhenAll(tasks);
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

    [AlwaysInterleave]
    [Alias("GetChatGroups")]
    Task<List<ChatGroupInfo>> GetChatGroups();

    [Alias("CreateChatGroup")]
    Task<ChatGroupInfo> CreateChatGroup(string name, string? scenario = null);

    [Alias("UpdateChatGroupScenario")]
    Task UpdateChatGroupScenario(Guid chatGroupId, string? scenario);

    [Alias("DownloadMessages")]
    Task<List<ChatMessage>> DownloadMessages();

    [Alias("CancelAllGenerations")]
    Task CancelAllGenerations(CancellationToken cancellationToken = default);

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

    public void Apply(ChatGroupScenarioUpdatedEvent @event)
    {
        var idx = ChatGroups.FindIndex(g => g.Id == @event.ChatGroupId);
        if (idx >= 0)
            ChatGroups[idx] = ChatGroups[idx] with { Scenario = @event.Scenario };
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

[GenerateSerializer, Alias(nameof(ChatGroupScenarioUpdatedEvent))]
public sealed record class ChatGroupScenarioUpdatedEvent : PartyEvent
{
    [Id(0)]
    public Guid ChatGroupId { get; set; }

    [Id(1)]
    public string? Scenario { get; set; }
}

[GenerateSerializer, Alias(nameof(PartyDeletedEvent))]
public sealed record class PartyDeletedEvent : PartyEvent;
