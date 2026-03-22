using System.Text.Json.Nodes;
using PartyTown.Services.Llm;

namespace PartyTown.Grains.Generation;

[Alias("PartyTown.Grains.Generation.ILlmEndpointGrain")]
public interface ILlmEndpointGrain : IGrainWithIntegerKey
{
    [Alias("GetModelsAsync")]
    Task<IReadOnlyList<LlmModel>> GetModelsAsync(CancellationToken cancellationToken = default);

    [Alias("GenerateStreamAsync")]
    IAsyncEnumerable<LlmGenerationEvent> GenerateAsync(LlmGenerationParams parameters, CancellationToken cancellationToken = default);
}

[Alias("PartyTown.Grains.Generation.ILlmRouterGrain")]
public interface ILlmRouterGrain : IGrainWithIntegerKey
{
    /// <summary>
    /// Routes the request to an appropriate endpoint grain and forwards the response.
    /// </summary>
    /// <param name="parameters">The parameters for the generation request.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Alias("RouteAndGenerateAsync")]
    Task<string> RouteAndGenerateAsync(LlmGenerationParams parameters);

    /// <summary>
    /// Retrieves a list of all available models of all the various LlmEndpoints.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
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
    public string? ProviderDescription { get; init; }
    public string? Description { get; init; }
    public int? ContextLength { get; init; }
}
