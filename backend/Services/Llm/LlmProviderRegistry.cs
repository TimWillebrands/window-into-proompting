namespace PartyTown.Services.Llm;

public sealed class LlmProviderRegistry(IEnumerable<ILlmProvider> providers) : ILlmProviderRegistry
{
    private readonly Dictionary<string, ILlmProvider> _providers = providers.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<CategorizedModels>> GetAllModelsAsync(CancellationToken cancellationToken = default)
    {
        var tasks = _providers.Values.Select(p => p.GetModelsAsync(cancellationToken));
        var results = await Task.WhenAll(tasks);

        var categorized = new List<CategorizedModels>(_providers.Count);
        for (var i = 0; i < _providers.Count; i++)
        {
            var provider = _providers.ElementAt(i);
            var models = results[i];
            if (models.Count > 0)
            {
                categorized.Add(new CategorizedModels(provider.Key, models));
            }
        }

        return categorized;
    }

    public ILlmProvider GetProvider(string providerId)
    {
        if (!_providers.TryGetValue(providerId, out var provider))
        {
            throw new InvalidOperationException($"Unknown provider: {providerId}");
        }
        return provider;
    }

    public async IAsyncEnumerable<LlmGenerationEvent> GenerateAsync(
        LlmGenerationParams parameters,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var providerId = parameters.Provider.ToLowerInvariant();
        if (!_providers.TryGetValue(providerId, out var provider))
        {
            throw new InvalidOperationException($"Unknown provider: {parameters.Provider}");
        }

        await foreach (var evt in provider.GenerateAsync(parameters, cancellationToken))
        {
            yield return evt;
        }
    }
}
