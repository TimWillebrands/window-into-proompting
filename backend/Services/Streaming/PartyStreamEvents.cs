using Orleans;
using PartyTown.Model;

namespace PartyTown.Services.Streaming;

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

    [Id(5)]
    public PartyTown.Grains.RaceEvaluation? RaceEvaluation { get; init; }

    [Id(6)]
    public string? WorldKind { get; init; }

    [Id(7)]
    public string? WorldJson { get; init; }

    [Id(8)]
    public PartyTown.Grains.SkippedTurn? SkippedTurn { get; init; }

    [Id(9)]
    public ChatGroupInfo? Conversation { get; init; }
}

[GenerateSerializer, Alias(nameof(MessageStreamEvent))]
public sealed record class MessageStreamEvent
{
    public const string PersonaEvaluatingResponse = "attend";
    public const string PersonaDeclinedResponse = "declined";
    public const string PersonaEvaluationStreaming = "appraisal";
    public const string PersonaEvaluationComplete = "appraisalComplete";
    public const string GenerationError = "error";
    public const string GenerationRetry = "retry";
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
