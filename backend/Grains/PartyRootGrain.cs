using PartyTown.Model;

namespace PartyTown.Grains;

public sealed class PartyRootGrain(
    [PersistentState(stateName: "partyRoot", storageName: "parties")]
    IPersistentState<PartyRootState> state,
    ILogger<PartyRootGrain> logger) : Grain, IPartyRootGrain
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

    public async Task AddParty(PartyInfo party)
    {
        logger.LogDebug("Registering entity: {EntityId}", party.Id);

        var partyGrain = GrainFactory.GetGrain<IPartyGrain>(party.Id);
        await partyGrain.SetParty(party);

        if (state.State.PartyIds.Add(party.Id))
        {
            await state.WriteStateAsync();
        }
    }

    /// <summary>
    /// Unregisters a party by deleting its party grain, removing its ID from persistent state, and purging any chat-group mappings that pointed to that party; persists state if changes were made.
    /// </summary>
    /// <param name="id">The unique identifier of the party to remove.</param>
    public async Task RemoveParty(Guid id)
    {
        logger.LogDebug("Unregistering entity: {EntityId}", id);

        var partyGrain = GrainFactory.GetGrain<IPartyGrain>(id);
        await partyGrain.DeleteParty();

        if (state.State.PartyIds.Remove(id))
        {
            var staleKeys = state.State.ChatGroupToParty
                .Where(kvp => kvp.Value == id)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var key in staleKeys)
                state.State.ChatGroupToParty.Remove(key);

            await state.WriteStateAsync();
        }
    }

    /// <summary>
        /// Determines whether the specified party identifier is registered in the root state.
        /// </summary>
        /// <param name="id">The party identifier to check for registration.</param>
        /// <returns>`true` if the ID is registered, `false` otherwise.</returns>
        public Task<bool> HasPartyId(Guid id)
        => Task.FromResult(state.State.PartyIds.Contains(id));

    /// <summary>
    /// Associate a chat group with an existing party and persist the mapping.
    /// </summary>
    /// <param name="chatGroupId">The chat group identifier to register.</param>
    /// <param name="partyId">The existing party identifier to associate with the chat group.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="partyId"/> is not a known party.</exception>
    /// <exception cref="InvalidOperationException">Thrown if <paramref name="chatGroupId"/> is already registered.</exception>
    public async Task RegisterChatGroup(Guid chatGroupId, Guid partyId)
    {
        if (!state.State.PartyIds.Contains(partyId))
            throw new ArgumentException($"Party {partyId} does not exist.", nameof(partyId));

        if (state.State.ChatGroupToParty.ContainsKey(chatGroupId))
            throw new InvalidOperationException($"Chat group {chatGroupId} is already registered.");

        state.State.ChatGroupToParty[chatGroupId] = partyId;
        await state.WriteStateAsync();
    }

    /// <summary>
            /// Gets the party identifier associated with the specified chat group.
            /// </summary>
            /// <param name="chatGroupId">The chat group identifier to look up.</param>
            /// <returns>The party `Guid` mapped to the chat group, or `null` if no mapping exists.</returns>
            public Task<Guid?> GetPartyForChatGroup(Guid chatGroupId)
        => Task.FromResult(state.State.ChatGroupToParty.TryGetValue(chatGroupId, out var partyId)
            ? partyId
            : (Guid?)null);

    /// <summary>
    /// Gets all registered parties.
    /// </summary>
    /// <returns>An array of <see cref="PartyInfo"/> objects for every registered party; an empty array if no parties are registered.</returns>
    public async Task<PartyInfo[]> GetAll()
    {
        logger.LogDebug("Listing all entities, count: {Count}", state.State.PartyIds.Count);

        if (state.State.PartyIds.Count == 0)
        {
            return [];
        }

        var tasks = state.State.PartyIds
            .Select(id => GrainFactory.GetGrain<IPartyGrain>(id).GetParty())
            .ToArray();

        return await Task.WhenAll(tasks);
    }
}

[Alias("backend.IPartyRootGrain")]
public interface IPartyRootGrain : IGrainWithGuidKey
{
    [Alias("AddParty")]
    Task AddParty(PartyInfo party);

    [Alias("RemoveParty")]
    Task RemoveParty(Guid id);

    [Alias("HasPartyId")]
    Task<bool> HasPartyId(Guid id);

    /// <summary>
    /// Retrieves information for all registered parties.
    /// </summary>
    /// <returns>An array of <see cref="PartyInfo"/> for every registered party; an empty array if no parties are registered.</returns>
    [Alias("GetAll")]
    Task<PartyInfo[]> GetAll();

    /// <summary>
    /// Associate a chat group identifier with an existing party.
    /// </summary>
    /// <param name="chatGroupId">The chat group's unique identifier to register.</param>
    /// <param name="partyId">The party's unique identifier to associate with the chat group; must already be registered.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="partyId"/> is not present in the known party IDs.</exception>
    /// <exception cref="InvalidOperationException">Thrown if <paramref name="chatGroupId"/> is already registered to a party.</exception>
    [Alias("RegisterChatGroup")]
    Task RegisterChatGroup(Guid chatGroupId, Guid partyId);

    /// <summary>
    /// Gets the party identifier associated with the specified chat group.
    /// </summary>
    /// <param name="chatGroupId">The chat group's unique identifier.</param>
    /// <returns>The associated party's <see cref="Guid"/> if registered, or <c>null</c> if no mapping exists.</returns>
    [Alias("GetPartyForChatGroup")]
    Task<Guid?> GetPartyForChatGroup(Guid chatGroupId);
}

[GenerateSerializer, Alias(nameof(PartyRootState))]
public sealed record class PartyRootState
{
    [Id(0)]
    public HashSet<Guid> PartyIds { get; set; } = [];

    [Id(1)]
    public Dictionary<Guid, Guid> ChatGroupToParty { get; set; } = [];
}
