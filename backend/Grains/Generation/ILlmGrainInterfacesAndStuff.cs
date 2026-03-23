using System.Text.Json.Nodes;

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

public readonly record struct LlmGenerationEvent(string Type, string Data);

public sealed record class LlmChatMessage
{
    public string Role { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string? Name { get; init; }
}

public sealed record class LlmGenerationParams
{
    public string Model { get; init; } = string.Empty;
    public IReadOnlyList<LlmChatMessage> Messages { get; init; } = [];
    public string? UserId { get; init; }
    public string? RoomId { get; init; }
    public double? Temperature { get; init; }
    public JsonObject? ResponseFormat { get; init; }
}

public sealed record class LlmModel
{
    public required string Name { get; init; }
    public required int EndpointProviderGrainId { get; init; }
    public required string ProviderType { get; init; }
    public string? ProviderDescription { get; init; }
    public string? Description { get; init; }
    public int? ContextLength { get; init; }
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
