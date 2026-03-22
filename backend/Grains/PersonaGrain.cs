using PartyTown.Model;

namespace PartyTown.Grains;

/// <summary>
/// Grain that stores and manages a single persona's data.
/// </summary>
public sealed class PersonaGrain(
    [PersistentState( stateName: "persona", storageName: "personas")]
    IPersistentState<Persona> state,
    ILogger<PersonaGrain> logger)
    : Grain, IPersonaGrain
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
    /// Sets the persona fields using a model instance.
    /// </summary>
    public Task SetPersona(Persona persona) =>
        SetPersona(persona.Name, persona.SystemPrompt, persona.Bio);

    /// <summary>
    /// Sets the full persona payload for this grain.
    /// </summary>
    public async Task SetPersona(string name, string systemPrompt, string? bio)
        => await UpdateStateAsync(current => current with
        {
            Id = this.GetPrimaryKey(),
            Name = name,
            SystemPrompt = systemPrompt,
            Bio = bio
        });

    /// <summary>
    /// Updates the persona name.
    /// </summary>
    public async Task SetName(string name)
        => await UpdateStateAsync(current => current with
        {
            Id = this.GetPrimaryKey(),
            Name = name
        });

    /// <summary>
    /// Updates the persona system prompt.
    /// </summary>
    public async Task SetSystemPrompt(string systemPrompt)
        => await UpdateStateAsync(current => current with
        {
            Id = this.GetPrimaryKey(),
            SystemPrompt = systemPrompt
        });

    /// <summary>
    /// Updates the persona bio.
    /// </summary>
    public async Task SetBio(string? bio)
        => await UpdateStateAsync(current => current with
        {
            Id = this.GetPrimaryKey(),
            Bio = bio
        });

    /// <summary>
    /// Gets the current persona state, ensuring the id matches the grain key.
    /// </summary>
    public Task<Persona> GetPersona() =>
        Task.FromResult(state.State with
        {
            Id = this.GetPrimaryKey()
        });

    /// <summary>
    /// Clears the persona state from storage.
    /// </summary>
    public Task DeletePersona() =>
        state.ClearStateAsync();

    private async Task UpdateStateAsync(Func<Persona, Persona> update)
    {
        var current = state.State ?? new Persona();
        state.State = update(current);
        await state.WriteStateAsync();
    }
}

/// <summary>
/// Grain contract for managing a single persona.
/// </summary>
[Alias("backend.IPersonaGrain")]
public interface IPersonaGrain : IGrainWithGuidKey
{
    /// <summary>
    /// Sets the persona fields using a model instance.
    /// </summary>
    [Alias("SetPersonaFromModel")]
    Task SetPersona(Persona persona);

    /// <summary>
    /// Sets the full persona payload for this grain.
    /// </summary>
    [Alias("SetPersona")]
    Task SetPersona(string name, string systemPrompt, string? bio);

    /// <summary>
    /// Updates the persona name.
    /// </summary>
    [Alias("SetName")]
    Task SetName(string name);

    /// <summary>
    /// Updates the persona system prompt.
    /// </summary>
    [Alias("SetSystemPrompt")]
    Task SetSystemPrompt(string systemPrompt);

    /// <summary>
    /// Updates the persona bio.
    /// </summary>
    [Alias("SetBio")]
    Task SetBio(string? bio);

    /// <summary>
    /// Gets the current persona model.
    /// </summary>
    [Alias("GetPersona")]
    Task<Persona> GetPersona();

    /// <summary>
    /// Deletes the persona state.
    /// </summary>
    [Alias("DeletePersona")]
    Task DeletePersona();
}
