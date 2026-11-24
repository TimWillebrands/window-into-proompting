using Orleans.Concurrency;
using Orleans.EventSourcing;
using Orleans.Providers;
using Orleans.Streams;
using PartyTown.Logging;
using PartyTown.Model;
using PartyTown.Services.Streaming;

namespace PartyTown.Grains;

[LogConsistencyProvider(ProviderName = "PartyStateStorage")]
[StorageProvider(ProviderName = "parties")]
public sealed class PartyGrain(
    ILogger<PartyGrain> logger)
    : JournaledGrain<PartyState, PartyEvent>, IPartyGrain
{
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Grain activated");
        await base.OnActivateAsync(cancellationToken);
        if (State.ChatGroups.Any(g => g.Kind == ChatGroupKinds.Group && g.MemberIds is null))
            this.RegisterGrainTimer(KnowGroupMembersAsync, new GrainTimerCreationOptions(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan));
    }

    private async Task KnowGroupMembersAsync()
    {
        foreach (var group in State.ChatGroups.Where(g => g.Kind == ChatGroupKinds.Group && g.MemberIds is null).ToList())
        {
            try
            {
                var members = await GrainFactory.GetGrain<IChatGroupGrain>(group.Id).GetParticipantIdsAsync();
                RaiseEvent(new ChatGroupMembersSetEvent { ChatGroupId = group.Id, MemberIds = members.ToList() });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Couldn't read the members of group {ChatGroupId}", group.Id);
            }
        }
        await ConfirmEvents();
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
        => Task.FromResult(State.ChatGroups.Select(g => g.Scenario is null ? g : g with { Scenario = null }).ToList());

    public async Task<ChatGroupInfo> CreateChatGroup(string name, List<Guid>? memberIds = null, Guid? createdBy = null)
    {
        var members = (memberIds ?? State.Participants.Select(p => p.Id).ToList()).Where(id => id != Guid.Empty).Distinct().ToList();
        var chatGroup = new ChatGroupInfo
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Kind = ChatGroupKinds.Group,
            MemberIds = members,
            CreatedBy = createdBy,
        };

        RaiseEvent(new ChatGroupCreatedEvent { ChatGroup = chatGroup });
        await ConfirmEvents();

        var registry = GrainFactory.GetGrain<IPartyRootGrain>(Guid.Empty);
        await registry.RegisterChatGroup(chatGroup.Id, this.GetPrimaryKey());
        await GrainFactory.GetGrain<IChatGroupGrain>(chatGroup.Id).SetParticipantIdsAsync(members.ToHashSet());
        await PublishConversationAsync(chatGroup);

        return chatGroup;
    }

    public async Task<ChatGroupInfo?> SetGroupMembersAsync(Guid chatGroupId, List<Guid> memberIds)
    {
        if (State.ChatGroups.FirstOrDefault(g => g.Id == chatGroupId) is not { Kind: ChatGroupKinds.Group }) return null;
        var members = memberIds.Where(id => id != Guid.Empty).Distinct().ToList();
        RaiseEvent(new ChatGroupMembersSetEvent { ChatGroupId = chatGroupId, MemberIds = members });
        await ConfirmEvents();
        await GrainFactory.GetGrain<IChatGroupGrain>(chatGroupId).SetParticipantIdsAsync(members.ToHashSet());
        var updated = State.ChatGroups.First(g => g.Id == chatGroupId);
        await PublishConversationAsync(updated);
        return updated;
    }

    private Task PublishConversationAsync(ChatGroupInfo group)
        => this.GetStreamProvider(PartyStreamIds.Provider)
            .GetStream<PartyStreamEvent>(PartyStreamIds.PartyEventsNamespace, PartyStreamIds.PartyEventId(this.GetPrimaryKey()))
            .OnNextAsync(new PartyStreamEvent { Type = "conversation", ChatGroupId = group.Id, Conversation = group });

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
        await PublishConversationAsync(chatGroup);

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
    Task<ChatGroupInfo> CreateChatGroup(string name, List<Guid>? memberIds = null, Guid? createdBy = null);

    [Alias("SetGroupMembersAsync")]
    Task<ChatGroupInfo?> SetGroupMembersAsync(Guid chatGroupId, List<Guid> memberIds);

    [Alias("GetOrCreateDirectChatAsync")]
    Task<ChatGroupInfo> GetOrCreateDirectChatAsync(Guid a, Guid b);

    [Alias("CreatePlaceRoomAsync")]
    Task<ChatGroupInfo> CreatePlaceRoomAsync(string name);

    [Alias("RemoveChatGroup")]
    Task RemoveChatGroup(Guid chatGroupId);

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
        ChatGroups.Add(@event.ChatGroup with { Scenario = null });
    }

    public void Apply(ChatGroupMembersSetEvent @event)
    {
        var idx = ChatGroups.FindIndex(g => g.Id == @event.ChatGroupId);
        if (idx >= 0)
            ChatGroups[idx] = ChatGroups[idx] with { MemberIds = @event.MemberIds.ToList() };
    }

    public void Apply(ChatGroupRemovedEvent @event)
    {
        ChatGroups.RemoveAll(g => g.Id == @event.ChatGroupId);
    }

    public void Apply(ChatGroupScenarioUpdatedEvent _)
    {
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

[GenerateSerializer, Alias(nameof(ChatGroupMembersSetEvent))]
public sealed record class ChatGroupMembersSetEvent : PartyEvent
{
    [Id(0)]
    public Guid ChatGroupId { get; set; }

    [Id(1)]
    public List<Guid> MemberIds { get; set; } = new();
}

[GenerateSerializer, Alias(nameof(ChatGroupRemovedEvent))]
public sealed record class ChatGroupRemovedEvent : PartyEvent
{
    [Id(0)]
    public Guid ChatGroupId { get; set; }
}

[GenerateSerializer, Alias(nameof(PartyDeletedEvent))]
public sealed record class PartyDeletedEvent : PartyEvent;
