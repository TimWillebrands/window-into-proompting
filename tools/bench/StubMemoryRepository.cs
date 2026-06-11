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
        IReadOnlyList<ParticipantSnapshot> presentParticipants,
        IReadOnlyList<ChatMessage> recentContext,
        CancellationToken ct)
        => Task.FromResult(new MemoryCaptureResult(false, 0, 0));

    public Task<IReadOnlyList<string>> RecallRecentSnippetsAsync(
        Guid personaId,
        Guid partyId,
        int limit,
        CancellationToken ct)
        => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
}
