using PartyTown.Model;

namespace PartyTown.Services.Memory;

/// <summary>
/// Concept tag produced by the event-describer extractor. <see cref="Name"/> is the
/// normalised form used for <c>MERGE</c> deduplication; <see cref="Display"/> preserves
/// the label as the LLM first surfaced it for UI rendering.
/// </summary>
public sealed record ConceptTag(string Name, string Display);

/// <summary>
/// Output of the neutral event-describer + tag extractor pass.
/// <see cref="Description"/> is a third-person factual summary; <see cref="Concepts"/>
/// and <see cref="ParticipantIds"/> are the entities this Event is *about* (objective,
/// shared across all Recollections of it).
/// </summary>
public sealed record EventExtraction(
    string Description,
    IReadOnlyList<ConceptTag> Concepts,
    IReadOnlyList<Guid> ParticipantIds);

/// <summary>
/// One target for batch Recollection extraction. <see cref="IsSpeaker"/> selects the
/// first-person ("you said…") vs observer ("you saw…") framing for this Participant.
/// </summary>
public sealed record RecollectionTarget(Guid PersonaId, string Name, bool IsSpeaker);

/// <summary>
/// One Recollection as the extractor produced it: the snippet plus the salience
/// <see cref="Weight"/> (0..1) emitted in the same LLM call — "how much would this stick,
/// a day later?" (ADR 0015). Weight ≈ 0 plays the old binary NONE role: the extractor
/// declines by omission, not by a sentinel string.
/// </summary>
public sealed record RecollectionDraft(string Snippet, double Weight);

/// <summary>
/// Which recall arm surfaced a memory (ADR 0015 two-arm quota union). Structural
/// opponent processing: the arms stay separate — no merged cross-arm score.
/// </summary>
public enum RecallArm
{
    /// <summary>Recency pool, salience-ranked — the ADR 0009 lineage.</summary>
    Recent,

    /// <summary>
    /// Graph walk from the beat's anchors (present cast Participants + Concepts named in
    /// the triggering message) via <c>←ABOUT← Event ←RECOLLECTS</c>.
    /// </summary>
    Relevant,
}

/// <summary>
/// One salience-ranked Recollection surfaced by Recall (ADR 0015). <see cref="EdgeId"/>
/// is the RECOLLECTS edge's stable identity — the key for the strengthening write when
/// the Decision phase picks this memory. <see cref="Guid.Empty"/> marks an edge captured
/// before the salience substrate existed (surfaceable, not strengthenable).
/// <see cref="Arm"/> records which arm's slot this memory consumed — observability only;
/// the prompt block stays undifferentiated.
/// </summary>
public sealed record RecalledMemory(
    Guid EdgeId,
    string Snippet,
    double Weight,
    int RecallCount,
    DateTimeOffset? LastRecalled,
    DateTimeOffset CapturedAt,
    double Salience,
    RecallArm Arm = RecallArm.Recent);

/// <summary>
/// Outcome of a single <see cref="IMemoryRepository.CaptureAsync"/> call.
/// <see cref="RecollectionPersonaIds"/> names the Participants whose unconsolidated pile
/// just grew — the set the capture path's Consolidation gauge check is scoped to.
/// </summary>
public sealed record MemoryCaptureResult(
    bool EventCreated,
    int RecollectionsCreated,
    int ConceptsTouched)
{
    public IReadOnlyList<Guid> RecollectionPersonaIds { get; init; } = Array.Empty<Guid>();
    /// <summary>The Event's Concept tags (display labels), for consumers outside memory (ADR 0027 footfall).</summary>
    public IReadOnlyList<string> Concepts { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Per-Party memory subgraph payload for the debug viz. Backend emits only what AGE
/// stores; display-name enrichment for Personas / Rooms is client-side.
/// </summary>
public sealed record MemoryGraphDto(
    IReadOnlyList<MemoryGraphNode> Nodes,
    IReadOnlyList<MemoryGraphLink> Links);

/// <summary>
/// <see cref="Id"/> is a kind-prefixed stable string (e.g. <c>event:&lt;guid&gt;</c>,
/// <c>concept:&lt;name&gt;</c>, <c>part:&lt;persona&gt;:&lt;party&gt;</c>) used as the join key
/// on both ends of every <see cref="MemoryGraphLink"/>. <see cref="RoomId"/>,
/// <see cref="AnchorMessageId"/>, <see cref="Source"/> and <see cref="PlaceId"/> are Event-only:
/// the thread, App (<see cref="MomentSources"/>) and Place the moment happened in.
/// </summary>
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

/// <summary>
/// <see cref="Kind"/> is <c>RECOLLECTS</c> (snippet, weight, recall stats), <c>ABOUT</c>, or
/// <c>STANCE</c> (the author's current belief: valence + reasoning; retracted ones omitted).
/// </summary>
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

/// <summary>
/// A closed moment an App hands to Capture (ADR 0018): lines oldest → newest, one marked.
/// Lines need not be Room messages — Board posts and happenings are lines too.
/// </summary>
/// <param name="Source">App that closed it: <see cref="MomentSources"/>.</param>
/// <param name="ThreadId">The Event's <c>room_id</c>: Room, DM, Place talk, Board thread or happening.</param>
/// <param name="MarkIndex">Index into <paramref name="Lines"/> of the line being remembered.</param>
/// <param name="PlaceId">Place the moment happened in, when it happened in one.</param>
public sealed record Moment(
    Guid PartyId,
    string Source,
    Guid ThreadId,
    IReadOnlyList<MomentLine> Lines,
    int MarkIndex,
    IReadOnlyList<ParticipantView> Present,
    Guid? PlaceId = null)
{
    /// <summary>A Room/DM/Place conversation, marked at <paramref name="markMessageId"/>.</summary>
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

/// <summary>One thing said or shown in a <see cref="Moment"/>. MessageId only for Room lines.</summary>
public sealed record MomentLine(Guid SenderId, string Text, long? At = null, int? MessageId = null);

/// <summary>App ids written as the Event's <c>source</c> property (ADR 0018).</summary>
public static class MomentSources
{
    public const string Rooms = "rooms";
    public const string Messenger = "messenger";
    public const string Places = "places";
    public const string Board = "board";
    public const string World = "world";
    public const string Import = "import";

    public static string OfKind(string? chatGroupKind) => chatGroupKind switch
    {
        ChatGroupKinds.Direct => Messenger,
        ChatGroupKinds.Place => Places,
        _ => Rooms,
    };
}
