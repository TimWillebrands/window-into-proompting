using Microsoft.Extensions.Options;
using Orleans.Concurrency;
using PartyTown.Configuration;

namespace PartyTown.Grains.Generation;

public sealed class LlmProviderConfigGrain(
    [PersistentState(stateName: "llm-providers", storageName: "parties")] IPersistentState<LlmProviderConfigState> state,
    IOptions<LlmOptions> seedOptions,
    ILogger<LlmProviderConfigGrain> logger)
    : Grain, ILlmProviderConfigGrain
{
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

        if (!state.State.SeededFromOptions)
        {
            await SeedFromOptionsAsync();
        }

        if (!state.State.DecisionsSeeded)
        {
            await SeedDecisionProvidersAsync();
        }
    }

    public Task<List<LlmProviderEntry>> GetProvidersAsync()
        => Task.FromResult(state.State.Providers.ToList());

    public async Task<LlmProviderEntry> AddProviderAsync(LlmProviderEntry entry)
    {
        var newEntry = entry with { Id = Guid.NewGuid(), Share = WholeShare(entry.Share) };
        state.State.Providers.Add(newEntry);
        await state.WriteStateAsync();
        await InvalidateRouter();
        logger.LogInformation("Added LLM provider {ProviderId} ({Type}: {Url})", newEntry.Id, newEntry.Type, newEntry.BaseUrl);
        return newEntry;
    }

    public async Task<LlmProviderEntry> UpdateProviderAsync(LlmProviderEntry entry)
    {
        var idx = state.State.Providers.FindIndex(p => p.Id == entry.Id);
        if (idx < 0)
            throw new InvalidOperationException($"Provider {entry.Id} not found");

        entry = entry with { Share = WholeShare(entry.Share) };
        state.State.Providers[idx] = entry;
        await state.WriteStateAsync();
        await InvalidateRouter();
        await LlmEndpoints.Resolve(GrainFactory, entry.Type, entry.Id).ApplyConfigAsync(entry);
        logger.LogInformation("Updated LLM provider {ProviderId}", entry.Id);
        return entry;
    }

    public async Task RemoveProviderAsync(Guid providerId)
    {
        var removed = state.State.Providers.FirstOrDefault(p => p.Id == providerId)
            ?? throw new InvalidOperationException($"Provider {providerId} not found");
        state.State.Providers.Remove(removed);
        state.State.PersonaRoutes.RemoveAll(r => r.ProviderId == providerId);

        await state.WriteStateAsync();
        await InvalidateRouter();
        await LlmEndpoints.Resolve(GrainFactory, removed.Type, providerId).ApplyConfigAsync(null);
        logger.LogInformation("Removed LLM provider {ProviderId}", providerId);
    }

    public Task<List<PersonaRoute>> GetPersonaRoutesAsync()
        => Task.FromResult(state.State.PersonaRoutes.ToList());

    public async Task<List<PersonaRoute>> SetPersonaRoutesAsync(Guid personaId, List<PersonaRoute> routes)
    {
        if (routes.FirstOrDefault(r => !IsTier(r.Tier)) is { } badTier)
            throw new ArgumentException($"Not a single tier: {badTier.Tier}");
        if (routes.FirstOrDefault(r => state.State.Providers.All(p => p.Id != r.ProviderId)) is { } badProvider)
            throw new ArgumentException($"Provider {badProvider.ProviderId} not found");

        state.State.PersonaRoutes.RemoveAll(r => r.PersonaId == personaId);
        state.State.PersonaRoutes.AddRange(routes
            .GroupBy(r => r.Tier)
            .Select(g => g.Last() with { PersonaId = personaId }));
        await state.WriteStateAsync();
        await InvalidateRouter();
        logger.LogInformation("Persona {PersonaId} now has {Count} model routes", personaId, routes.Count);
        return state.State.PersonaRoutes.Where(r => r.PersonaId == personaId).ToList();
    }

    private static bool IsTier(JobComplexity tier) => LlmTiers.All.Contains(tier);

    private static int WholeShare(int share) => share > 0 ? share : Shares.Default;

    private async Task SeedFromOptionsAsync()
    {
        var providers = seedOptions.Value.Providers;
        if (providers.Count == 0)
        {
            logger.LogInformation("No providers in appsettings to seed from");
            state.State.SeededFromOptions = true;
            await state.WriteStateAsync();
            return;
        }

        var entries = providers.Select(p => p switch
        {
            OllamaProviderConfig o => new LlmProviderEntry
            {
                Id = Guid.NewGuid(),
                Type = "ollama",
                BaseUrl = o.BaseUrl,
                ModelName = o.TEMP_ModelName,
                SupportedComplexities = o.SupportedComplexities,
            },
            OpenRouterProviderConfig o => new LlmProviderEntry
            {
                Id = Guid.NewGuid(),
                Type = "openrouter",
                BaseUrl = o.BaseUrl,
                ApiKey = o.ApiKey,
                ModelName = o.TEMP_ModelName,
                SupportedComplexities = o.SupportedComplexities,
            },
            _ => throw new InvalidOperationException($"Unknown provider type: {p.GetType().Name}")
        }).ToList();

        state.State.Providers = entries;
        state.State.SeededFromOptions = true;
        await state.WriteStateAsync();
        logger.LogInformation("Seeded {Count} LLM providers from appsettings", entries.Count);
    }

    private async Task SeedDecisionProvidersAsync()
    {
        var entries = seedOptions.Value.DecisionProviders.Select(p => p with { Id = Guid.NewGuid() }).ToList();
        state.State.Providers.AddRange(entries);
        state.State.DecisionsSeeded = true;
        await state.WriteStateAsync();
        logger.LogInformation("Seeded {Count} Decisions providers", entries.Count);
    }

    private Task InvalidateRouter()
        => GrainFactory.GetGrain<ILlmRouterGrain>(0).InvalidateCacheAsync();
}

