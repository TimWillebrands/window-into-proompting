using Orleans;
using PartyTown.Model;

namespace PartyTown.Services.Streaming;

/// <summary>
/// Internal Orleans stream event for party-level updates.
/// Transforms grain state changes into realtime envelope payloads via <see cref="PartyRealtimeHub"/>.
/// </summary>
[GenerateSerializer, Alias(nameof(PartyStreamEvent))]
public sealed record class PartyStreamEvent
{
    [Id(0)]
    public required string Type { get; init; }

    [Id(1)]
    public ChatMessage? Message { get; init; }

    [Id(2)]
    public int? MessageId { get; init; }

    [Id(3)]
    public Guid ChatGroupId { get; init; }

    [Id(4)]
    public MessageStreamEvent? MessageEvent { get; init; }
}

/// <summary>
/// Internal Orleans stream event for per-message generation updates (token chunks, reasoning, completion).
/// Fanned out to clients subscribed to a specific message's generation lifecycle.
/// </summary>
[GenerateSerializer, Alias(nameof(MessageStreamEvent))]
public sealed record class MessageStreamEvent
{
    public const string PersonaEvaluatingResponse = "attend";
    public const string PersonaDeclinedResponse = "declined";
    public const string PersonaEvaluationStreaming = "appraisal";
    public const string PersonaEvaluationComplete = "appraisalComplete";
    public const string GenerationError = "error";
    public const string ContentChunk = "message";
    public const string ReasoningChunk = "reasoning";
    public const string GenerationComplete = "finished";

    [Id(0)]
    public required string Event { get; init; }

    [Id(1)]
    public required string Data { get; init; }

    [Id(2)]
    public required bool Done { get; init; }

    [Id(3)]
    public required Guid ChatGroupId { get; init; }
}
