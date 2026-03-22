
using Orleans.Concurrency;
using PartyTown.Grains.Generation;

[Reentrant] // Allows the router to handle multiple requests simultaneously
public class LlmRouterGrain : Grain, ILlmRouterGrain
{
    public Task<IReadOnlyList<LlmModel>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<string> RouteAndGenerateAsync(LlmGenerationParams parameters)
    {
        throw new NotImplementedException();
    }
}