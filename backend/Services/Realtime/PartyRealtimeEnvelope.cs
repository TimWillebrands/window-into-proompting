using System.Text.Json.Serialization;
using PartyTown.Model;

namespace PartyTown.Services.Realtime;

public sealed record class PartyRealtimeEnvelope
{
    public required string Type { get; init; }

    public required long Sequence { get; init; }

    public required long Timestamp { get; init; }

    public required IPartyRealtimeData Data { get; init; }
}

public interface IRoomFrame : IPartyRealtimeData
{
    Guid ChatGroupId { get; }
}

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
[JsonDerivedType(typeof(PartySkippedTurnData), "party_skipped_turn")]
[JsonDerivedType(typeof(PartyWorldEventData), "party_world_event")]
[JsonDerivedType(typeof(PartyConversationData), "party_conversation")]
public interface IPartyRealtimeData;

public sealed record class PartySnapshotData(
    Guid PartyId,
    Guid ChatGroupId,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<PartyTown.Grains.SkippedTurn> SkippedTurns,
    IReadOnlyList<PartyTown.Grains.RaceEvaluation> RaceEvaluations) : IRoomFrame;

public sealed record class PartyMessageCreatedData(Guid PartyId, Guid ChatGroupId, ChatMessage? Message) : IRoomFrame;

public sealed record class PartyMessageDeletedData(Guid PartyId, Guid ChatGroupId, int? MessageId) : IRoomFrame;

public sealed record class PartyMessagesTruncatedData(Guid PartyId, Guid ChatGroupId, int? MessageId) : IRoomFrame;

public sealed record class PartyGenerationStartedData(Guid PartyId, Guid ChatGroupId, int MessageId) : IRoomFrame;

public sealed record class PartyGenerationDeltaData(Guid PartyId, Guid ChatGroupId, int MessageId, string Event, string? Data, bool Done) : IRoomFrame;

public sealed record class PartyGenerationCompletedData(Guid PartyId, Guid ChatGroupId, int MessageId) : IRoomFrame;

public sealed record class PartyPongData(Guid PartyId, Guid ChatGroupId) : IPartyRealtimeData;

public sealed record class PartyUnknownEventData(Guid PartyId, Guid ChatGroupId, string EventType, ChatMessage? Message, int? MessageId) : IRoomFrame;

public sealed record class PersonaGenerationDeltaData(string Data) : IPartyRealtimeData;

public sealed record class PersonaGenerationCompletedData(string? Name, string? SystemPrompt, string? Bio, double? Chattiness = null, double? Impulsivity = null) : IPartyRealtimeData;

public sealed record class PersonaGenerationErrorData(string Message) : IPartyRealtimeData;

public sealed record class PartyRaceEvaluationData(Guid PartyId, Guid ChatGroupId, PartyTown.Grains.RaceEvaluation Evaluation) : IRoomFrame;

public sealed record class PartySkippedTurnData(Guid PartyId, Guid ChatGroupId, PartyTown.Grains.SkippedTurn Skipped) : IRoomFrame;

public sealed record class PartyConversationData(Guid PartyId, Guid ChatGroupId, ChatGroupInfo? Conversation) : IRoomFrame;

public sealed record class PartyWorldEventData(Guid PartyId, string Kind, System.Text.Json.JsonElement Payload) : IPartyRealtimeData;






