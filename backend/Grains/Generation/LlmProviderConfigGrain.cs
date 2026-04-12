using Microsoft.Extensions.Options;
using PartyTown.Configuration;

namespace PartyTown.Grains.Generation;

/// <summary>
/// Singleton grain (key 0) that persists the list of LLM provider configurations.
/// Replaces appsettings.json as the runtime source of truth for providers.
/// Seeds from IOptions<LlmOptions> on first activation for backwards compatibility.
/// </summary>
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
    }

    public Task<List<LlmProviderEntry>> GetProvidersAsync()
        => Task.FromResult(state.State.Providers.ToList());

    public async Task<LlmProviderEntry> AddProviderAsync(LlmProviderEntry entry)
    {
        var newEntry = entry with { Id = Guid.NewGuid() };
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

        state.State.Providers[idx] = entry;
        await state.WriteStateAsync();
        await InvalidateRouter();
        logger.LogInformation("Updated LLM provider {ProviderId}", entry.Id);
        return entry;
    }

    public async Task RemoveProviderAsync(Guid providerId)
    {
        var removed = state.State.Providers.RemoveAll(p => p.Id == providerId);
        if (removed == 0)
            throw new InvalidOperationException($"Provider {providerId} not found");

        await state.WriteStateAsync();
        await InvalidateRouter();
        logger.LogInformation("Removed LLM provider {ProviderId}", providerId);
    }

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
                SupportedComplexities = JobComplexity.General,
            },
            OpenRouterProviderConfig o => new LlmProviderEntry
            {
                Id = Guid.NewGuid(),
                Type = "openrouter",
                BaseUrl = o.BaseUrl,
                ApiKey = o.ApiKey,
                ModelName = o.TEMP_ModelName,
                SupportedComplexities = JobComplexity.General,
            },
            _ => throw new InvalidOperationException($"Unknown provider type: {p.GetType().Name}")
        }).ToList();

        state.State.Providers = entries;
        state.State.SeededFromOptions = true;
        await state.WriteStateAsync();
        logger.LogInformation("Seeded {Count} LLM providers from appsettings", entries.Count);
    }

    private Task InvalidateRouter()
        => GrainFactory.GetGrain<ILlmRouterGrain>(0).InvalidateCacheAsync();
}

[Alias("PartyTown.Grains.Generation.ILlmProviderConfigGrain")]
public interface ILlmProviderConfigGrain : IGrainWithIntegerKey
{
    [Alias("GetProvidersAsync")]
    Task<List<LlmProviderEntry>> GetProvidersAsync();

    [Alias("AddProviderAsync")]
    Task<LlmProviderEntry> AddProviderAsync(LlmProviderEntry entry);

    [Alias("UpdateProviderAsync")]
    Task<LlmProviderEntry> UpdateProviderAsync(LlmProviderEntry entry);

    [Alias("RemoveProviderAsync")]
    Task RemoveProviderAsync(Guid providerId);
}

[GenerateSerializer]
[Alias("PartyTown.Grains.Generation.LlmProviderConfigState")]
public sealed class LlmProviderConfigState
{
    [Id(0)] public List<LlmProviderEntry> Providers { get; set; } = [];
    [Id(1)] public bool SeededFromOptions { get; set; }
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
}
