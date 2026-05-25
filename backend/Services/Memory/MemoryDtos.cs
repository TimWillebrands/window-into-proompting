using PartyTown.Model;

namespace PartyTown.Services.Memory;

/// <summary>
/// Minimal projection of a Participant as seen at capture time — id plus display name plus
/// the Driver kind. Used by extractors to ground per-Persona snippets and by the repository
/// to wire <c>RECOLLECTS</c> edges from non-user participants (LLM and System both
/// accumulate memory; only User does not).
/// </summary>
public sealed record ParticipantSnapshot(Guid Id, string Name, DriverKind Driver);

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
