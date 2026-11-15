using Orleans.Concurrency;
using Orleans.EventSourcing;
using Orleans.Providers;
using PartyTown.Logging;
using PartyTown.Model;

namespace PartyTown.Grains;

[LogConsistencyProvider(ProviderName = "PartyStateStorage")]
[StorageProvider(ProviderName = "parties")]
public sealed class PartyGrain(
    ILogger<PartyGrain> logger)
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
        var normalized = WithNarratorEnforced(participants);

        if (ParticipantsEqual(State.Participants, normalized))
            return;

        RaiseEvent(new ParticipantsSetEvent
        {
            Participants = [.. normalized]
        });

        await ConfirmEvents();
    }

    public async Task<List<PartyParticipant>> AddParticipantsAsync(List<PartyParticipant> participants)
    {
        var known = State.Participants.Select(p => p.Id).ToHashSet();
        var added = participants.Where(p => p.Id != Guid.Empty && known.Add(p.Id)).ToList();
        if (added.Count > 0)
            await SetParticipants(State.Participants.Concat(added).ToList());
        return added;
    }

    public async Task UpsertParticipantsAsync(List<PartyParticipant> participants)
    {
        var byId = State.Participants.ToDictionary(p => p.Id);
        foreach (var p in participants.Where(p => p.Id != Guid.Empty))
            byId[p.Id] = p;
        await SetParticipants(byId.Values.ToList());
    }

    public async Task<PartyParticipant?> RemoveParticipantAsync(Guid personaId)
    {
        var leaving = State.Participants.FirstOrDefault(p => p.Id == personaId && p.Id != Narrator.PersonaId);
        if (leaving is not null)
            await SetParticipants(State.Participants.Where(p => p.Id != personaId).ToList());
        return leaving;
    }

    private static bool ParticipantsEqual(List<PartyParticipant> a, List<PartyParticipant> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].Id != b[i].Id || a[i].Name != b[i].Name || a[i].Driver != b[i].Driver)
                return false;
        }
        return true;
    }

    private static List<PartyParticipant> WithNarratorEnforced(List<PartyParticipant> participants)
    {
        var result = new List<PartyParticipant>(participants.Count + 1);
        var narratorSeen = false;
        foreach (var p in participants)
        {
            if (p.Id == Narrator.PersonaId)
            {
                if (narratorSeen) continue;
                narratorSeen = true;
                result.Add(new PartyParticipant
                {
                    Id = Narrator.PersonaId,
                    Name = Narrator.DisplayName,
                    Driver = DriverKind.System,
                });
            }
            else
            {
                result.Add(p);
            }
        }
        if (!narratorSeen)
        {
            result.Add(new PartyParticipant
            {
                Id = Narrator.PersonaId,
                Name = Narrator.DisplayName,
                Driver = DriverKind.System,
            });
        }
        return result;
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

        var registry = GrainFactory.GetGrain<IPartyRootGrain>(Guid.Empty);
        await registry.RegisterChatGroup(chatGroup.Id, this.GetPrimaryKey());

        return chatGroup;
    }

    public async Task<ChatGroupInfo> GetOrCreateDirectChatAsync(Guid a, Guid b)
    {
        var existing = State.ChatGroups.FirstOrDefault(g =>
            g.Kind == ChatGroupKinds.Direct && g.MemberIds is { Count: 2 } m && m.Contains(a) && m.Contains(b));
        if (existing is not null)
            return existing;

        var names = State.Participants.Where(p => p.Id == a || p.Id == b).Select(p => p.Name).ToList();
        var chatGroup = new ChatGroupInfo
        {
            Id = Guid.NewGuid(),
            Name = string.Join(" & ", names),
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Kind = ChatGroupKinds.Direct,
            MemberIds = new List<Guid> { a, b },
        };

        RaiseEvent(new ChatGroupCreatedEvent { ChatGroup = chatGroup });
        await ConfirmEvents();

        var registry = GrainFactory.GetGrain<IPartyRootGrain>(Guid.Empty);
        await registry.RegisterChatGroup(chatGroup.Id, this.GetPrimaryKey());
        await GrainFactory.GetGrain<IChatGroupGrain>(chatGroup.Id)
            .SetParticipantIdsAsync(new HashSet<Guid> { a, b });

        return chatGroup;
    }

    public async Task<ChatGroupInfo> CreatePlaceRoomAsync(string name)
    {
        var chatGroup = new ChatGroupInfo
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Kind = ChatGroupKinds.Place,
        };

        RaiseEvent(new ChatGroupCreatedEvent { ChatGroup = chatGroup });
        await ConfirmEvents();

        var registry = GrainFactory.GetGrain<IPartyRootGrain>(Guid.Empty);
        await registry.RegisterChatGroup(chatGroup.Id, this.GetPrimaryKey());

        return chatGroup;
    }

    public async Task RemoveChatGroup(Guid chatGroupId)
    {
        if (!State.ChatGroups.Any(g => g.Id == chatGroupId))
            return;

        RaiseEvent(new ChatGroupRemovedEvent { ChatGroupId = chatGroupId });
        await ConfirmEvents();
    }

    public async Task UpdateChatGroupScenario(Guid chatGroupId, string? scenario)
    {
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
        var rooms = await Task.WhenAll(State.ChatGroups.Select(cg => GrainFactory.GetGrain<IChatGroupGrain>(cg.Id).GetMessagesAsync()));
        return NewestPerRoom(rooms, SnapshotPerRoom);
    }

    public const int SnapshotPerRoom = 200;

    public static List<ChatMessage> NewestPerRoom(IEnumerable<IReadOnlyList<ChatMessage>> rooms, int perRoom)
        => rooms.SelectMany(msgs => msgs.OrderBy(m => m.MessageId).TakeLast(perRoom)).ToList();

    public async Task CancelAllGenerations(CancellationToken ct = default)
    {
        var tasks = SelectCancelTargets(State.Participants)
            .Select(p => GrainFactory.GetGrain<IPersonaGrain>(p.Id).CancelGenerationAsync());
        await Task.WhenAll(tasks);
    }

    public static IEnumerable<PartyParticipant> SelectCancelTargets(IEnumerable<PartyParticipant> participants)
        => participants.Where(p => p.Driver != DriverKind.System);

    public async Task<IReadOnlyList<CastMember>> GetCastAsync()
    {
        var library = await Task.WhenAll(State.Participants
            .Where(p => p.Driver != DriverKind.System)
            .Select(p => GrainFactory.GetGrain<IPersonaGrain>(p.Id).GetPersona()));
        var byId = library.ToDictionary(p => p.Id, p => p);

        return State.Participants
            .Select(link => CastMember.Create(link, byId.GetValueOrDefault(link.Id)))
            .ToList();
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

    [Alias("AddParticipantsAsync")]
    Task<List<PartyParticipant>> AddParticipantsAsync(List<PartyParticipant> participants);

    [Alias("UpsertParticipantsAsync")]
    Task UpsertParticipantsAsync(List<PartyParticipant> participants);

    [Alias("RemoveParticipantAsync")]
    Task<PartyParticipant?> RemoveParticipantAsync(Guid personaId);

    [AlwaysInterleave]
    [Alias("GetChatGroups")]
    Task<List<ChatGroupInfo>> GetChatGroups();

    [Alias("CreateChatGroup")]
    Task<ChatGroupInfo> CreateChatGroup(string name, string? scenario = null);

    [Alias("GetOrCreateDirectChatAsync")]
    Task<ChatGroupInfo> GetOrCreateDirectChatAsync(Guid a, Guid b);

    [Alias("CreatePlaceRoomAsync")]
    Task<ChatGroupInfo> CreatePlaceRoomAsync(string name);

    [Alias("RemoveChatGroup")]
    Task RemoveChatGroup(Guid chatGroupId);

    [Alias("UpdateChatGroupScenario")]
    Task UpdateChatGroupScenario(Guid chatGroupId, string? scenario);

    [Alias("DownloadMessages")]
    Task<List<ChatMessage>> DownloadMessages();

    [Alias("CancelAllGenerations")]
    Task CancelAllGenerations(CancellationToken cancellationToken = default);

    [AlwaysInterleave]
    [Alias("GetCastAsync")]
    Task<IReadOnlyList<CastMember>> GetCastAsync();

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

    public void Apply(ChatGroupRemovedEvent @event)
    {
        ChatGroups.RemoveAll(g => g.Id == @event.ChatGroupId);
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

[GenerateSerializer, Alias(nameof(ChatGroupRemovedEvent))]
public sealed record class ChatGroupRemovedEvent : PartyEvent
{
    [Id(0)]
    public Guid ChatGroupId { get; set; }
}

[GenerateSerializer, Alias(nameof(PartyDeletedEvent))]
public sealed record class PartyDeletedEvent : PartyEvent;
