using Microsoft.Extensions.Options;
using Orleans.Concurrency;
using PartyTown.Configuration;

namespace PartyTown.Grains.Generation;

[Reentrant]
public sealed class LlmRouterGrain(IOptions<LlmOptions> llmOptions) : Grain, ILlmRouterGrain
{
    private IReadOnlyList<ILlmEndpointGrain> ModelProviders => field ??= FetchModelProviders();

    public async Task<IReadOnlyList<LlmModel>> GetModelsAsync(
        CancellationToken cancellationToken = default)
    {
        var tasks = ModelProviders
            .Select(async grain =>
            {
                try { return await grain.GetModelsAsync(cancellationToken); }
                catch { return []; }
            });
        var results = await Task.WhenAll(tasks);
        return [.. results.SelectMany(model => model)];
    }


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
            catch
            {
                return new
                {
                    Grain = grain,
                    Pressure = double.MaxValue,
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
