using System.Text.Json.Serialization;
using PartyTown.Model;

namespace PartyTown.Services.Realtime;

/// <summary>
/// Outgoing realtime envelope sent over websocket to clients.
/// </summary>
public sealed record class PartyRealtimeEnvelope
{
    /// <summary>Event type identifier (e.g., "party.snapshot", "party.message.created").</summary>
    public required string Type { get; init; }

    /// <summary>Monotonically increasing sequence number per party hub session.</summary>
    public required long Sequence { get; init; }

    /// <summary>Unix timestamp in milliseconds when the envelope was created.</summary>
    public required long Timestamp { get; init; }

    /// <summary>Typed payload for this event type.</summary>
    public required IPartyRealtimeData Data { get; init; }
}

/// <summary>
/// Marker interface for typed envelope payloads.
/// </summary>
[JsonDerivedType(typeof(PartySnapshotData), "party_snapshot")]
[JsonDerivedType(typeof(PartyMessageCreatedData), "party_message_created")]
[JsonDerivedType(typeof(PartyMessageDeletedData), "party_message_deleted")]
[JsonDerivedType(typeof(PartyMessagesTruncatedData), "party_messages_truncated")]
[JsonDerivedType(typeof(PartyGenerationStartedData), "party_generation_started")]
[JsonDerivedType(typeof(PartyGenerationDeltaData), "party_generation_delta")]
[JsonDerivedType(typeof(PartyGenerationCompletedData), "party_generation_completed")]
[JsonDerivedType(typeof(PartyPongData), "party_pong")]
[JsonDerivedType(typeof(PartyUnknownEventData), "party_unknown")]
[JsonDerivedType(typeof(PersonaGenerationDeltaData), "persona_generation_delta")]
[JsonDerivedType(typeof(PersonaGenerationCompletedData), "persona_generation_completed")]
[JsonDerivedType(typeof(PersonaGenerationErrorData), "persona_generation_error")]
[JsonDerivedType(typeof(PartyRaceEvaluationData), "party_race_evaluation")]
public interface IPartyRealtimeData;

/// <summary>Payload for the "party.snapshot" event: initial backlog on connect.
/// Includes messages and the two thought-log auxiliary streams (skipped turns +
/// race evaluations) so a page reload rehydrates the full thought log instead of
/// dropping every "negative" decision the moment the websocket reconnects.</summary>
public sealed record class PartySnapshotData(
    Guid PartyId,
    Guid ChatGroupId,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<PartyTown.Grains.SkippedTurn> SkippedTurns,
    IReadOnlyList<PartyTown.Grains.RaceEvaluation> RaceEvaluations) : IPartyRealtimeData;

/// <summary>Payload for the "party.message.created" event: a new message was added to the party.</summary>
public sealed record class PartyMessageCreatedData(Guid PartyId, Guid ChatGroupId, ChatMessage? Message) : IPartyRealtimeData;

/// <summary>Payload for the "party.message.deleted" event: a single message was removed.</summary>
public sealed record class PartyMessageDeletedData(Guid PartyId, Guid ChatGroupId, int? MessageId) : IPartyRealtimeData;

/// <summary>Payload for the "party.messages.truncated" event: all messages after a point were removed.</summary>
public sealed record class PartyMessagesTruncatedData(Guid PartyId, Guid ChatGroupId, int? MessageId) : IPartyRealtimeData;

/// <summary>Payload for the "party.generation.started" event: a new message generation has begun.</summary>
public sealed record class PartyGenerationStartedData(Guid PartyId, Guid ChatGroupId, int MessageId) : IPartyRealtimeData;

/// <summary>Payload for the "party.generation.delta" event: token/reasoning chunk during generation.</summary>
public sealed record class PartyGenerationDeltaData(Guid PartyId, Guid ChatGroupId, int MessageId, string Event, string? Data, bool Done) : IPartyRealtimeData;

/// <summary>Payload for the "party.generation.completed" event: generation finished (success or failure).</summary>
public sealed record class PartyGenerationCompletedData(Guid PartyId, Guid ChatGroupId, int MessageId) : IPartyRealtimeData;

/// <summary>Payload for the "party.pong" event: server response to client ping.</summary>
public sealed record class PartyPongData(Guid PartyId, Guid ChatGroupId) : IPartyRealtimeData;

/// <summary>Payload for unhandled or unknown event types.</summary>
public sealed record class PartyUnknownEventData(Guid PartyId, Guid ChatGroupId, string EventType, ChatMessage? Message, int? MessageId) : IPartyRealtimeData;

/// <summary>Payload for the "persona.generation.delta" event: a streamed text chunk from persona generation.</summary>
public sealed record class PersonaGenerationDeltaData(string Data) : IPartyRealtimeData;

/// <summary>Payload for the "persona.generation.completed" event: generation finished with optional parsed fields.</summary>
public sealed record class PersonaGenerationCompletedData(string? Name, string? SystemPrompt, string? Bio) : IPartyRealtimeData;

/// <summary>Payload for the "persona.generation.error" event.</summary>
public sealed record class PersonaGenerationErrorData(string Message) : IPartyRealtimeData;

/// <summary>Payload for the "party.race.evaluation" event: one stop-signal race outcome
/// from <c>PersonaGrain.RunStopSignalRaceAsync</c>, surfaced live to the thought log.</summary>
public sealed record class PartyRaceEvaluationData(Guid PartyId, Guid ChatGroupId, PartyTown.Grains.RaceEvaluation Evaluation) : IPartyRealtimeData;
