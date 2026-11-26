using PartyTown.Model;

namespace PartyTown.Services.Memory;

public sealed record ConceptTag(string Name, string Display);

public sealed record EventExtraction(
    string Description,
    IReadOnlyList<ConceptTag> Concepts,
    IReadOnlyList<Guid> ParticipantIds);

public sealed record RecollectionTarget(Guid PersonaId, string Name, bool IsSpeaker);

public sealed record RecollectionDraft(string Snippet, double Weight);

public enum RecallArm
{
    Recent,

    Relevant,
}

public sealed record RecalledMemory(
    Guid EdgeId,
    string Snippet,
    double Weight,
    int RecallCount,
    DateTimeOffset? LastRecalled,
    DateTimeOffset CapturedAt,
    double Salience,
    RecallArm Arm = RecallArm.Recent);

public sealed record MemoryCaptureResult(
    bool EventCreated,
    int RecollectionsCreated,
    int ConceptsTouched)
{
    public IReadOnlyList<Guid> RecollectionPersonaIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<string> Concepts { get; init; } = Array.Empty<string>();
}

public sealed record MemoryGraphDto(
    IReadOnlyList<MemoryGraphNode> Nodes,
    IReadOnlyList<MemoryGraphLink> Links);

public sealed record MemoryGraphNode(
    string Id,
    string Kind,
    string? Description = null,
    string? Display = null,
    string? CreatedAt = null,
    string? RoomId = null,
    int? AnchorMessageId = null,
    string? Source = null,
    string? PlaceId = null);

public sealed record MemoryGraphLink(
    string Source,
    string Target,
    string Kind,
    string? Snippet = null,
    string? Ts = null,
    double? Weight = null,
    int? RecallCount = null,
    string? LastRecalled = null,
    double? Valence = null,
    string? Reasoning = null);

public sealed record Moment(
    Guid PartyId,
    string Source,
    Guid ThreadId,
    IReadOnlyList<MomentLine> Lines,
    int MarkIndex,
    IReadOnlyList<ParticipantView> Present,
    Guid? PlaceId = null)
{
    public static Moment FromMessages(
        Guid partyId, string source, Guid threadId, IReadOnlyList<ChatMessage> messages, int markMessageId,
        IReadOnlyList<ParticipantView> present, Guid? placeId = null)
    {
        var ordered = messages.OrderBy(m => m.MessageId).ToList();
        var mark = ordered.FindIndex(m => m.MessageId == markMessageId);
        if (mark < 0)
            throw new KeyNotFoundException($"Message {markMessageId} not in the moment's lines (thread {threadId}).");
        return new Moment(partyId, source, threadId,
            ordered.Select(m => new MomentLine(m.SenderId, m.Content ?? string.Empty, m.SendAt, m.MessageId)).ToList(),
            mark, present, placeId);
    }
}

public sealed record MomentLine(Guid SenderId, string Text, long? At = null, int? MessageId = null);

public static class MomentSources
{
    public const string Rooms = "rooms";
    public const string Messenger = "messenger";
    public const string Places = "places";
    public const string Board = "board";
    public const string World = "world";
    public const string Import = "import";
    public const string Work = "work";
    public const string Stock = "stock";

    public static string OfKind(string? chatGroupKind) => chatGroupKind switch
    {
        ChatGroupKinds.Direct => Messenger,
        ChatGroupKinds.Place => Places,
        _ => Rooms,
    };
}
