using PartyTown.Model;

namespace PartyTown.Services.Memory;

/// <summary>
/// Memory subsystem seam. Slice 1 ships <see cref="CaptureMomentAsync"/>; slice 2 adds
/// <see cref="RecallRecentSnippetsAsync"/> (top-N recent Recollection snippets — see ADR 0009).
/// Stance and Consolidation arrive in later slices.
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
    /// Cast present when the moment was marked. LLM-Effective participants each get a
    /// Recollection attempt; User-Effective participants are skipped.
    /// </param>
    /// <param name="recentContext">
    /// The conversation slice the extractors see, ordered oldest-to-newest, and including
    /// the marked message identified by <paramref name="messageId"/>.
    /// </param>
    Task<MemoryCaptureResult> CaptureMomentAsync(
        Guid partyId,
        Guid roomId,
        int messageId,
        IReadOnlyList<ParticipantView> presentParticipants,
        IReadOnlyList<ChatMessage> recentContext,
        CancellationToken ct);

    /// <summary>
    /// Return the most recent Recollection snippets for a Participant — the Persona's first-
    /// person memory in this Party, across all Rooms. Ordered newest-first. MVP retrieval
    /// (ADR 0009): no concept matching, no embeddings; relevance is judged in-context by the
    /// generating LLM. Empty list if nothing has been remembered yet.
    /// </summary>
    /// <param name="personaId">The Persona's library id (Persona.Id, not Participant pkey).</param>
    /// <param name="partyId">Scope: only Recollections inside this Party.</param>
    /// <param name="limit">Maximum number of snippets to return. Caller picks the budget.</param>
    Task<IReadOnlyList<string>> RecallRecentSnippetsAsync(
        Guid personaId,
        Guid partyId,
        int limit,
        CancellationToken ct);
}
