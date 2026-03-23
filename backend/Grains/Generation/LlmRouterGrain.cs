using System.Text;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;
using PartyTown.Configuration;

namespace PartyTown.Grains.Generation;

[Reentrant]
public sealed class LlmRouterGrain(
    IOptions<LlmOptions> llmOptions,
    ILogger<LlmRouterGrain> logger
) : Grain, ILlmRouterGrain
{
    public async Task<IReadOnlyList<LlmModel>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var providers = llmOptions.Value.Providers;
        var tasks = new List<Task<IReadOnlyList<LlmModel>>>();

        var ollamaCount = providers.Count(p => p.Type == "ollama");
        var openRouterCount = providers.Count(p => p.Type == "openrouter");

        for (var i = 0; i < ollamaCount; i++)
            tasks.Add(GrainFactory.GetGrain<IOllamaEndpointGrain>(i).GetModelsAsync(cancellationToken));
        for (var i = 0; i < openRouterCount; i++)
            tasks.Add(GrainFactory.GetGrain<IOpenRouterEndpointGrain>(i).GetModelsAsync(cancellationToken));

        var results = await Task.WhenAll(tasks);
        return results.SelectMany(x => x).ToList();
    }

    public async Task<string> RouteAndGenerateAsync(LlmGenerationParams parameters)
    {
        var models = await GetModelsAsync();
        var model = models.FirstOrDefault(m => m.Name == parameters.Model)
            ?? throw new InvalidOperationException($"No endpoint grain found for model '{parameters.Model}'");

        logger.LogInformation("Routing generation for model {Model} to {ProviderType}[{GrainId}]",
            parameters.Model, model.ProviderType, model.EndpointProviderGrainId);

        var endpointGrain = LlmEndpointGrainFactory.GetGrain(GrainFactory, model);
        var sb = new StringBuilder();

        await foreach (var evt in endpointGrain.GenerateAsync(parameters))
        {
            if (evt.Type == "message")
                sb.Append(evt.Data);
        }

        return sb.ToString();
    }
}
