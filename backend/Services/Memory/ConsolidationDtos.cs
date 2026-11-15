namespace PartyTown.Services.Memory;

public sealed record UnconsolidatedRecollection(
    Guid Id,
    string Snippet,
    DateTimeOffset Ts,
    double Weight);

public sealed record ConsolidationBatch(
    DateTimeOffset? Watermark,
    IReadOnlyList<UnconsolidatedRecollection> Items);

public sealed record ConsolidationSubject(Guid PersonaId, string Name, string? Bio);

public sealed record ConsolidationRosterEntry(Guid PersonaId, string Name);

public sealed record StanceProposal(
    StanceTargetKind Kind,
    Guid? TargetPersonaId,
    string? ConceptName,
    string? ConceptDisplay,
    double Valence,
    string Reasoning);

public sealed record ConsolidationRunResult(
    Guid RunId,
    Guid PersonaId,
    int RecollectionsWalked,
    int StancesAppended,
    DateTimeOffset? Watermark,
    bool Skipped = false);
