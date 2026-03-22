namespace PartyTown.Services.Llm;

public interface ILlmProvider
{
    string Id { get; }
    Task<IReadOnlyList<LlmModel>> GetModelsAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<LlmGenerationEvent> GenerateAsync(LlmGenerationParams parameters, CancellationToken cancellationToken = default);
}

public sealed record class CategorizedModels(string Provider, IReadOnlyList<LlmModel> Models);

public interface ILlmProviderRegistry
{
    Task<IReadOnlyList<CategorizedModels>> GetAllModelsAsync(CancellationToken cancellationToken = default);
    ILlmProvider GetProvider(string providerId);
    IAsyncEnumerable<LlmGenerationEvent> GenerateAsync(LlmGenerationParams parameters, CancellationToken cancellationToken = default);
}