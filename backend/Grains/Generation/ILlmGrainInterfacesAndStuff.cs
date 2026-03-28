namespace PartyTown.Grains.Generation;

[Alias("PartyTown.Grains.Generation.ILlmEndpointGrain")]
public interface ILlmEndpointGrain : IGrainWithIntegerKey
{
    [Alias("GetModelsAsync")]
    Task<IReadOnlyList<LlmModel>> GetModelsAsync(CancellationToken cancellationToken = default);

    [Alias("GenerateStreamAsync")]
    IAsyncEnumerable<LlmGenerationEvent> GenerateAsync(LlmGenerationParams parameters, CancellationToken cancellationToken = default);
}

[Alias("PartyTown.Grains.Generation.IOllamaEndpointGrain")]
public interface IOllamaEndpointGrain : ILlmEndpointGrain { }

[Alias("PartyTown.Grains.Generation.IOpenRouterEndpointGrain")]
public interface IOpenRouterEndpointGrain : ILlmEndpointGrain { }

[Alias("PartyTown.Grains.Generation.ILlmRouterGrain")]
public interface ILlmRouterGrain : IGrainWithIntegerKey
{
    [Alias("RouteAndGenerateAsync")]
    Task<string> RouteAndGenerateAsync(LlmGenerationParams parameters);

    [Alias("GetModelsAsync")]
    Task<IReadOnlyList<LlmModel>> GetModelsAsync(CancellationToken cancellationToken = default);
}

[GenerateSerializer]
public readonly record struct LlmGenerationEvent(
    [property: Id(0)] string Type,
    [property: Id(1)] string Data);

[GenerateSerializer]
public sealed record class LlmChatMessage
{
    [Id(0)] public string Role { get; init; } = string.Empty;
    [Id(1)] public string Content { get; init; } = string.Empty;
    [Id(2)] public string? Name { get; init; }
}

[GenerateSerializer]
public sealed record class LlmGenerationParams
{
    [Id(0)] public string Model { get; init; } = string.Empty;
    [Id(1)] public IReadOnlyList<LlmChatMessage> Messages { get; init; } = [];
    [Id(2)] public string? UserId { get; init; }
    [Id(3)] public string? RoomId { get; init; }
    [Id(4)] public double? Temperature { get; init; }
    // Serialized JSON string (JsonObject is not Orleans-serializable)
    [Id(5)] public string? ResponseFormat { get; init; }
}

[GenerateSerializer]
public sealed record class LlmModel
{
    [Id(0)] public required string Name { get; init; }
    [Id(1)] public required int EndpointProviderGrainId { get; init; }
    [Id(2)] public required string ProviderType { get; init; }
    [Id(3)] public string? ProviderDescription { get; init; }
    [Id(4)] public string? Description { get; init; }
    [Id(5)] public int? ContextLength { get; init; }
}

public static class LlmEndpointGrainFactory
{
    public static ILlmEndpointGrain GetGrain(IGrainFactory grainFactory, LlmModel model) =>
        model.ProviderType switch
        {
            "ollama" => grainFactory.GetGrain<IOllamaEndpointGrain>(model.EndpointProviderGrainId),
            "openrouter" => grainFactory.GetGrain<IOpenRouterEndpointGrain>(model.EndpointProviderGrainId),
            _ => throw new NotSupportedException($"Unknown provider type: {model.ProviderType}")
        };
}
