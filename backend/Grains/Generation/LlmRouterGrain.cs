using System.Diagnostics;
using Orleans.Concurrency;
using PartyTown.Logging;

namespace PartyTown.Grains.Generation;

[Reentrant]
public sealed class LlmRouterGrain(ILogger<LlmRouterGrain> logger) : Grain, ILlmRouterGrain
{
    private IReadOnlyList<(LlmProviderEntry Entry, ILlmEndpointGrain Grain)>? _cachedProviders;

    private readonly Dictionary<Guid, DateTimeOffset> _benchedUntil = new();
    private static readonly TimeSpan BenchFor = TimeSpan.FromMinutes(10);

    public Task ReportFailureAsync(Guid providerId, string reason)
    {
        _benchedUntil[providerId] = DateTimeOffset.UtcNow + BenchFor;
        logger.LogWarning("Provider {ProviderId} benched for {Minutes} min: {Reason}", providerId, BenchFor.TotalMinutes, reason);
        return Task.CompletedTask;
    }

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
            .Select(p => p.Grain)
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

        // Tier support is a property of the provider entry, not of its model list — no
        // per-route network listing.
        var modelProviderTasks = providers.Select(async p =>
        {
            var supports = p.Entry.SupportedComplexities.HasFlag(jobComplexity);
            return new
            {
                Grain = p.Grain,
                Priority = p.Entry.Priority,
                Benched = _benchedUntil.TryGetValue(p.Entry.Id, out var until) && until > DateTimeOffset.UtcNow,
                Pressure = await p.Grain.PressureAsync(),
                CompatibleModels = supports ? new List<string> { p.Entry.ModelName } : new List<string>(),
                Failed = false,
                Error = (string?)null
            };
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

        // Healthy before benched (a benched provider is still better than none), then priority, then load.
        var compatibleProvider = finishedTasks
            .Where(provider => !provider.Failed && provider.CompatibleModels.Any())
            .OrderBy(provider => provider.Benched)
            .ThenBy(provider => provider.Priority)
            .ThenBy(provider => provider.Pressure)
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

    private async Task<IReadOnlyList<(LlmProviderEntry Entry, ILlmEndpointGrain Grain)>> GetProvidersAsync(CancellationToken cancellationToken)
    {
        if (_cachedProviders is not null)
            return _cachedProviders;

        var configGrain = GrainFactory.GetGrain<ILlmProviderConfigGrain>(0);
        var entries = await configGrain.GetProvidersAsync();

        _cachedProviders = entries
            .Where(e => e.IsEnabled)
            .Select(e => (e, LlmEndpoints.Resolve(GrainFactory, e.Type, e.Id))).ToList();

        logger.LogDebug("Refreshed provider cache with {Count} providers", _cachedProviders.Count);
        return _cachedProviders;
    }
}
