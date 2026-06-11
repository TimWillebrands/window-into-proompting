using System.Text.Json.Serialization;

namespace PartyTown.Services.Memory;

/// <summary>
/// What a <see cref="StanceRecord"/> points at. A Stance targets another
/// <see cref="StanceTargetKind.Participant"/>, a <see cref="StanceTargetKind.Concept"/>, or
/// the authoring Participant itself (<see cref="StanceTargetKind.Self"/>). Self is the same
/// graph node as the source — a self-loop edge — but carried as its own kind so the API and
/// UI can express "how you feel about yourself" without resolving the source persona id.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StanceTargetKind>))]
public enum StanceTargetKind
{
    Participant,
    Concept,
    Self,
}

/// <summary>
/// Write-side target descriptor for <see cref="IMemoryRepository.AppendStanceAsync"/>.
/// <see cref="PersonaId"/> is required for <see cref="StanceTargetKind.Participant"/>;
/// <see cref="ConceptName"/>/<see cref="ConceptDisplay"/> for <see cref="StanceTargetKind.Concept"/>
/// (the Concept is MERGE'd — auto-created on first reference, per CONTEXT.md). Self carries
/// neither — the repository points the edge back at the source Participant.
/// </summary>
public sealed record StanceTargetSpec(
    StanceTargetKind Kind,
    Guid? PersonaId,
    string? ConceptName,
    string? ConceptDisplay);

/// <summary>
/// Who wrote a STANCE edge (ADR 0016 auditability). Stored as an <c>origin</c> property on
/// the edge; edges written before attribution existed lack the property and read back as
/// <see cref="Curator"/> — the only writer that existed then.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StanceOrigin>))]
public enum StanceOrigin
{
    /// <summary>Hand-authored in the debug/curation UI.</summary>
    Curator,

    /// <summary>Auto-appended by a Consolidation run; <see cref="StanceAttribution.RunId"/> set.</summary>
    Consolidation,

    /// <summary>
    /// Neutralizing edge appended by one-click retract; <see cref="StanceAttribution.RetractsId"/>
    /// points at the edge being masked. A retract-current target renders nothing in
    /// <c># Where you stand</c> — latest-wins masks the junk without deleting history.
    /// </summary>
    Retract,
}

/// <summary>
/// Write-side provenance for <see cref="IMemoryRepository.AppendStanceAsync"/>. Null means
/// curator-authored (no extra edge properties — matches pre-attribution edges).
/// </summary>
public sealed record StanceAttribution(
    StanceOrigin Origin,
    Guid? RunId = null,
    Guid? RetractsId = null);

/// <summary>
/// One STANCE edge as stored, for listing + history (ADR 0016). Append-only: every write is
/// a new edge, so a target accumulates many records over time. <see cref="IsCurrent"/> marks
/// the latest-wins survivor per (source, target) — the one the read path surfaces. History
/// stays queryable because the superseded edges are returned too, just unflagged.
/// <see cref="Origin"/>/<see cref="RunId"/>/<see cref="RetractsId"/> carry the audit trail:
/// which writer appended this edge, under which Consolidation run, masking which prior edge.
/// </summary>
public sealed record StanceRecord(
    Guid Id,
    double Valence,
    string Reasoning,
    DateTimeOffset Ts,
    StanceTargetKind TargetKind,
    Guid? TargetPersonaId,
    string? TargetConceptName,
    string? TargetConceptDisplay,
    bool IsCurrent,
    StanceOrigin Origin = StanceOrigin.Curator,
    Guid? RunId = null,
    Guid? RetractsId = null);

/// <summary>
/// One latest-wins Stance line surfaced for the ambient <c># Where you stand</c> block
/// (ADR 0016). <see cref="Reasoning"/> is the second-person one-liner rendered into both the
/// Decision and Speaking prompts; <see cref="Valence"/> rides along so the block can order
/// most-strongly-felt first. Already anchor-scoped and capped by
/// <see cref="IMemoryRepository.RecallStancesAsync"/>.
/// </summary>
public sealed record StanceLine(string Reasoning, double Valence);
