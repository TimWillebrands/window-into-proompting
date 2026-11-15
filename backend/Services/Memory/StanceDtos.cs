using System.Text.Json.Serialization;

namespace PartyTown.Services.Memory;

[JsonConverter(typeof(JsonStringEnumConverter<StanceTargetKind>))]
public enum StanceTargetKind
{
    Participant,
    Concept,
    Self,
}

public sealed record StanceTargetSpec(
    StanceTargetKind Kind,
    Guid? PersonaId,
    string? ConceptName,
    string? ConceptDisplay);

[JsonConverter(typeof(JsonStringEnumConverter<StanceOrigin>))]
public enum StanceOrigin
{
    Curator,

    Consolidation,

    Retract,
}

public sealed record StanceAttribution(
    StanceOrigin Origin,
    Guid? RunId = null,
    Guid? RetractsId = null);

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
    Guid? RetractsId = null,
    bool IsIntrinsic = false);

public sealed record StanceLine(string Reasoning, double Valence, string? Contrast = null);
