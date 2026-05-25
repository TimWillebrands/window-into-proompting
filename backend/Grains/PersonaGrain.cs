using Orleans.Concurrency;
using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;

namespace PartyTown.Grains;

/// <summary>
/// Grain that stores a single persona's data and reacts to messages. Thin Orleans
/// shell over persisted persona state + a per-activation <see cref="InFlightStore"/> +
/// the DI-injected <see cref="RaceTrigger"/> and <see cref="ResponsePipeline"/>.
///
/// Marked [Reentrant] so multiple concurrent NotifyMessageAsync calls don't deadlock,
/// and so CancelGenerationAsync can interrupt an in-flight NotifyMessageAsync.
/// </summary>
[Reentrant]
public sealed class PersonaGrain(
    [PersistentState(stateName: "persona", storageName: "personas")]
    IPersistentState<Persona> state,
    RaceTrigger raceTrigger,
    ResponsePipeline pipeline,
    ILogger<PersonaGrain> logger)
    : Grain, IPersonaGrain
{
    private readonly InFlightStore _store = new();

    public Task CancelGenerationAsync() => _store.CancelAllAsync();

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "PersonaGrain activated - Name: '{PersonaName}' - Id: '{PersonaId}'",
            state.State.Name,
            this.GetPrimaryKey());
        return base.OnActivateAsync(cancellationToken);
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "PersonaGrain deactivating: {Reason} - Id: '{PersonaId}'",
            reason.ReasonCode,
            this.GetPrimaryKey());
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    public Task SetPersona(Persona persona) =>
        SetPersona(persona.Name, persona.SystemPrompt, persona.Bio);

    public async Task SetPersona(string name, string systemPrompt, string? bio)
        => await UpdateStateAsync(current => current with
        {
            Id = this.GetPrimaryKey(),
            Name = name,
            SystemPrompt = systemPrompt,
            Bio = bio
        });

    public async Task SetName(string name)
        => await UpdateStateAsync(current => current with
        {
            Id = this.GetPrimaryKey(),
            Name = name
        });

    public async Task SetSystemPrompt(string systemPrompt)
        => await UpdateStateAsync(current => current with
        {
            Id = this.GetPrimaryKey(),
            SystemPrompt = systemPrompt
        });

    public async Task SetBio(string? bio)
        => await UpdateStateAsync(current => current with
        {
            Id = this.GetPrimaryKey(),
            Bio = bio
        });

    public Task<Persona> GetPersona() =>
        Task.FromResult(state.State with
        {
            Id = this.GetPrimaryKey()
        });

    public Task DeletePersona() =>
        state.ClearStateAsync();

    /// <summary>
    /// Called by ChatGroupGrain when a new message arrives in the chat group.
    /// Delegates race evaluation + per-turn orchestration to the injected services.
    /// </summary>
    public async Task NotifyMessageAsync(Guid chatGroupId, ChatMessage triggeringMessage, CancellationToken ct = default)
    {
        if (triggeringMessage.SenderId == this.GetPrimaryKey()) return;
        var chatGroup = GrainFactory.GetGrain<IChatGroupGrain>(chatGroupId);
        var persona = state.State with { Id = this.GetPrimaryKey() };
        await raceTrigger.EvaluateAsync(persona, chatGroupId, triggeringMessage, chatGroup, _store, ct);
        await pipeline.HandleAsync(persona, chatGroupId, triggeringMessage, chatGroup, _store, ct);
    }

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
    [Alias("SetPersonaFromModel")]
    Task SetPersona(Persona persona);

    [Alias("SetPersona")]
    Task SetPersona(string name, string systemPrompt, string? bio);

    [Alias("SetName")]
    Task SetName(string name);

    [Alias("SetSystemPrompt")]
    Task SetSystemPrompt(string systemPrompt);

    [Alias("SetBio")]
    Task SetBio(string? bio);

    [Alias("GetPersona")]
    Task<Persona> GetPersona();

    [Alias("DeletePersona")]
    Task DeletePersona();

    [Alias("NotifyMessageAsync")]
    Task NotifyMessageAsync(Guid chatGroupId, ChatMessage triggeringMessage, CancellationToken ct);

    [Alias("CancelGenerationAsync")]
    Task CancelGenerationAsync();
}
