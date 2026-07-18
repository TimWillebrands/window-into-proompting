using Orleans;
using PartyTown.Services.Import;

namespace PartyTown.Services.Streaming;

/// <summary>
/// Internal Orleans stream event for import-session updates (scene run lifecycle).
/// Published by <see cref="ImportRunCoordinator"/>, fanned out by <see cref="PartyTown.Services.Realtime.PartyRealtimeHub"/>.
/// </summary>
[GenerateSerializer, Alias(nameof(ImportStreamEvent))]
public sealed record class ImportStreamEvent
{
    public const string RunStarted = "runStarted";
    public const string RunProgress = "runProgress";
    public const string RunCompleted = "runCompleted";
    public const string RunFailed = "runFailed";

    [Id(0)]
    public required string Type { get; init; }

    [Id(1)]
    public Guid SceneId { get; init; }

    [Id(2)]
    public int CallsDone { get; init; }

    [Id(3)]
    public int TotalCalls { get; init; }

    /// <summary>Which extraction path is running: "canon" or "messages".</summary>
    [Id(4)]
    public string? Stage { get; init; }

    /// <summary>Pre-fold item previews from the call that just finished.</summary>
    [Id(5)]
    public List<ImportRunItemPreview> Items { get; init; } = new();

    /// <summary>Carried on <see cref="RunCompleted"/>: the folded scene result.</summary>
    [Id(6)]
    public SceneRunResult? Result { get; init; }

    [Id(7)]
    public string? Error { get; init; }
}

/// <summary>Slim pre-fold item preview streamed while a scene run is in flight.</summary>
[GenerateSerializer, Alias(nameof(ImportRunItemPreview))]
public sealed record class ImportRunItemPreview
{
    [Id(0)]
    public string? Type { get; init; }

    [Id(1)]
    public string? Persona { get; init; }

    [Id(2)]
    public string Summary { get; init; } = string.Empty;

    [Id(3)]
    public double? Weight { get; init; }
}
