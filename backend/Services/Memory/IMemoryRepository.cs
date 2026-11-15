using PartyTown.Model;

namespace PartyTown.Services.Memory;

public interface IMemoryRepository
{
    Task<MemoryCaptureResult> CaptureAsync(Moment moment, CancellationToken ct);

    Task<IReadOnlyList<RecalledMemory>> RecallAsync(
        Guid personaId,
        Guid partyId,
        IReadOnlyList<Guid> presentPersonaIds,
        string anchorText,
        int limit,
        CancellationToken ct,
        Guid? placeId = null);

    Task StrengthenRecollectionAsync(Guid recollectionId, CancellationToken ct);

    Task<Guid> AppendStanceAsync(
        Guid partyId,
        Guid sourcePersonaId,
        StanceTargetSpec target,
        double valence,
        string reasoning,
        StanceAttribution? attribution,
        CancellationToken ct);

    Task<Guid?> RetractStanceAsync(
        Guid partyId,
        Guid sourcePersonaId,
        Guid stanceId,
        CancellationToken ct);

    Task<ConsolidationBatch> GetUnconsolidatedRecollectionsAsync(
        Guid partyId,
        Guid personaId,
        CancellationToken ct);

    Task MarkConsolidatedAsync(
        Guid partyId,
        Guid personaId,
        ConsolidationBatch batch,
        CancellationToken ct);

    Task<IReadOnlyList<StanceRecord>> ListStancesAsync(
        Guid partyId,
        Guid sourcePersonaId,
        CancellationToken ct);

    Task<IReadOnlyList<StanceLine>> RecallStancesAsync(
        Guid partyId,
        Guid sourcePersonaId,
        IReadOnlyList<Guid> presentPersonaIds,
        string anchorText,
        int limit,
        CancellationToken ct);

    Task<MemoryGraphDto> GetPartyMemoryGraphAsync(Guid partyId, CancellationToken ct);

    Task<Guid> AppendIntrinsicStanceAsync(
        Guid personaId,
        StanceTargetSpec target,
        double valence,
        string reasoning,
        StanceAttribution? attribution,
        CancellationToken ct);

    Task<IReadOnlyList<StanceRecord>> ListIntrinsicStancesAsync(
        Guid personaId,
        CancellationToken ct);

    Task<Guid?> PromoteStanceAsync(
        Guid partyId,
        Guid sourcePersonaId,
        Guid stanceId,
        CancellationToken ct);

    Task ForgetBeforeAsync(Guid partyId, Guid personaId, DateTimeOffset before, CancellationToken ct);
}
