using Orleans.Concurrency;
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

    public Task<bool> HasPartyId(Guid id)
        => Task.FromResult(state.State.PartyIds.Contains(id));

    public async Task RegisterChatGroup(Guid chatGroupId, Guid partyId)
    {
        if (!state.State.PartyIds.Contains(partyId))
            throw new ArgumentException($"Party {partyId} does not exist.", nameof(partyId));

        if (state.State.ChatGroupToParty.ContainsKey(chatGroupId))
            throw new InvalidOperationException($"Chat group {chatGroupId} is already registered.");

        state.State.ChatGroupToParty[chatGroupId] = partyId;
        await state.WriteStateAsync();
    }

    public Task<Guid?> GetPartyForChatGroup(Guid chatGroupId)
        => Task.FromResult(state.State.ChatGroupToParty.TryGetValue(chatGroupId, out var partyId)
            ? partyId
            : (Guid?)null);

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

    public async Task<List<WorldEntry>> GetWorldsAsync()
        => (await GetAll())
            .Select(p => new WorldEntry(p.Id, p.Name, state.State.Stopped.Contains(p.Id), p.Id == state.State.Current, p.Participants))
            .ToList();

    public Task<Guid> GetCurrentAsync() => Task.FromResult(state.State.Current);

    public async Task<bool> SetCurrentAsync(Guid id)
    {
        if (!state.State.PartyIds.Contains(id)) return false;
        if (state.State.Current == id) return true;
        state.State.Current = id;
        await state.WriteStateAsync();
        return true;
    }

    public Task<bool> IsStoppedAsync(Guid id) => Task.FromResult(state.State.Stopped.Contains(id));

    public async Task<bool> SetStoppedAsync(Guid id, bool stopped)
    {
        if (!state.State.PartyIds.Contains(id)) return false;
        if (stopped ? state.State.Stopped.Add(id) : state.State.Stopped.Remove(id))
            await state.WriteStateAsync();
        return true;
    }
}

/// <summary>A World as the login screen lists it.</summary>
[GenerateSerializer, Alias("PartyTown.Grains.WorldEntry")]
public sealed record WorldEntry(
    [property: Id(0)] Guid Id,
    [property: Id(1)] string Name,
    [property: Id(2)] bool Stopped,
    [property: Id(3)] bool Current,
    [property: Id(4)] List<PartyParticipant> Participants);

[Alias("backend.IPartyRootGrain")]
public interface IPartyRootGrain : IGrainWithGuidKey
{
    [Alias("AddParty")]
    Task AddParty(PartyInfo party);

    [Alias("RemoveParty")]
    Task RemoveParty(Guid id);

    [Alias("HasPartyId")]
    Task<bool> HasPartyId(Guid id);

    [Alias("GetAll")]
    Task<PartyInfo[]> GetAll();

    [Alias("RegisterChatGroup")]
    Task RegisterChatGroup(Guid chatGroupId, Guid partyId);

    [Alias("GetPartyForChatGroup")]
    Task<Guid?> GetPartyForChatGroup(Guid chatGroupId);

    [Alias("GetWorldsAsync")]
    Task<List<WorldEntry>> GetWorldsAsync();

    /// <summary>The World new visitors and MCP agents land in.</summary>
    [AlwaysInterleave, Alias("GetCurrentAsync")]
    Task<Guid> GetCurrentAsync();

    /// <summary>False when no such World.</summary>
    [Alias("SetCurrentAsync")]
    Task<bool> SetCurrentAsync(Guid id);

    /// <summary>Stopped Worlds keep everything but take no writes and no turns.</summary>
    [AlwaysInterleave, Alias("IsStoppedAsync")]
    Task<bool> IsStoppedAsync(Guid id);

    /// <summary>False when no such World.</summary>
    [Alias("SetStoppedAsync")]
    Task<bool> SetStoppedAsync(Guid id, bool stopped);
}

[GenerateSerializer, Alias(nameof(PartyRootState))]
public sealed record class PartyRootState
{
    [Id(0)]
    public HashSet<Guid> PartyIds { get; set; } = [];

    [Id(1)]
    public Dictionary<Guid, Guid> ChatGroupToParty { get; set; } = [];

    [Id(2)]
    public HashSet<Guid> Stopped { get; set; } = new();

    /// <summary>Guid.Empty, the root, until someone moves the World.</summary>
    [Id(3)]
    public Guid Current { get; set; }
}
