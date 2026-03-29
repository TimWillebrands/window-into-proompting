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
            .Select(grain => grain.GetModelsAsync(cancellationToken));
        var results = await Task.WhenAll(tasks);
        return [.. results.SelectMany(model => model)];
    }

    public async Task<IAsyncEnumerable<LlmGenerationEvent>> RouteAndGenerateAsync(
        LlmGenerationJob job,
        CancellationToken cancellationToken = default)
    {
        var modelProviderTasks = ModelProviders.Select(async grain =>
        {
            var models = await grain.GetModelsAsync();
            return new
            {
                Grain = grain,
                Pressure = await grain.PressureAsync(),
                CompatibleModels = models.Where(m => m.Supports(job.JobComplexity))
            };
        });

        var compatibleProvider = (await Task.WhenAll(modelProviderTasks))
            .Where(provider => provider.CompatibleModels.Any())
            .OrderBy(provider => provider.Pressure)
            .FirstOrDefault();

        return compatibleProvider is null
            ? throw new InvalidOperationException($"No model-providers available for job complexity {job.JobComplexity}")
            : compatibleProvider.Grain.GenerateAsync(job, cancellationToken);
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
