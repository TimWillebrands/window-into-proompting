using Orleans.Concurrency;

namespace PartyTown.Grains.Generation;

[Reentrant]
public sealed class LlmRouterGrain(ILogger<LlmRouterGrain> logger) : Grain, ILlmRouterGrain
{
    private IReadOnlyList<ILlmEndpointGrain>? _cachedProviders;

    public Task InvalidateCacheAsync()
    {
        _cachedProviders = null;
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<LlmModel>> GetModelsAsync(
        CancellationToken cancellationToken = default)
    {
        var providers = await GetProvidersAsync(cancellationToken);
        var tasks = providers
            .Select(async grain =>
            {
                try { return await grain.GetModelsAsync(cancellationToken); }
                catch (Exception ex) when (ex is not OperationCanceledException and not TaskCanceledException)
                {
                    logger.LogWarning(ex, "Failed to get models from provider {GrainId}", grain.GetPrimaryKey());
                    return Array.Empty<LlmModel>();
                }
            });
        var results = await Task.WhenAll(tasks);
        return results.SelectMany(model => model).ToList();
    }

    public async Task<ILlmEndpointGrain> RouteAsync(
        JobComplexity jobComplexity,
        CancellationToken cancellationToken = default)
    {
        var providers = await GetProvidersAsync(cancellationToken);
        var modelProviderTasks = providers.Select(async grain =>
        {
            try
            {
                var models = await grain.GetModelsAsync(cancellationToken);
                return new
                {
                    Grain = grain,
                    Pressure = await grain.PressureAsync(),
                    CompatibleModels = models.Where(m => m.Supports(jobComplexity)),
                    Failed = false
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not TaskCanceledException)
            {
                logger.LogWarning(ex, "Provider {GrainId} failed during routing, skipping", grain.GetPrimaryKey());
                return new
                {
                    Grain = grain,
                    Pressure = int.MaxValue,
                    CompatibleModels = Enumerable.Empty<LlmModel>(),
                    Failed = true
                };
            }
        });

        var finishedTasks = await Task.WhenAll(modelProviderTasks);
        var compatibleProvider = finishedTasks
            .Where(provider => !provider.Failed && provider.CompatibleModels.Any())
            .OrderBy(provider => provider.Pressure)
            .FirstOrDefault();

        return compatibleProvider is null
            ? throw new InvalidOperationException($"No model-providers available for job complexity {jobComplexity}")
            : compatibleProvider.Grain;
    }

    private async Task<IReadOnlyList<ILlmEndpointGrain>> GetProvidersAsync(CancellationToken cancellationToken)
    {
        if (_cachedProviders is not null)
            return _cachedProviders;

        var configGrain = GrainFactory.GetGrain<ILlmProviderConfigGrain>(0);
        var entries = await configGrain.GetProvidersAsync();

        _cachedProviders = entries.Select(e => e.Type switch
        {
            "ollama" => GrainFactory.GetGrain<IOllamaEndpointGrain>(e.Id) as ILlmEndpointGrain,
            "openrouter" => GrainFactory.GetGrain<IOpenRouterEndpointGrain>(e.Id),
            _ => throw new InvalidOperationException($"Unsupported provider type: {e.Type}")
        }).ToList();

        logger.LogDebug("Refreshed provider cache with {Count} providers", _cachedProviders.Count);
        return _cachedProviders;
    }
}
