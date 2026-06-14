using PartyTown.Model;

namespace PartyTown.Services.Memory;

/// <summary>
/// Memory subsystem seam. Slice 1 ships <see cref="CaptureMomentAsync"/>; slice 2 added
/// recall (top-N recent, ADR 0009), upgraded by ADR 0015 to salience-ranked
/// <see cref="RecallAsync"/> plus the <see cref="StrengthenRecollectionAsync"/> write.
/// The Stance floor (ADR 0016) added <see cref="AppendStanceAsync"/> /
/// <see cref="ListStancesAsync"/> / <see cref="RecallStancesAsync"/>; Consolidation v1
/// added the watermark reads/writes (<see cref="GetUnconsolidatedRecollectionsAsync"/> /
/// <see cref="AdvanceConsolidationWatermarkAsync"/>) and <see cref="RetractStanceAsync"/>.
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
    /// objective tags via one LLM call, extract every present Participant's Recollection
    /// snippet via a second batched LLM call, and persist the Event, Concepts, and
    /// Recollection edges in one transaction. Exactly two LLM calls regardless of cast size.
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
    /// Two-arm salience-ranked Recall (ADR 0015): a quota union of the <em>relevant</em>
    /// arm — a graph walk <c>←ABOUT← Event ←RECOLLECTS</c> from the beat's anchors
    /// (present cast Participants + Concepts whose normalised name matches tokens of the
    /// triggering message) — and the <em>recent</em> arm (recency pool). Each arm is
    /// internally ranked by <c>weight × decay(now − ts) + use_bonus(recall_count,
    /// last_recalled)</c> computed in C#; the relevant arm takes up to ~5 slots, recent
    /// fills the remainder to <paramref name="limit"/>; dedup by edge id. Zero anchor
    /// matches degrades gracefully to recency-only (ADR 0009 behaviour). One Cypher
    /// round-trip, no LLM, no embeddings. Empty list if nothing has been remembered yet.
    /// </summary>
    /// <param name="personaId">The Persona's library id (Persona.Id, not Participant pkey).</param>
    /// <param name="partyId">Scope: only Recollections inside this Party.</param>
    /// <param name="presentPersonaIds">The Room's live cast (self included) — Participant anchors.</param>
    /// <param name="anchorText">The triggering message — tokenised into Concept anchors.</param>
    /// <param name="limit">Maximum number of memories to return. Caller picks the budget.</param>
    Task<IReadOnlyList<RecalledMemory>> RecallAsync(
        Guid personaId,
        Guid partyId,
        IReadOnlyList<Guid> presentPersonaIds,
        string anchorText,
        int limit,
        CancellationToken ct);

    /// <summary>
    /// Strengthening write (ADR 0015): the Decision phase *picked* this memory — not mere
    /// surfacing — so increment its <c>recall_count</c> and stamp <c>last_recalled</c>.
    /// One DB write, fired off the beat path; a miss (unknown id, pre-ADR-0015 edge) is a
    /// silent no-op.
    /// </summary>
    /// <param name="recollectionId">The RECOLLECTS edge's <c>id</c> property (uuid).</param>
    Task StrengthenRecollectionAsync(Guid recollectionId, CancellationToken ct);

    /// <summary>
    /// Append one Stance edge (ADR 0016) from the authoring Participant to a target —
    /// another Participant, a Concept (MERGE'd, auto-created on first reference), or itself.
    /// Append-only: never mutates a prior edge; the new edge becomes the latest-wins current
    /// stance for that (source, target) pair. The curator is the floor's first writer.
    /// </summary>
    /// <param name="partyId">Party scope — the Participant is keyed (persona_id, party_id).</param>
    /// <param name="sourcePersonaId">The Persona authoring the Stance (Acquired, Participant-scope).</param>
    /// <param name="target">Who/what the Stance points at.</param>
    /// <param name="valence">Scalar feeling, clamped to −1..1.</param>
    /// <param name="reasoning">Short second-person text in the Recollection-snippet voice.</param>
    /// <param name="attribution">
    /// Who is writing and why (ADR 0016 auditability). Null = curator-authored.
    /// </param>
    /// <returns>The new edge's stable <c>id</c>.</returns>
    Task<Guid> AppendStanceAsync(
        Guid partyId,
        Guid sourcePersonaId,
        StanceTargetSpec target,
        double valence,
        string reasoning,
        StanceAttribution? attribution,
        CancellationToken ct);

    /// <summary>
    /// One-click retract (ADR 0016): append a neutralizing edge (valence 0, origin
    /// <see cref="StanceOrigin.Retract"/>, <c>retracts</c> pointing at
    /// <paramref name="stanceId"/>) toward the same target. Latest-wins makes the retract
    /// current immediately — the target then renders nothing in <c># Where you stand</c> —
    /// while the full history stays queryable. Never deletes.
    /// </summary>
    /// <returns>The neutralizing edge's id, or null when <paramref name="stanceId"/> is not
    /// an edge authored by this Participant.</returns>
    Task<Guid?> RetractStanceAsync(
        Guid partyId,
        Guid sourcePersonaId,
        Guid stanceId,
        CancellationToken ct);

    /// <summary>
    /// The Recollections accumulated since the Participant's Consolidation watermark (the
    /// <c>ts</c> property on the Participant vertex, ADR 0016), oldest first, capped to a
    /// batch the proposer prompt can hold. Also feeds the gauge: Σ <c>weight</c> of the
    /// returned items is the "unprocessed experience" a trigger compares to its threshold.
    /// </summary>
    Task<ConsolidationBatch> GetUnconsolidatedRecollectionsAsync(
        Guid partyId,
        Guid personaId,
        CancellationToken ct);

    /// <summary>
    /// Stamp the Participant's Consolidation watermark (<c>ts</c> property) — every
    /// Recollection with <c>ts</c> at or before it counts as consumed. Called after a run
    /// even when it proposed nothing: the experience was slept on either way.
    /// </summary>
    Task AdvanceConsolidationWatermarkAsync(
        Guid partyId,
        Guid personaId,
        DateTimeOffset watermark,
        CancellationToken ct);

    /// <summary>
    /// Every Stance edge authored by a Participant, newest first, for the debug/authoring UI.
    /// History is preserved (append-only) — superseded edges are returned too; the latest per
    /// (source, target) carries <see cref="StanceRecord.IsCurrent"/> = true. Empty when the
    /// Participant has authored no Stances.
    /// </summary>
    Task<IReadOnlyList<StanceRecord>> ListStancesAsync(
        Guid partyId,
        Guid sourcePersonaId,
        CancellationToken ct);

    /// <summary>
    /// Latest-wins Stances scoped to the beat's live anchors, for the ambient
    /// <c># Where you stand</c> block (ADR 0016). Keeps only the current edge per target,
    /// then renders a Stance iff its target is one of the present cast
    /// (<paramref name="presentPersonaIds"/>, which includes self) or a Concept whose name or
    /// display appears in <paramref name="anchorText"/> (the triggering message). Ordered
    /// most-strongly-felt first and capped at <paramref name="limit"/>. Pure DB read — zero
    /// LLM calls on the beat path. Empty when nothing is anchored.
    /// </summary>
    Task<IReadOnlyList<StanceLine>> RecallStancesAsync(
        Guid partyId,
        Guid sourcePersonaId,
        IReadOnlyList<Guid> presentPersonaIds,
        string anchorText,
        int limit,
        CancellationToken ct);

    /// <summary>
    /// Read the per-Party memory subgraph for the debug viz: every Event with
    /// <c>party_id = partyId</c> (carrying its <c>room_id</c> / <c>anchor_message_id</c> as
    /// properties), the Participants who recollect (or are talked about by) those Events,
    /// the Personas behind those Participants, and the Concepts those Events are about.
    /// Silent Participants are not enumerated; there are no Party/Room/Message vertices.
    /// Empty Rooms (no captured Event) do not appear — the viz lists Rooms from the REST
    /// API. Snapshot only — no streaming.
    /// </summary>
    Task<MemoryGraphDto> GetPartyMemoryGraphAsync(Guid partyId, CancellationToken ct);

    // ─── Intrinsic Stances (ADR 0016, issue #91) ──────────────────────────────────────────

    /// <summary>
    /// Append one Intrinsic Stance at Persona (library) scope — a <c>(:Persona)-[:STANCE]</c>
    /// edge that travels into every Party the Persona joins. Append-only, same latest-wins
    /// rule as Acquired. The curator authors these in the persona-management UI; Promotion
    /// lifts an Acquired Stance to this scope.
    /// </summary>
    /// <param name="personaId">The authoring Persona's library id.</param>
    /// <param name="target">
    /// Target shape. For <see cref="StanceTargetKind.Participant"/>, <c>PersonaId</c> names
    /// the target Persona (not a Participant) — on promotion the caller has already resolved
    /// the Participant's underlying Persona id. <see cref="StanceTargetKind.Self"/> writes a
    /// self-loop on the Persona node.
    /// </param>
    /// <returns>The new edge's stable <c>id</c>.</returns>
    Task<Guid> AppendIntrinsicStanceAsync(
        Guid personaId,
        StanceTargetSpec target,
        double valence,
        string reasoning,
        StanceAttribution? attribution,
        CancellationToken ct);

    /// <summary>
    /// Every Intrinsic Stance edge authored at Persona scope, newest first. Mirrors
    /// <see cref="ListStancesAsync"/> but reads from the Persona node rather than a
    /// Participant. <see cref="StanceRecord.IsCurrent"/> marks the latest-wins survivor per
    /// target; superseded edges are returned for history.
    /// </summary>
    Task<IReadOnlyList<StanceRecord>> ListIntrinsicStancesAsync(
        Guid personaId,
        CancellationToken ct);

    /// <summary>
    /// Promote an Acquired Stance to Intrinsic scope (ADR 0016): write a new Persona-scope
    /// edge carrying the current projection; the original Acquired edge is untouched. If the
    /// Acquired Stance targets a Participant, the target is re-pointed to that Participant's
    /// underlying Persona so the edge is meaningful across Parties. Concept and self targets
    /// carry over unchanged.
    /// </summary>
    /// <param name="partyId">Party scope for resolving the Acquired Stance.</param>
    /// <param name="sourcePersonaId">Persona id of the Participant who owns the Acquired Stance.</param>
    /// <param name="stanceId">The Acquired Stance edge id to promote.</param>
    /// <returns>
    /// The new Intrinsic edge's id, or <c>null</c> when <paramref name="stanceId"/> is not an
    /// edge authored by this Participant in this Party.
    /// </returns>
    Task<Guid?> PromoteStanceAsync(
        Guid partyId,
        Guid sourcePersonaId,
        Guid stanceId,
        CancellationToken ct);
}
