using PartyTown.Model;
using PartyTown.Services.Memory;

namespace PartyTown.Bench;

/// <summary>
/// No-op <see cref="IMemoryRepository"/> for the bench host. A registration is mandatory in any
/// host that can activate <c>PersonaGrain</c> — without it, fanout/AddPersona hangs silently in
/// WaitingForActivation (see memory <c>feedback_testcluster_persona_grain_dep.md</c>). Probes
/// that target memory swap in the real <c>MemoryRepository</c>; everything else gets this stub.
/// </summary>
public sealed class StubMemoryRepository : IMemoryRepository
{
    public Task<MemoryCaptureResult> CaptureMomentAsync(
        Guid partyId,
        Guid roomId,
        int messageId,
        IReadOnlyList<ParticipantView> presentParticipants,
        IReadOnlyList<ChatMessage> recentContext,
        CancellationToken ct)
        => Task.FromResult(new MemoryCaptureResult(false, 0, 0));

    public Task<IReadOnlyList<RecalledMemory>> RecallAsync(
        Guid personaId,
        Guid partyId,
        IReadOnlyList<Guid> presentPersonaIds,
        string anchorText,
        int limit,
        CancellationToken ct)
        => Task.FromResult<IReadOnlyList<RecalledMemory>>(Array.Empty<RecalledMemory>());

    public Task StrengthenRecollectionAsync(Guid recollectionId, CancellationToken ct)
        => Task.CompletedTask;

    public Task<Guid> AppendStanceAsync(
        Guid partyId,
        Guid sourcePersonaId,
        StanceTargetSpec target,
        double valence,
        string reasoning,
        StanceAttribution? attribution,
        CancellationToken ct)
        => Task.FromResult(Guid.NewGuid());

    public Task<Guid?> RetractStanceAsync(
        Guid partyId,
        Guid sourcePersonaId,
        Guid stanceId,
        CancellationToken ct)
        => Task.FromResult<Guid?>(null);

    public Task<IReadOnlyList<StanceRecord>> ListStancesAsync(
        Guid partyId,
        Guid sourcePersonaId,
        CancellationToken ct)
        => Task.FromResult<IReadOnlyList<StanceRecord>>(Array.Empty<StanceRecord>());

    public Task<IReadOnlyList<StanceLine>> RecallStancesAsync(
        Guid partyId,
        Guid sourcePersonaId,
        IReadOnlyList<Guid> presentPersonaIds,
        string anchorText,
        int limit,
        CancellationToken ct)
        => Task.FromResult<IReadOnlyList<StanceLine>>(Array.Empty<StanceLine>());

    public Task<Guid> AppendIntrinsicStanceAsync(
        Guid personaId,
        StanceTargetSpec target,
        double valence,
        string reasoning,
        StanceAttribution? attribution,
        CancellationToken ct)
        => Task.FromResult(Guid.NewGuid());

    public Task<IReadOnlyList<StanceRecord>> ListIntrinsicStancesAsync(
        Guid personaId,
        CancellationToken ct)
        => Task.FromResult<IReadOnlyList<StanceRecord>>(Array.Empty<StanceRecord>());

    public Task<Guid?> PromoteStanceAsync(
        Guid partyId,
        Guid sourcePersonaId,
        Guid stanceId,
        CancellationToken ct)
        => Task.FromResult<Guid?>(null);

    public Task<ConsolidationBatch> GetUnconsolidatedRecollectionsAsync(
        Guid partyId,
        Guid personaId,
        CancellationToken ct)
        => Task.FromResult(new ConsolidationBatch(null, Array.Empty<UnconsolidatedRecollection>()));

    public Task AdvanceConsolidationWatermarkAsync(
        Guid partyId,
        Guid personaId,
        DateTimeOffset watermark,
        CancellationToken ct)
        => Task.CompletedTask;

    public Task<MemoryGraphDto> GetPartyMemoryGraphAsync(Guid partyId, CancellationToken ct)
        => Task.FromResult(new MemoryGraphDto(Array.Empty<MemoryGraphNode>(), Array.Empty<MemoryGraphLink>()));
}
