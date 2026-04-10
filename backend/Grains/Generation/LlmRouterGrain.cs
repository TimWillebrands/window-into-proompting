using Microsoft.Extensions.Options;
using Orleans.Concurrency;
using PartyTown.Configuration;

namespace PartyTown.Grains.Generation;

[Reentrant]
public sealed class LlmRouterGrain(IOptions<LlmOptions> llmOptions) : Grain, ILlmRouterGrain
{
    private IReadOnlyList<ILlmEndpointGrain> ModelProviders => field ??= FetchModelProviders();

    /// <summary>
    /// Collects models from all configured model-provider endpoint grains and returns them as a single list.
    /// </summary>
    /// <returns>A read-only list of discovered <see cref="LlmModel"/> instances aggregated from all providers; individual provider failures (other than cancellation) are treated as contributing no models.</returns>
    public async Task<IReadOnlyList<LlmModel>> GetModelsAsync(
        CancellationToken cancellationToken = default)
    {
        var tasks = ModelProviders
            .Select(async grain =>
            {
                try { return await grain.GetModelsAsync(cancellationToken); }
                catch (Exception ex) when (ex is not OperationCanceledException and not TaskCanceledException)
                { return []; }
            });
        var results = await Task.WhenAll(tasks);
        return [.. results.SelectMany(model => model)];
    }


    /// <summary>
    /// Selects the best endpoint grain capable of handling a job with the specified complexity.
    /// </summary>
    /// <param name="jobComplexity">Job complexity used to filter models supported by providers.</param>
    /// <param name="cancellationToken">Token to observe while querying providers.</param>
    /// <returns>The chosen <see cref="ILlmEndpointGrain"/> whose models support the requested complexity.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no providers have compatible models for the specified <paramref name="jobComplexity"/>.</exception>
    public async Task<ILlmEndpointGrain> RouteAsync(
        JobComplexity jobComplexity,
        CancellationToken cancellationToken = default)
    {
        var modelProviderTasks = ModelProviders.Select(async grain =>
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

    /// <summary>
    /// Builds a list of endpoint grains corresponding to the configured LLM providers, preserving the order from <c>llmOptions.Value.Providers</c>.
    /// Each provider entry is mapped to its corresponding <see cref="ILlmEndpointGrain"/> using the provider's index as the grain key.
    /// </summary>
    /// <returns>
    /// An <see cref="IReadOnlyList{T}"/> of <see cref="ILlmEndpointGrain"/> instances aligned with the configured providers.
    /// The list order matches the configuration order; elements may be null if a provider mapping yields a null reference.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown when a configured provider type is not supported.</exception>
    private IReadOnlyList<ILlmEndpointGrain> FetchModelProviders()
    {
        var models = llmOptions.Value.Providers.Select((providerConfig, i) =>
        {
            return providerConfig switch
            {
                OllamaProviderConfig =>
                    GrainFactory.GetGrain<IOllamaEndpointGrain>(i) as ILlmEndpointGrain,
                OpenRouterProviderConfig =>
                    GrainFactory.GetGrain<IOpenRouterEndpointGrain>(i),
                _ => throw new InvalidOperationException($"Unsupported provider type: {providerConfig.GetType().Name}"),
            };
        });

        return [.. models];
    }
}
