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
/// Outcome of a single <see cref="IMemoryRepository.CaptureMomentAsync"/> call.
/// </summary>
public sealed record MemoryCaptureResult(
    bool EventCreated,
    int RecollectionsCreated,
    int ConceptsTouched);

/// <summary>
/// Per-Party memory subgraph payload for the debug viz. Backend emits only what AGE
/// stores; display-name enrichment for Personas / Rooms is client-side.
/// </summary>
public sealed record MemoryGraphDto(
    IReadOnlyList<MemoryGraphNode> Nodes,
    IReadOnlyList<MemoryGraphLink> Links);

/// <summary>
/// <see cref="Id"/> is a kind-prefixed stable string (e.g. <c>room:&lt;guid&gt;</c>,
/// <c>event:&lt;guid&gt;</c>) used as the canonical join key on both ends of every
/// <see cref="MemoryGraphLink"/>.
/// </summary>
public sealed record MemoryGraphNode(
    string Id,
    string Kind,
    string? Description = null,
    string? Display = null,
    string? CreatedAt = null);

/// <summary>
/// <see cref="Kind"/> is one of <c>RECOLLECTS</c>, <c>ABOUT</c>, <c>ANCHORED_TO</c>,
/// <c>HAS_PARTICIPANT</c>.
/// </summary>
public sealed record MemoryGraphLink(
    string Source,
    string Target,
    string Kind,
    string? Snippet = null,
    string? Ts = null);
