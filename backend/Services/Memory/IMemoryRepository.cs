using PartyTown.Model;

namespace PartyTown.Services.Memory;

/// <summary>
/// Memory subsystem write seam. Slice 1 exposes <see cref="CaptureMomentAsync"/> only —
/// recall, Stance, and Consolidation arrive in later slices.
/// </summary>
/// <remarks>
/// Per ADR 0006, the implementation runs Cypher against Apache AGE directly through
/// <c>AppDbContext</c>; it is not behind an Orleans grain. Memory DTOs are plain records
/// so the Orleans serialization footguns flagged in <c>CLAUDE.md</c> never apply here.
/// </remarks>
public interface IMemoryRepository
{
    /// <summary>
    /// Capture a single "remember this" moment: extract a neutral Event description and
    /// objective tags via one LLM call, fan out per-Participant Recollection snippets in
    /// parallel, and persist the Event, Concepts, and Recollection edges in one transaction.
    /// </summary>
    /// <param name="partyId">Party the Room belongs to.</param>
    /// <param name="roomId">Room (legacy: ChatGroup) the Message was sent in.</param>
    /// <param name="messageId">The marked message inside <paramref name="recentContext"/>.</param>
    /// <param name="presentParticipants">
    /// Cast present when the moment was marked. Non-user participants each get a Recollection
    /// attempt; user-driven participants are skipped.
    /// </param>
    /// <param name="recentContext">
    /// The conversation slice the extractors see, ordered oldest-to-newest, and including
    /// the marked message identified by <paramref name="messageId"/>.
    /// </param>
    Task<MemoryCaptureResult> CaptureMomentAsync(
        Guid partyId,
        Guid roomId,
        int messageId,
        IReadOnlyList<ParticipantSnapshot> presentParticipants,
        IReadOnlyList<ChatMessage> recentContext,
        CancellationToken ct);
}
