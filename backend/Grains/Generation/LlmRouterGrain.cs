using System.Diagnostics;
using Orleans.Concurrency;
using PartyTown.Logging;

namespace PartyTown.Grains.Generation;

[Reentrant]
public sealed class LlmRouterGrain(ILogger<LlmRouterGrain> logger) : Grain, ILlmRouterGrain
{
    private IReadOnlyList<(LlmProviderEntry Entry, ILlmEndpointGrain Grain)>? _cachedProviders;
    private Dictionary<(Guid Persona, JobComplexity Tier), Guid>? _cachedRoutes;

    private readonly Dictionary<Guid, (DateTimeOffset Until, string Reason)> _benched = new();
    private readonly Dictionary<Guid, string> _offline = new();
    private static readonly TimeSpan BenchFor = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(5);

    public Task ReportFailureAsync(Guid providerId, string reason)
    {
        _benched[providerId] = (DateTimeOffset.UtcNow + BenchFor, reason);
        logger.LogWarning("Provider {ProviderId} benched for {Minutes} min: {Reason}", providerId, BenchFor.TotalMinutes, reason);
        return Task.CompletedTask;
    }

    public Task ReportOfflineAsync(Guid providerId, string reason)
    {
        if (_offline.TryAdd(providerId, reason))
            logger.LogWarning("Provider {ProviderId} offline: {Reason}", providerId, reason);
        return Task.CompletedTask;
    }

