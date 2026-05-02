using System.Diagnostics;
using Orleans.Concurrency;
using PartyTown.Logging;

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
        // Routing is currently silent ("which provider answered?" requires reading logs).
        // This span makes the decision tree visible in the Aspire trace: one llm.route
        // child under whichever caller-span (persona.think / persona.generate) invoked us,
        // with a per-candidate event explaining why each provider was kept or dropped.
        using var routeSpan = Tracing.Persona.StartActivity("llm.route", ActivityKind.Internal);
        routeSpan?.SetTag("job.complexity", jobComplexity.ToString());

        var providers = await GetProvidersAsync(cancellationToken);
        routeSpan?.SetTag("candidates.total", providers.Count);

        var modelProviderTasks = providers.Select(async grain =>
        {
            try
            {
                var models = await grain.GetModelsAsync(cancellationToken);
                return new
                {
                    Grain = grain,
                    Pressure = await grain.PressureAsync(),
                    CompatibleModels = models.Where(m => m.Supports(jobComplexity)).ToList(),
                    Failed = false,
                    Error = (string?)null
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not TaskCanceledException)
            {
                logger.LogWarning(ex, "Provider {GrainId} failed during routing, skipping", grain.GetPrimaryKey());
                return new
                {
                    Grain = grain,
                    Pressure = int.MaxValue,
                    CompatibleModels = new List<LlmModel>(),
                    Failed = true,
                    Error = (string?)ex.Message
                };
            }
        });

        var finishedTasks = await Task.WhenAll(modelProviderTasks);

        foreach (var candidate in finishedTasks)
        {
            var status = candidate.Failed
                ? "failed"
                : candidate.CompatibleModels.Count == 0
                    ? "incompatible"
                    : "eligible";
            routeSpan?.AddEvent(new ActivityEvent("candidate", default, new ActivityTagsCollection
            {
                ["grain.id"] = candidate.Grain.GetPrimaryKey(),
                ["pressure"] = candidate.Pressure,
                ["compatible_models"] = candidate.CompatibleModels.Count,
                ["status"] = status,
                ["error"] = candidate.Error
            }));
        }

        var compatibleProvider = finishedTasks
            .Where(provider => !provider.Failed && provider.CompatibleModels.Any())
            .OrderBy(provider => provider.Pressure)
            .FirstOrDefault();

        if (compatibleProvider is null)
        {
            routeSpan?.SetStatus(ActivityStatusCode.Error, "no compatible providers");
            routeSpan?.SetTag("decision", "none");
            throw new InvalidOperationException($"No model-providers available for job complexity {jobComplexity}");
        }

        routeSpan?.SetTag("decision", "selected");
        routeSpan?.SetTag("selected.grain_id", compatibleProvider.Grain.GetPrimaryKey());
        routeSpan?.SetTag("selected.pressure", compatibleProvider.Pressure);
        routeSpan?.SetTag("selected.compatible_models", compatibleProvider.CompatibleModels.Count);
        return compatibleProvider.Grain;
    }

    private async Task<IReadOnlyList<ILlmEndpointGrain>> GetProvidersAsync(CancellationToken cancellationToken)
    {
        if (_cachedProviders is not null)
            return _cachedProviders;

        var configGrain = GrainFactory.GetGrain<ILlmProviderConfigGrain>(0);
        var entries = await configGrain.GetProvidersAsync();

        _cachedProviders = entries
            .Where(e => e.IsEnabled)
            .Select(e => e.Type switch
            {
                "ollama" => GrainFactory.GetGrain<IOllamaEndpointGrain>(e.Id) as ILlmEndpointGrain,
                "openrouter" => GrainFactory.GetGrain<IOpenRouterEndpointGrain>(e.Id),
                _ => throw new InvalidOperationException($"Unsupported provider type: {e.Type}")
            }).ToList();

        logger.LogDebug("Refreshed provider cache with {Count} providers", _cachedProviders.Count);
        return _cachedProviders;
    }
}
