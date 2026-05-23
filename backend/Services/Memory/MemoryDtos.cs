namespace PartyTown.Services.Memory;

/// <summary>
/// Minimal projection of a Participant as seen at capture time — id plus display name plus
/// whether they are user-driven. Used by extractors to ground per-Persona snippets and by
/// the repository to wire <c>RECOLLECTS</c> edges from non-user participants.
/// </summary>
public sealed record ParticipantSnapshot(Guid Id, string Name, bool IsUser);

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
/// Per-Party memory subgraph payload for the debug viz (issue #58). Backend emits
/// only what AGE stores — ids plus a few inline scalars (Event description, Recollection
/// snippet, Concept display). Display-name enrichment for Personas / Rooms / Messages is
/// performed client-side via the existing TanStack Query hooks.
/// </summary>
public sealed record MemoryGraphDto(
    IReadOnlyList<MemoryGraphNode> Nodes,
    IReadOnlyList<MemoryGraphLink> Links);

/// <summary>
/// A node in the memory subgraph. <see cref="Id"/> is a kind-prefixed stable string
/// (e.g. <c>room:&lt;guid&gt;</c>, <c>event:&lt;guid&gt;</c>) used as the canonical join
/// key on both ends of every <see cref="MemoryGraphLink"/>.
/// </summary>
public sealed record MemoryGraphNode(
    string Id,
    string Kind,
    string? Description = null,
    string? Display = null,
    string? CreatedAt = null);

/// <summary>
/// A directed edge between two <see cref="MemoryGraphNode"/>s. <see cref="Kind"/> is one
/// of <c>RECOLLECTS</c>, <c>ABOUT</c>, <c>ANCHORED_TO</c>, <c>HAS_PARTICIPANT</c>.
/// </summary>
public sealed record MemoryGraphLink(
    string Source,
    string Target,
    string Kind,
    string? Snippet = null,
    string? Ts = null);