    public async Task CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var providers = await GetProvidersAsync(cancellationToken);
        var pings = await Task.WhenAll(providers.Select(async p => (p.Entry.Id, Problem: await PingAsync(p.Grain, cancellationToken))));
        foreach (var (id, problem) in pings)
        {
            if (problem is not null)
            {
                await ReportOfflineAsync(id, problem);
                continue;
            }
            if (!_offline.Remove(id)) continue;
            // Whatever benched it while it was down no longer says anything about it.
            _benched.Remove(id);
            logger.LogInformation("Provider {ProviderId} is back online", id);
        }
    }

    private static async Task<string?> PingAsync(ILlmEndpointGrain grain, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(PingTimeout);
        try
        {
            return await grain.PingAsync(deadline.Token).WaitAsync(deadline.Token);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return ex is OperationCanceledException ? $"no answer within {PingTimeout.TotalSeconds:0}s" : ex.Message;
        }
    }

    public async Task<List<ProviderAvailability>> GetAvailabilityAsync()
    {
        var now = DateTimeOffset.UtcNow;
        return (await GetProvidersAsync(CancellationToken.None)).Select(p => p.Entry.Id).Select(id =>
            _offline.TryGetValue(id, out var why) ? new ProviderAvailability(id, ProviderState.Offline, why, null)
            : _benched.TryGetValue(id, out var bench) && bench.Until > now ? new ProviderAvailability(id, ProviderState.Benched, bench.Reason, bench.Until)
            : new ProviderAvailability(id, ProviderState.Ready, null, null)).ToList();
    }

    private bool IsOffline(Guid id) => _offline.ContainsKey(id);

    private bool IsBenched(Guid id) => _benched.TryGetValue(id, out var bench) && bench.Until > DateTimeOffset.UtcNow;

    public Task InvalidateCacheAsync()
    {
        _cachedProviders = null;
        _cachedRoutes = null;
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

    public async Task<ILlmEndpointGrain?> AssignedEndpointAsync(
        Guid personaId,
        JobComplexity tier,
        CancellationToken cancellationToken = default)
    {
        var routes = await GetRoutesAsync();
        if (!routes.TryGetValue((personaId, tier), out var providerId)) return null;

        // Disabled, benched or offline: the persona falls back to the pool rather than going mute.
        var assigned = Usable(await GetProvidersAsync(cancellationToken), providerId)?.Grain;

        using var span = Tracing.Persona.StartActivity("llm.route.assigned", ActivityKind.Internal);
        span?.SetTag("persona.id", personaId);
        span?.SetTag("job.complexity", tier.ToString());
        span?.SetTag("provider.id", providerId);
        span?.SetTag("decision", assigned is null ? "pool" : "assigned");
        return assigned;
    }

    public async Task<ILlmEndpointGrain> RouteAsync(
        JobComplexity jobComplexity,
        Guid? affinity = null,
        CancellationToken cancellationToken = default)
    {
        using var routeSpan = Tracing.Persona.StartActivity("llm.route", ActivityKind.Internal);
        routeSpan?.SetTag("job.complexity", jobComplexity.ToString());
        routeSpan?.SetTag("affinity", affinity);

        var ranked = Rank(await GetProvidersAsync(cancellationToken), jobComplexity, affinity);
        routeSpan?.SetTag("candidates.total", ranked.Count);
        if (ranked.Count == 0)
        {
            routeSpan?.SetStatus(ActivityStatusCode.Error, "no compatible providers");
            routeSpan?.SetTag("decision", "none");
            throw new InvalidOperationException($"No model-providers available for job complexity {jobComplexity}");
        }

        var picked = ranked[0];
        routeSpan?.SetTag("decision", "selected");
        routeSpan?.SetTag("selected.provider_id", picked.Entry.Id);
        routeSpan?.SetTag("selected.model", picked.Entry.ModelName);
        routeSpan?.SetTag("selected.benched", IsBenched(picked.Entry.Id));
        return picked.Grain;
    }

    public async Task<List<ModelAssignment>> GetAssignmentsAsync(List<Guid> personaIds)
    {
        var providers = await GetProvidersAsync(CancellationToken.None);
        var routes = await GetRoutesAsync();
        return personaIds.Select(id => (Guid?)id).Prepend(null)
            .SelectMany(persona => LlmTiers.All.Select(tier =>
            {
                Guid? pinned = persona is { } id && routes.TryGetValue((id, tier), out var route) ? route : null;
                var lands = pinned is { } pin && Usable(providers, pin) is not null
                    ? pinned
                    : Rank(providers, tier, persona).Select(p => (Guid?)p.Entry.Id).FirstOrDefault();
                return new ModelAssignment(persona ?? Guid.Empty, tier, lands, pinned);
            }))
            .ToList();
    }

    /// <summary>The pool for a tier, best first: healthy, then Priority, then this key's Share order (ADR 0048).</summary>
    private List<(LlmProviderEntry Entry, ILlmEndpointGrain Grain)> Rank(
        IReadOnlyList<(LlmProviderEntry Entry, ILlmEndpointGrain Grain)> providers, JobComplexity tier, Guid? affinity)
        // Offline is known unreachable, so it's left out entirely; the caller's next tier beats a dead box.
        => providers
            .Where(p => !p.Entry.Reserved && !IsOffline(p.Entry.Id) && p.Entry.SupportedComplexities.HasFlag(tier))
            .OrderBy(p => IsBenched(p.Entry.Id))
            .ThenBy(p => p.Entry.Priority)
            .ThenBy(p => Shares.Rank(affinity, p.Entry.Id, p.Entry.Share))
            .ToList();

    private (LlmProviderEntry Entry, ILlmEndpointGrain Grain)? Usable(
        IReadOnlyList<(LlmProviderEntry Entry, ILlmEndpointGrain Grain)> providers, Guid providerId)
        => IsBenched(providerId) || IsOffline(providerId)
            ? null
            : providers.Where(p => p.Entry.Id == providerId).Select(p => ((LlmProviderEntry, ILlmEndpointGrain)?)p).FirstOrDefault();

    private async Task<Dictionary<(Guid Persona, JobComplexity Tier), Guid>> GetRoutesAsync()
        => _cachedRoutes ??= (await GrainFactory.GetGrain<ILlmProviderConfigGrain>(0).GetPersonaRoutesAsync())
            .ToDictionary(r => (r.PersonaId, r.Tier), r => r.ProviderId);

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
