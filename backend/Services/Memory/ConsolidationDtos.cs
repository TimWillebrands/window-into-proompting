namespace PartyTown.Services.Memory;

/// <summary>
/// One RECOLLECTS edge not yet consumed by Consolidation (its <c>ts</c> is past the
/// Participant's watermark). The Consolidation prompt walks these oldest-first.
/// </summary>
public sealed record UnconsolidatedRecollection(
    Guid Id,
    string Snippet,
    DateTimeOffset Ts,
    double Weight);

/// <summary>
/// The unconsolidated slice of a Participant's Recollections plus the watermark it was read
/// against. <see cref="Watermark"/> is null when the Participant has never been consolidated
/// (the <c>ts</c> property is absent) — every Recollection is then unconsolidated.
/// </summary>
public sealed record ConsolidationBatch(
    DateTimeOffset? Watermark,
    IReadOnlyList<UnconsolidatedRecollection> Items);

/// <summary>
/// The Participant a Consolidation run sleeps for: identity plus the bio the proposer prompt
/// grounds on. Built by the caller from the Party's cast — the memory layer never reaches
/// into grains.
/// </summary>
public sealed record ConsolidationSubject(Guid PersonaId, string Name, string? Bio);

/// <summary>
/// One Party cast member the proposer may target with a Participant Stance. Name is what the
/// LLM keys its output by; PersonaId is what gets written.
/// </summary>
public sealed record ConsolidationRosterEntry(Guid PersonaId, string Name);

/// <summary>
/// One proposed Stance append out of a Consolidation run — already resolved to write-shape
/// (names mapped to persona ids, concept names normalised) by the proposer.
/// </summary>
public sealed record StanceProposal(
    StanceTargetKind Kind,
    Guid? TargetPersonaId,
    string? ConceptName,
    string? ConceptDisplay,
    double Valence,
    string Reasoning);

/// <summary>
/// Outcome of one Consolidation run for one Participant. <see cref="RecollectionsWalked"/> = 0
/// means there was nothing to sleep on (watermark already current) and no LLM call was made.
/// <see cref="Skipped"/> marks a run that bowed out because another run for the same
/// Participant was already in flight.
/// </summary>
public sealed record ConsolidationRunResult(
    Guid RunId,
    Guid PersonaId,
    int RecollectionsWalked,
    int StancesAppended,
    DateTimeOffset? Watermark,
    bool Skipped = false);
