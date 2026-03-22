using PartyTown.Model;

namespace PartyTown.Grains;

/// <summary>
/// Root grain that tracks all persona grain ids and coordinates creation/removal.
/// </summary>
public sealed class PersonaRootGrain(
    [PersistentState( stateName: "personaRoot", storageName: "personas")]
    IPersistentState<PersonaRootState> state,
    ILogger<PersonaRootGrain> logger)
    : Grain, IPersonaRootGrain
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
    /// <summary>
    /// Adds or updates a persona based on a model instance.
    /// </summary>
    public Task AddPersona(Persona persona) =>
        AddPersona(persona.Id, persona.Name, persona.SystemPrompt, persona.Bio);

    /// <summary>
    /// Creates or updates a persona grain and records its id in the root state.
    /// </summary>
    public async Task AddPersona(Guid id, string name, string systemPrompt, string? bio)
    {
        logger.LogDebug("Registering entity: {EntityId}", id);

        var personaGrain = GrainFactory.GetGrain<IPersonaGrain>(id);
        await personaGrain.SetPersona(name, systemPrompt, bio);

        if (state.State.PersonaIds.Add(id))
        {
            await state.WriteStateAsync();
        }
    }

    /// <summary>
    /// Returns all persona models by querying each registered persona grain.
    /// </summary>
    public async Task<Persona[]> GetAll()
    {
        logger.LogDebug("Listing all entities, count: {Count}", state.State.PersonaIds.Count);

        var getPersonaTasks = state.State.PersonaIds
            .Select(id => GrainFactory.GetGrain<IPersonaGrain>(id).GetPersona())
            .ToArray();

        if (getPersonaTasks.Length == 0)
        {
            return [];
        }

        return await Task.WhenAll(getPersonaTasks);
    }

    /// <summary>
    /// Returns lightweight persona metadata for all registered personas.
    /// </summary>
    public async Task<PersonaMetadata[]> GetAllMetadata()
    {
        var personas = await GetAll();
        return [.. personas.Select(persona => new PersonaMetadata(persona.Id, persona.Name))];
    }

    public Task<bool> HasPersonaId(Guid personaId)
        => Task.FromResult(state.State.PersonaIds.Contains(personaId));

    /// <summary>
    /// Removes a persona based on a model instance.
    /// </summary>
    public Task RemovePersona(Persona persona) =>
        RemovePersona(persona.Id);

    /// <summary>
    /// Deletes the persona grain and removes its id from the root state.
    /// </summary>
    public async Task RemovePersona(Guid id)
    {
        logger.LogDebug("Unregistering entity: {EntityId}", id);

        var personaGrain = GrainFactory.GetGrain<IPersonaGrain>(id);

        // Persona's are cardinality n-1 to a root, so a personaRoot acts as
        // the aggregate and can thus also delete the persona.
        await personaGrain.DeletePersona();

        if (state.State.PersonaIds.Remove(id))
        {
            await state.WriteStateAsync();
        }
    }
}

/// <summary>
/// Grain contract for the persona root registry.
/// </summary>
[Alias("backend.IPersonaRootGrain")]
public interface IPersonaRootGrain : IGrainWithGuidKey
{
    /// <summary>
    /// Creates or updates a persona and registers its id.
    /// </summary>
    [Alias("AddPersona")]
    Task AddPersona(Guid id, string name, string systemPrompt, string? bio);

    /// <summary>
    /// Creates or updates a persona from a model instance.
    /// </summary>
    [Alias("AddPersonaFromModel")]
    Task AddPersona(Persona persona);

    /// <summary>
    /// Returns all persona models stored in the system.
    /// </summary>
    [Alias("GetAll")]
    Task<Persona[]> GetAll();

    /// <summary>
    /// Returns all persona models stored in the system.
    /// </summary>
    [Alias("GetAllMetadata")]
    Task<PersonaMetadata[]> GetAllMetadata();

    /// <summary>
    /// Checks if a certain Persona exists in this root.
    /// </summary>
    [Alias("HasPersonaId")]
    Task<bool> HasPersonaId(Guid id);

    /// <summary>
    /// Deletes a persona and unregisters its id.
    /// </summary>
    [Alias("RemovePersona")]
    Task RemovePersona(Guid id);

    /// <summary>
    /// Deletes a persona based on a model instance.
    /// </summary>
    [Alias("RemovePersonaFromModel")]
    Task RemovePersona(Persona persona);
}

/// <summary>
/// Persistent state for the persona root grain.
/// </summary>
[GenerateSerializer, Alias(nameof(PersonaRootState))]
public sealed record class PersonaRootState
{
    /// <summary>
    /// Set of persona grain ids registered with the root grain.
    /// </summary>
    [Id(0)]
    public HashSet<Guid> PersonaIds { get; set; } = [];
}
