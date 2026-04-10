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
    /// <summary>
    /// Handles grain activation and records an activation log message.
    /// </summary>
    /// <param name="cancellationToken">Token to observe while performing activation.</param>
    /// <returns>A task that completes when activation processing is finished.</returns>
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

    /// <summary>
    /// Sets the party's participant list and propagates the update to all registered chat groups.
    /// </summary>
    /// <param name="participants">The participants to assign to the party; the list is persisted as the party's current participants and then forwarded to each chat group grain.</param>
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

    /// <summary>
        /// Gets a snapshot of the party's chat groups.
        /// </summary>
        /// <returns>A list containing a copy of the stored <see cref="ChatGroupInfo"/> entries.</returns>
        public Task<List<ChatGroupInfo>> GetChatGroups()
        => Task.FromResult<List<ChatGroupInfo>>([.. State.ChatGroups]);

    /// <summary>
    /// Create a new chat group for this party, persist the creation event, and register the group with the party root registry.
    /// </summary>
    /// <param name="name">Display name for the new chat group.</param>
    /// <returns>The created <see cref="ChatGroupInfo"/>, including its assigned Id and CreatedAt timestamp.</returns>
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

    /// <summary>
    /// Aggregate messages from all chat groups belonging to this party.
    /// </summary>
    /// <returns>List of up to 100 messages from across chat groups, ordered by MessageId (ascending).</returns>
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

    /// <summary>
    /// Requests cancellation of all ongoing generation operations across the party's chat groups.
    /// Currently a no-op when chat group grains do not support cancellation; cancellation support may be added later.
    /// </summary>
    /// <returns>A task that completes once cancellation requests have been issued (may complete immediately if cancellation is unsupported).</returns>
    public Task CancelAllGenerations()
    {
        // Delegate cancellation to all chat group grains
        // TODO: Add cancellation support to ChatGroupGrain in a future iteration
        return Task.CompletedTask;
    }

    /// <summary>
    /// Marks the party as deleted and persists that state change.
    /// </summary>
    /// <returns>A task that completes when the deletion event has been confirmed in storage.</returns>
    public async Task DeleteParty()
    {
        RaiseEvent(new PartyDeletedEvent());
        await ConfirmEvents();
    }
}

[Alias("backend.IPartyGrain")]
public interface IPartyGrain : IGrainWithGuidKey
{
    /// <summary>
    /// Retrieves a snapshot of the party's current metadata and participants.
    /// </summary>
    /// <returns>A <see cref="PartyInfo"/> containing the party's Id, Name, and a copy of the Participants list.</returns>
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

    /// <summary>
    /// Fetches messages from every chat group in this party and aggregates them into a single list.
    /// </summary>
    /// <returns>A list of up to 100 chat messages aggregated from all chat groups, ordered by MessageId (ascending).</returns>
    [Alias("DownloadMessages")]
    Task<List<ChatMessage>> DownloadMessages();

    /// <summary>
    /// Requests cancellation of all in-progress assistant/message generation operations associated with this party.
    /// </summary>
    /// <remarks>
    /// If there are no active generations or cancellation is not supported by underlying grains, the call may complete without effect.
    /// </remarks>
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

    /// <summary>
    /// Replaces the state's Participants list with a copy of the participants carried by the event.
    /// </summary>
    /// <param name="@event">The event containing the new participants to apply to state.</param>
    public void Apply(ParticipantsSetEvent @event)
    {
        Participants = [.. @event.Participants];
    }

    /// <summary>
    /// Reset the party state to its default (empty) values.
    /// </summary>
    /// <param name="_">The deletion event (ignored); triggers clearing of Id, Name, Participants, and ChatGroups.</param>
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
