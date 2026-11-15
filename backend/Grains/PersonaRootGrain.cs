using PartyTown.Model;

namespace PartyTown.Grains;

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
    public async Task AddPersona(Persona persona)
    {
        await GrainFactory.GetGrain<IPersonaGrain>(persona.Id).SetPersona(persona);
        if (state.State.PersonaIds.Add(persona.Id))
            await state.WriteStateAsync();
    }

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

    public async Task<PersonaMetadata[]> GetAllMetadata()
    {
        var personas = await GetAll();
        return [.. personas.Select(persona => new PersonaMetadata(persona.Id, persona.Name))];
    }

    public Task<bool> HasPersonaId(Guid personaId)
        => Task.FromResult(state.State.PersonaIds.Contains(personaId));

    public Task RemovePersona(Persona persona) =>
        RemovePersona(persona.Id);

    public async Task RemovePersona(Guid id)
    {
        logger.LogDebug("Unregistering entity: {EntityId}", id);

        var personaGrain = GrainFactory.GetGrain<IPersonaGrain>(id);

        await personaGrain.DeletePersona();

        if (state.State.PersonaIds.Remove(id))
        {
            await state.WriteStateAsync();
        }
    }
}

[Alias("backend.IPersonaRootGrain")]
public interface IPersonaRootGrain : IGrainWithGuidKey
{
    [Alias("AddPersona")]
    Task AddPersona(Guid id, string name, string systemPrompt, string? bio);

    [Alias("AddPersonaFromModel")]
    Task AddPersona(Persona persona);

    [Alias("GetAll")]
    Task<Persona[]> GetAll();

    [Alias("GetAllMetadata")]
    Task<PersonaMetadata[]> GetAllMetadata();

    [Alias("HasPersonaId")]
    Task<bool> HasPersonaId(Guid id);

    [Alias("RemovePersona")]
    Task RemovePersona(Guid id);

    [Alias("RemovePersonaFromModel")]
    Task RemovePersona(Persona persona);
}

[GenerateSerializer, Alias(nameof(PersonaRootState))]
public sealed record class PersonaRootState
{
    [Id(0)]
    public HashSet<Guid> PersonaIds { get; set; } = [];
}