[Alias("PartyTown.Grains.Generation.ILlmProviderConfigGrain")]
public interface ILlmProviderConfigGrain : IGrainWithIntegerKey
{
    [AlwaysInterleave]
    [Alias("GetProvidersAsync")]
    Task<List<LlmProviderEntry>> GetProvidersAsync();

    [Alias("AddProviderAsync")]
    Task<LlmProviderEntry> AddProviderAsync(LlmProviderEntry entry);

    [Alias("UpdateProviderAsync")]
    Task<LlmProviderEntry> UpdateProviderAsync(LlmProviderEntry entry);

    [Alias("RemoveProviderAsync")]
    Task RemoveProviderAsync(Guid providerId);

    [Alias("GetPersonaRoutesAsync")]
    Task<List<PersonaRoute>> GetPersonaRoutesAsync();

    [Alias("SetPersonaRoutesAsync")]
    Task<List<PersonaRoute>> SetPersonaRoutesAsync(Guid personaId, List<PersonaRoute> routes);
}

[GenerateSerializer]
[Alias("PartyTown.Grains.Generation.LlmProviderConfigState")]
public sealed class LlmProviderConfigState
{
    [Id(0)] public List<LlmProviderEntry> Providers { get; set; } = [];
    [Id(1)] public bool SeededFromOptions { get; set; }
    [Id(2)] public List<PersonaRoute> PersonaRoutes { get; set; } = new();
    [Id(3)] public bool DecisionsSeeded { get; set; }
}

[GenerateSerializer]
[Alias("PartyTown.Grains.Generation.PersonaRoute")]
public sealed record class PersonaRoute
{
    [Id(0)] public Guid PersonaId { get; init; }
    [Id(1)] public JobComplexity Tier { get; init; }
    [Id(2)] public Guid ProviderId { get; init; }
}

[GenerateSerializer]
[Alias("PartyTown.Grains.Generation.LlmProviderEntry")]
public sealed record class LlmProviderEntry
{
    [Id(0)] public Guid Id { get; init; }
    [Id(1)] public required string Type { get; init; }
    [Id(2)] public required string BaseUrl { get; init; }
    [Id(3)] public string? ApiKey { get; init; }
    [Id(4)] public string? ModelName { get; init; }
    [Id(5)] public JobComplexity SupportedComplexities { get; init; } = JobComplexity.General;
    [Id(6)] public TimeSpan? GenerationTimeout { get; init; }
    [Id(7)] public bool IsEnabled { get; init; } = true;
    [Id(8)] public int Priority { get; init; }
    [Id(9)] public bool Reserved { get; init; }
    [Id(11)] public string? Tailnet { get; init; }
    [Id(12)] public ReasoningEffort ReasoningEffort { get; init; }
    [Id(13)] public int Share { get; init; } = Shares.Default;
}
