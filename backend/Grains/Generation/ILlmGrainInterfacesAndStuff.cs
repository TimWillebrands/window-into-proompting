using Orleans.Concurrency;
using PartyTown.Model;

namespace PartyTown.Grains.Generation;

[Alias("PartyTown.Grains.Generation.ILlmEndpointGrain")]
public interface ILlmEndpointGrain : IGrainWithGuidKey
{
    [Alias("GetModelsAsync")]
    Task<IReadOnlyList<LlmModel>> GetModelsAsync(CancellationToken cancellationToken = default);

    [Alias("GenerateStreamAsync")]
    IAsyncEnumerable<LlmGenerationEvent> GenerateAsync(LlmGenerationJob parameters, CancellationToken cancellationToken = default);

    [Alias("CompleteOneShotAsync")]
    Task<string> CompleteOneShotAsync(LlmGenerationJob parameters, CancellationToken cancellationToken = default);

    [AlwaysInterleave]
    [Alias("Pressure")]
    ValueTask<int> PressureAsync();

    [AlwaysInterleave]
    [Alias("JudgeAsync")]
    Task<string> JudgeAsync(string state, string questionsJson, CancellationToken cancellationToken = default);

    [AlwaysInterleave]
    [Alias("PingAsync")]
    Task<string?> PingAsync(CancellationToken cancellationToken = default);

    [Alias("GetAttributionAsync")]
    Task<ChatMessageMetadata> GetAttributionAsync();

    [Alias("ApplyConfigAsync")]
    Task ApplyConfigAsync(LlmProviderEntry? entry);
}

public static class LlmEndpoints
{
    public static ILlmEndpointGrain Resolve(IGrainFactory grains, string type, Guid id) => type switch
    {
        "ollama" => grains.GetGrain<IOllamaEndpointGrain>(id),
        "openrouter" => grains.GetGrain<IOpenRouterEndpointGrain>(id),
        "anthropic" => grains.GetGrain<IAnthropicEndpointGrain>(id),
        _ => throw new InvalidOperationException($"Unsupported provider type: {type}"),
    };
}

[Alias("PartyTown.Grains.Generation.IOllamaEndpointGrain")]
public interface IOllamaEndpointGrain : ILlmEndpointGrain { }

[Alias("PartyTown.Grains.Generation.IAnthropicEndpointGrain")]
public interface IAnthropicEndpointGrain : ILlmEndpointGrain { }

[Alias("PartyTown.Grains.Generation.IOpenRouterEndpointGrain")]
public interface IOpenRouterEndpointGrain : ILlmEndpointGrain { }

[Alias("PartyTown.Grains.Generation.ILlmRouterGrain")]
public interface ILlmRouterGrain : IGrainWithIntegerKey
{
    [Alias("RouteAsync")]
    Task<ILlmEndpointGrain> RouteAsync(
        JobComplexity jobComplexity,
        Guid? affinity = null,
        CancellationToken cancellationToken = default);

    [Alias("AssignedEndpointAsync")]
    Task<ILlmEndpointGrain?> AssignedEndpointAsync(
        Guid personaId,
        JobComplexity tier,
        CancellationToken cancellationToken = default);

    [Alias("GetModelsAsync")]
    Task<IReadOnlyList<LlmModel>> GetModelsAsync(CancellationToken cancellationToken = default);

    [Alias("InvalidateCacheAsync")]
    Task InvalidateCacheAsync();

    [Alias("ReportFailureAsync")]
    Task ReportFailureAsync(Guid providerId, string reason);

    [Alias("ReportOfflineAsync")]
    Task ReportOfflineAsync(Guid providerId, string reason);

    [Alias("CheckAvailabilityAsync")]
    Task CheckAvailabilityAsync(CancellationToken cancellationToken = default);

    [Alias("GetAvailabilityAsync")]
    Task<List<ProviderAvailability>> GetAvailabilityAsync();

    [Alias("GetAssignmentsAsync")]
    Task<List<ModelAssignment>> GetAssignmentsAsync(List<Guid> personaIds);
}

[GenerateSerializer]
[Alias("PartyTown.Grains.Generation.ModelAssignment")]
public sealed record ModelAssignment(
    [property: Id(0)] Guid PersonaId,
    [property: Id(1)] JobComplexity Tier,
    [property: Id(2)] Guid? ProviderId,
    [property: Id(3)] Guid? PinnedTo,
    [property: Id(4)] bool Balanced);

[GenerateSerializer]
[Alias("PartyTown.Grains.Generation.ProviderState")]
public enum ProviderState
{
    Ready = 0,
    Benched = 1,
    Offline = 2,
}

[GenerateSerializer]
[Alias("PartyTown.Grains.Generation.ProviderAvailability")]
public sealed record ProviderAvailability(
    [property: Id(0)] Guid ProviderId,
    [property: Id(1)] ProviderState State,
    [property: Id(2)] string? Reason,
    [property: Id(3)] DateTimeOffset? Until);

[GenerateSerializer]
[Alias("PartyTown.Grains.Generation.LlmGenerationEvent")]
public readonly record struct LlmGenerationEvent(
    [property: Id(0)] string Type,
    [property: Id(1)] string Data)
{
    public const string ContentChunk = "message";
    public const string ReasoningChunk = "reasoning";
    public const string GenerationError = "error";
}

[GenerateSerializer]
[Alias("PartyTown.Grains.Generation.LlmChatMessage")]
public sealed record class LlmChatMessage
{
    [Id(0)] public string Role { get; init; } = string.Empty;
    [Id(1)] public string Content { get; init; } = string.Empty;
    [Id(2)] public string? Name { get; init; }
}

[GenerateSerializer]
[Alias("PartyTown.Grains.Generation.LlmGenerationJob")]
public sealed record class LlmGenerationJob
{
    [Id(0)] public IReadOnlyList<LlmChatMessage> Messages { get; init; } = Array.Empty<LlmChatMessage>();
    [Id(1)] public JobComplexity JobComplexity { get; init; }
    [Id(2)] public LlmModelParameters? ModelParameters { get; init; }
    [Id(3)] public Dictionary<string, string>? JobData { get; init; }
    [Id(4)] public string? ResponseFormat { get; init; }
}

[Flags]
public enum JobComplexity
{
    General = 1,

    CharacterVoice = 2,

    CharacterThoughts = 4,

    Decisions = 8,
}

[GenerateSerializer]
[Alias("PartyTown.Grains.Generation.LlmModelParameters")]
public sealed record class LlmModelParameters
{
    [Id(0)] public double? Temperature { get; init; }
    [Id(1)] public string? ResponseFormat { get; init; }
}

[GenerateSerializer]
[Alias("PartyTown.Grains.Generation.LlmModel")]
public sealed record class LlmModel
{
    [Id(0)] public required string Name { get; init; }
    [Id(1)] public required Guid EndpointProviderGrainId { get; init; }
    [Id(2)] public required string ProviderType { get; init; }
    [Id(3)] public string? ProviderDescription { get; init; }
    [Id(4)] public string? Description { get; init; }
    [Id(5)] public int? ContextLength { get; init; }
    [Id(6)] public JobComplexity SupportedComplexities { get; init; }

    internal bool Supports(JobComplexity jobComplexity)
        => SupportedComplexities.HasFlag(jobComplexity);
}
