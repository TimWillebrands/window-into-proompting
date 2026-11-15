using Orleans.Concurrency;
using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;

namespace PartyTown.Grains;

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

    public async Task SetPersona(Persona persona)
        => await UpdateStateAsync(current => current with
        {
            Id = this.GetPrimaryKey(),
            Name = persona.Name,
            SystemPrompt = persona.SystemPrompt,
            Bio = persona.Bio,
            Chattiness = Math.Clamp(persona.Chattiness, 0, 1),
            Impulsivity = Math.Clamp(persona.Impulsivity, 0, 1),
            Aura = Persona.CleanAura(persona.Aura)
        });

    public async Task SetAura(string? aura)
        => await UpdateStateAsync(current => current with
        {
            Id = this.GetPrimaryKey(),
            Aura = Persona.CleanAura(aura)
        });

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

    public async Task NotifyMessageAsync(Guid chatGroupId, ChatMessage triggeringMessage, CancellationToken ct = default)
    {
        if (triggeringMessage.SenderId == this.GetPrimaryKey()) return;
        var chatGroup = GrainFactory.GetGrain<IChatGroupGrain>(chatGroupId);
        var persona = state.State with { Id = this.GetPrimaryKey() };
        await raceTrigger.EvaluateAsync(persona, chatGroupId, triggeringMessage, chatGroup, _store, ct);
        await pipeline.HandleAsync(persona, chatGroupId, triggeringMessage, chatGroup, _store, ct);
    }

    public async Task AnswerAsync(Guid chatGroupId, ChatMessage line)
    {
        var chatGroup = GrainFactory.GetGrain<IChatGroupGrain>(chatGroupId);
        var persona = state.State with { Id = this.GetPrimaryKey() };
        await pipeline.AnswerAsync(persona, chatGroupId, line, chatGroup, _store, CancellationToken.None);
    }

    public async Task<string?> InitiateAsync(Guid chatGroupId, string intent, string? feeling)
    {
        var chatGroup = GrainFactory.GetGrain<IChatGroupGrain>(chatGroupId);
        var persona = state.State with { Id = this.GetPrimaryKey() };
        return await pipeline.InitiateAsync(persona, chatGroupId, chatGroup, _store, intent, feeling, CancellationToken.None);
    }

    private async Task UpdateStateAsync(Func<Persona, Persona> update)
    {
        var current = state.State ?? new Persona();
        state.State = update(current);
        await state.WriteStateAsync();
    }
}

[Alias("backend.IPersonaGrain")]
public interface IPersonaGrain : IGrainWithGuidKey
{
    [Alias("SetPersonaFromModel")]
    Task SetPersona(Persona persona);

    [Alias("SetPersona")]
    Task SetPersona(string name, string systemPrompt, string? bio);

    [Alias("SetAura")]
    Task SetAura(string? aura);

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

    [Alias("AnswerAsync")]
    Task AnswerAsync(Guid chatGroupId, ChatMessage line);

    [Alias("InitiateAsync")]
    Task<string?> InitiateAsync(Guid chatGroupId, string intent, string? feeling);
}
