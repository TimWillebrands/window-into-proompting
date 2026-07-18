namespace PartyTown.Services.Import;

/// <summary>
/// IR chunk categories emitted by <see cref="AiStudioImportParser"/>. Strings (not an
/// enum) so grain state, REST payloads and the conservation ledger all speak the same
/// vocabulary as the spearhead artifacts.
/// </summary>
public static class ImportChunkCategories
{
    public const string Thought = "thought";
    public const string Media = "media";
    public const string Recap = "recap";
    public const string Message = "message";
    public const string Empty = "empty";
}

public static class DraftItemTypes
{
    public const string Trait = "trait";
    public const string Episode = "episode";
    public const string Rule = "rule";
}

/// <summary>Where a draft item is headed at commit. Flippable draft state, not a deletion.</summary>
public static class DraftRouting
{
    /// <summary>Episode becomes an AGE Event (+ Recollections) at commit.</summary>
    public const string Event = "event";

    /// <summary>Routed to Room history only — no AGE Event (weight floor, or human demotion).</summary>
    public const string History = "history";

    /// <summary>Recorded but never written (rules / meta content). Reason always set.</summary>
    public const string Discarded = "discarded";

    /// <summary>Trait folded into a persona card at commit.</summary>
    public const string PersonaCard = "persona";
}

/// <summary>Per-chunk conservation ledger dispositions. Every chunk lands in exactly one.</summary>
public static class ChunkDispositions
{
    public const string EventRouted = "event-routed";
    public const string Folded = "folded";
    public const string HistoryOnly = "history-only";
    public const string Discarded = "discarded";
    public const string Unprocessed = "unprocessed";
}

/// <summary>Correction-ledger vocabulary (ADR 0017): what the human changed between the
/// extractor's suggestion and the committed final. <c>merged</c>/<c>split</c>/
/// <c>match-flipped</c> are reserved for the registry/match-or-mint slice (issue 03) —
/// nothing emits them yet.</summary>
public static class CorrectionKinds
{
    public const string Promoted = "promoted";
    public const string Demoted = "demoted";
    public const string Reweighted = "reweighted";
    public const string Merged = "merged";
    public const string Split = "split";
    public const string Renamed = "renamed";
    public const string MatchFlipped = "match-flipped";
    public const string RegeneratedWithNote = "regenerated-with-note";
}

/// <summary>Where a registry cast member routes at commit (ADR 0017): dossier'd cast
/// become Personas; recurring referenced characters become Concepts, deliberately —
/// person-as-concept is load-bearing (arc-critical non-cast characters are reachable
/// only via Concept).</summary>
public static class CastRoutingModes
{
    public const string Persona = "persona";
    public const string Concept = "concept";
}

/// <summary>Per-character match-or-mint state (ADR 0013 mechanism, carried into 0017):
/// <c>unmatched → proposed → confirmed-match | confirmed-mint</c>. Commit executes the
/// recorded decision, never prompts; a scene with a <c>proposed</c> cast member refuses
/// to commit until the human decides.</summary>
public static class CastMatchStates
{
    public const string Unmatched = "unmatched";
    public const string Proposed = "proposed";
    public const string ConfirmedMatch = "confirmed-match";
    public const string ConfirmedMint = "confirmed-mint";
}

/// <summary>One registry cast member: canonical name, source-stated aliases (typos the
/// human pinned), persona-vs-concept routing, and the match-or-mint state against the
/// persona library. Entries discovered by a scene run start unconfirmed (proposals);
/// the registry is always optional — an empty one is a valid run.</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.RegistryCastEntry")]
public sealed class RegistryCastEntry
{
    [Id(0)] public string Name { get; set; } = string.Empty;
    [Id(1)] public List<string> Aliases { get; set; } = new();

    /// <summary>One of <see cref="CastRoutingModes"/>.</summary>
    [Id(2)] public string Routing { get; set; } = CastRoutingModes.Persona;

    /// <summary>False while the entry is a scene-run proposal the human has not blessed.
    /// Only confirmed entries feed the map call and the fold's canonicalisation.</summary>
    [Id(3)] public bool Confirmed { get; set; }

    /// <summary>One of <see cref="CastMatchStates"/>.</summary>
    [Id(4)] public string MatchState { get; set; } = CastMatchStates.Unmatched;

    /// <summary>Library persona the matcher proposed (kept after the decision — the
    /// correction ledger diffs the decision against it).</summary>
    [Id(5)] public Guid? ProposedPersonaId { get; set; }

    [Id(6)] public string? ProposedPersonaName { get; set; }

    /// <summary>The persona a confirmed-match reuses at commit.</summary>
    [Id(7)] public Guid? MatchedPersonaId { get; set; }
}

/// <summary>Human upsert of a registry cast entry. Null fields keep current values.</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.RegistryCastEdit")]
public sealed record RegistryCastEdit
{
    [Id(0)] public List<string>? Aliases { get; init; }

    /// <summary>"persona" | "concept".</summary>
    [Id(1)] public string? Routing { get; init; }

    [Id(2)] public bool? Confirmed { get; init; }
}

/// <summary>Human match-or-mint decision for one cast member.</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.CastMatchDecision")]
public sealed record CastMatchDecision
{
    /// <summary>"match" | "mint".</summary>
    [Id(0)] public string Decision { get; init; } = string.Empty;

    /// <summary>Match target; defaults to the proposed persona when omitted.</summary>
    [Id(1)] public Guid? PersonaId { get; init; }
}

/// <summary>Human edit of a registry concept (alias pinning + confirmation).</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.RegistryConceptEdit")]
public sealed record RegistryConceptEdit
{
    [Id(0)] public List<string>? Aliases { get; init; }
    [Id(1)] public bool? Confirmed { get; init; }
}

/// <summary>The session registry: cast + concepts, confirmed entries and open proposals.</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.ImportRegistryView")]
public sealed record ImportRegistryView
{
    [Id(0)] public List<RegistryCastEntry> Cast { get; init; } = new();
    [Id(1)] public List<ImportConcept> Concepts { get; init; } = new();
}

/// <summary>
/// The reviewed persona card the commit executes (ADR 0017 finalize invariant: every LLM
/// output passes human review as draft before it becomes real). Produced by the finalize
/// step (trait compress + Bio synthesis), editable by the human; a re-finalize after a
/// human edit only fills the Proposed* fields — human edits win, machines propose.
/// </summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.PersonaCardDraft")]
public sealed class PersonaCardDraft
{
    /// <summary>Canonical cast name this card belongs to.</summary>
    [Id(0)] public string Persona { get; set; } = string.Empty;

    [Id(1)] public string SystemPrompt { get; set; } = string.Empty;
    [Id(2)] public string? Bio { get; set; }

    /// <summary>Set on human edit; from then on finalize may only propose.</summary>
    [Id(3)] public bool HumanEdited { get; set; }

    [Id(4)] public string? ProposedSystemPrompt { get; set; }
    [Id(5)] public string? ProposedBio { get; set; }

    /// <summary>Hash of the trait summaries the last finalize consumed — detects staleness
    /// (new traits since) without storing the trait text twice.</summary>
    [Id(6)] public string TraitFingerprint { get; set; } = string.Empty;

    [Id(7)] public DateTimeOffset? FinalizedAt { get; set; }

    /// <summary>Snapshot of what the last commit wrote to the library persona. The commit
    /// compares the live persona against this before overwriting — drift means a human
    /// edited the persona outside the import, and human edits win.</summary>
    [Id(8)] public string? CommittedSystemPrompt { get; set; }

    [Id(9)] public string? CommittedBio { get; set; }
}

/// <summary>Human edit of a persona card draft. <c>AcceptProposal</c> promotes the pending
/// re-finalize proposal into the card instead.</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.PersonaCardEdit")]
public sealed record PersonaCardEdit
{
    [Id(0)] public string? SystemPrompt { get; init; }
    [Id(1)] public string? Bio { get; init; }
    [Id(2)] public bool? AcceptProposal { get; init; }
}

/// <summary>Result of a scene finalize pass: the cards now in the draft (new, refreshed
/// or proposal-updated) plus what the pass skipped as already current.</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.SceneFinalizeResult")]
public sealed record SceneFinalizeResult
{
    [Id(0)] public List<PersonaCardDraft> Cards { get; init; } = new();
    [Id(1)] public List<string> Skipped { get; init; } = new();
    [Id(2)] public int LlmCalls { get; init; }
}

/// <summary>One typed IR chunk. Stored once on the session; scenes reference by index range.</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.ImportChunk")]
public sealed record ImportChunk
{
    [Id(0)] public int Index { get; init; }
    [Id(1)] public string Role { get; init; } = string.Empty;
    [Id(2)] public string Category { get; init; } = string.Empty;
    [Id(3)] public string Text { get; init; } = string.Empty;
}

/// <summary>Parsed source document handed to the session grain at creation.</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.ImportSource")]
public sealed record ImportSource
{
    [Id(0)] public string? FileName { get; init; }
    [Id(1)] public string SystemInstruction { get; init; } = string.Empty;
    [Id(2)] public List<ImportChunk> Chunks { get; init; } = new();
}

/// <summary>Per-import knobs. Anchor + spacing place episode timestamps; the weight floor
/// routes sub-floor episodes to history-only (draft default, flippable per item).</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.ImportSettings")]
public sealed record ImportSettings
{
    [Id(0)] public double WeightFloor { get; init; } = 0.5;
    [Id(1)] public DateTimeOffset Anchor { get; init; }
    [Id(2)] public double SpacingMinutes { get; init; } = 10;
}

[GenerateSerializer, Alias("PartyTown.Services.Import.ImportSettingsUpdate")]
public sealed record ImportSettingsUpdate
{
    [Id(0)] public double? WeightFloor { get; init; }
    [Id(1)] public DateTimeOffset? Anchor { get; init; }
    [Id(2)] public double? SpacingMinutes { get; init; }
}

/// <summary>A human-selected chunk range processed as one unit (workshop vocabulary).</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.ImportScene")]
public sealed class ImportScene
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public int FromChunk { get; set; }
    [Id(2)] public int ToChunk { get; set; }
    [Id(3)] public string? Note { get; set; }

    /// <summary>Feed the export's systemInstruction (character dossier) through the canon
    /// path as part of this scene's run. Typically set on one scene per session.</summary>
    [Id(4)] public bool IncludeDossier { get; set; }

    [Id(5)] public int RunCount { get; set; }
    [Id(6)] public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>Committed scenes are settled: rerun/edit/delete are refused; their draft
    /// slice becomes the committed-item index later runs dedup against.</summary>
    [Id(7)] public bool Committed { get; set; }

    [Id(8)] public DateTimeOffset? CommittedAt { get; set; }

    /// <summary>Room messages for this scene were written. Persisted before the commit is
    /// complete so a retried commit never double-appends messages (the one non-idempotent
    /// write in the sequence).</summary>
    [Id(9)] public bool MessagesWritten { get; set; }
}

[GenerateSerializer, Alias("PartyTown.Services.Import.SceneDefinition")]
public sealed record SceneDefinition
{
    [Id(0)] public int FromChunk { get; init; }
    [Id(1)] public int ToChunk { get; init; }
    [Id(2)] public string? Note { get; init; }
    [Id(3)] public bool IncludeDossier { get; init; }
}

/// <summary>One folded draft item. Owned by a scene — rerunning that scene replaces its items.</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.ImportDraftItem")]
public sealed class ImportDraftItem
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public Guid SceneId { get; set; }
    [Id(2)] public string Type { get; set; } = string.Empty;

    /// <summary>Canonical persona name for traits; null for episodes/rules.</summary>
    [Id(3)] public string? Persona { get; set; }

    [Id(4)] public string Summary { get; set; } = string.Empty;
    [Id(5)] public double Weight { get; set; }
    [Id(6)] public List<string> Participants { get; set; } = new();
    [Id(7)] public List<string> Concepts { get; set; } = new();
    [Id(8)] public List<int> SourceChunks { get; set; } = new();

    /// <summary>Extraction call this item came from (dedup skips same-call siblings).</summary>
    [Id(9)] public string SourceId { get; set; } = string.Empty;

    [Id(10)] public string Routing { get; set; } = string.Empty;
    [Id(11)] public string? RoutingReason { get; set; }

    /// <summary>True once a human flipped the routing; settings changes then leave it alone.</summary>
    [Id(12)] public bool RoutingOverridden { get; set; }

    /// <summary>Scheme timestamp: anchor + earliest-source-chunk · spacing.</summary>
    [Id(13)] public DateTimeOffset At { get; set; }

    // Suggested-value snapshots, frozen at fold time. The correction ledger diffs these
    // against the human-final values at commit; edits never touch them.

    [Id(14)] public string SuggestedSummary { get; set; } = string.Empty;
    [Id(15)] public double SuggestedWeight { get; set; }
    [Id(16)] public string SuggestedRouting { get; set; } = string.Empty;
}

[GenerateSerializer, Alias("PartyTown.Services.Import.DraftItemEdit")]
public sealed record DraftItemEdit
{
    /// <summary>"event" | "history" (episodes only) to flip, or "auto" to clear the
    /// override and fall back to the weight floor.</summary>
    [Id(0)] public string? Routing { get; init; }

    [Id(1)] public string? Summary { get; init; }
    [Id(2)] public double? Weight { get; init; }
}

/// <summary>Recurring named thing, merged by source-stated aliases only.</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.ImportConcept")]
public sealed class ImportConcept
{
    [Id(0)] public string Name { get; set; } = string.Empty;
    [Id(1)] public List<string> Aliases { get; set; } = new();
    [Id(2)] public int Mentions { get; set; }

    /// <summary>Scene-discovered concepts start unconfirmed (registry proposals); only
    /// confirmed ones are injected into map calls as canonical vocabulary.</summary>
    [Id(3)] public bool Confirmed { get; set; }
}

// ── scene map call output (SceneMapService → grain fold) ────────────────────────

/// <summary>One typed item straight out of an extraction call, pre-fold.</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.MappedItem")]
public sealed record MappedItem
{
    [Id(0)] public string SourceId { get; init; } = string.Empty;
    [Id(1)] public string? Type { get; init; }
    [Id(2)] public string? Persona { get; init; }
    [Id(3)] public string? Summary { get; init; }
    [Id(4)] public double? Weight { get; init; }
    [Id(5)] public List<string> Participants { get; init; } = new();
    [Id(6)] public List<ConceptDraft> Concepts { get; init; } = new();
    [Id(7)] public List<int> SourceChunks { get; init; } = new();
}

[GenerateSerializer, Alias("PartyTown.Services.Import.ConceptDraft")]
public sealed record ConceptDraft
{
    [Id(0)] public string Name { get; init; } = string.Empty;
    [Id(1)] public List<string> Aliases { get; init; } = new();
}

/// <summary>Chunk the model flagged as pure OOC/meta, with its stated reason.</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.ChunkDiscard")]
public sealed record ChunkDiscard
{
    [Id(0)] public int ChunkIndex { get; init; }
    [Id(1)] public string Reason { get; init; } = string.Empty;
}

/// <summary>An extraction call (or item) that degraded instead of aborting the run.</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.DegradedRecord")]
public sealed record DegradedRecord
{
    [Id(0)] public string SourceId { get; init; } = string.Empty;
    [Id(1)] public string Reason { get; init; } = string.Empty;
    [Id(2)] public string? Detail { get; init; }
}

/// <summary>Everything one scene's extraction calls produced, before the fold.</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.SceneMapResult")]
public sealed record SceneMapResult
{
    [Id(0)] public List<MappedItem> Items { get; init; } = new();
    [Id(1)] public List<ChunkDiscard> Discards { get; init; } = new();
    [Id(2)] public List<DegradedRecord> Degraded { get; init; } = new();
    [Id(3)] public int LlmCalls { get; init; }
    [Id(4)] public int UnparseableCalls { get; init; }
}

// ── fold output + persisted run record ──────────────────────────────────────────

/// <summary>An incoming item dropped by dedup, with the telling that was kept.</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.DedupDrop")]
public sealed record DedupDrop
{
    [Id(0)] public Guid KeptItemId { get; init; }
    [Id(1)] public string KeptSummary { get; init; } = string.Empty;
    [Id(2)] public string DroppedSummary { get; init; } = string.Empty;
    [Id(3)] public string DroppedSourceId { get; init; } = string.Empty;
}

/// <summary>Latest run of one scene — the inputs the live ledger derivation needs
/// (LLM discards, pre-dedup salience) plus transparency records.</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.SceneRunRecord")]
public sealed class SceneRunRecord
{
    [Id(0)] public Guid SceneId { get; set; }
    [Id(1)] public DateTimeOffset RanAt { get; set; }
    [Id(2)] public int LlmCalls { get; set; }
    [Id(3)] public int UnparseableCalls { get; set; }
    [Id(4)] public List<ChunkDiscard> LlmDiscards { get; set; } = new();

    /// <summary>Chunks that fed any extracted episode, pre-dedup — they were judged
    /// salient even if the episode itself was later dropped as a re-telling.</summary>
    [Id(5)] public List<int> SalientChunks { get; set; } = new();

    [Id(6)] public List<DedupDrop> Deduped { get; set; } = new();
    [Id(7)] public List<DegradedRecord> Degraded { get; set; } = new();
}

/// <summary>Disposition of one chunk in the conservation ledger.</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.ChunkRouting")]
public sealed record ChunkRouting
{
    [Id(0)] public int ChunkIndex { get; init; }
    [Id(1)] public string Category { get; init; } = string.Empty;
    [Id(2)] public string Disposition { get; init; } = string.Empty;
    [Id(3)] public string? Reason { get; init; }
    [Id(4)] public Guid? SceneId { get; init; }
}

/// <summary>What one scene run did: the scene's current draft slice plus a routing
/// record per chunk in its range.</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.SceneRunResult")]
public sealed record SceneRunResult
{
    [Id(0)] public Guid SceneId { get; init; }
    [Id(1)] public DateTimeOffset RanAt { get; init; }
    [Id(2)] public int LlmCalls { get; init; }
    [Id(3)] public int UnparseableCalls { get; init; }
    [Id(4)] public int ReplacedItems { get; init; }
    [Id(5)] public List<ImportDraftItem> Items { get; init; } = new();
    [Id(6)] public List<ChunkRouting> ChunkRoutings { get; init; } = new();
    [Id(7)] public List<DedupDrop> Deduped { get; init; } = new();
    [Id(8)] public List<DegradedRecord> Degraded { get; init; } = new();
}

// ── commit (per-scene, deterministic — ADR 0017 slice 3) ────────────────────────

/// <summary>Where this session commits: pinned on the first scene commit, every later
/// commit extends the same Room. Whole-import rollback deletes this Room.</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.ImportCommitTarget")]
public sealed record ImportCommitTarget
{
    [Id(0)] public Guid PartyId { get; init; }
    [Id(1)] public Guid RoomId { get; init; }
    [Id(2)] public string RoomName { get; init; } = string.Empty;

    /// <summary>Participant standing in for the export's human ("user" role chunks).</summary>
    [Id(3)] public Guid UserParticipantId { get; init; }
}

/// <summary>Commit request body. Only the first commit of a session needs a party;
/// later commits inherit the pinned target (a different party is refused).</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.SceneCommitRequest")]
public sealed record SceneCommitRequest
{
    [Id(0)] public Guid? PartyId { get; init; }
    [Id(1)] public string? RoomName { get; init; }
}

/// <summary>Everything the deterministic commit planner needs for one scene, read from
/// the grain in a single call.</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.SceneCommitInput")]
public sealed record SceneCommitInput
{
    [Id(0)] public Guid SessionId { get; init; }
    [Id(1)] public string? FileName { get; init; }
    [Id(2)] public ImportSettings Settings { get; init; } = new();
    [Id(3)] public ImportScene Scene { get; init; } = new();

    /// <summary>This scene's draft slice (the items being committed).</summary>
    [Id(4)] public List<ImportDraftItem> Items { get; init; } = new();

    /// <summary>All trait items across the draft — the as-drafted cast universe and the
    /// card source for persona minting (replaced by finalize in issue 03).</summary>
    [Id(5)] public List<ImportDraftItem> TraitItems { get; init; } = new();

    [Id(6)] public List<ImportChunk> Chunks { get; init; } = new();
    [Id(7)] public List<ChunkRouting> ChunkRoutings { get; init; } = new();
    [Id(8)] public List<ImportConcept> Concepts { get; init; } = new();
    [Id(9)] public ImportCommitTarget? Target { get; init; }

    /// <summary>Canonical cast name → persona id for personas earlier commits minted.</summary>
    [Id(10)] public Dictionary<string, Guid> CommittedPersonas { get; init; } = new();

    /// <summary>The session registry cast — aliases, routing and match decisions. The
    /// planner resolves participants through it and executes recorded match decisions.</summary>
    [Id(11)] public List<RegistryCastEntry> Cast { get; init; } = new();

    /// <summary>Canonical cast name → reviewed persona card (finalize output). Minting
    /// requires a card — commit never generates one (finalize is the pre-commit step).</summary>
    [Id(12)] public Dictionary<string, PersonaCardDraft> Cards { get; init; } = new();
}

[GenerateSerializer, Alias("PartyTown.Services.Import.SceneCommitResult")]
public sealed record SceneCommitResult
{
    [Id(0)] public Guid SceneId { get; init; }
    [Id(1)] public Guid PartyId { get; init; }
    [Id(2)] public Guid RoomId { get; init; }
    [Id(3)] public DateTimeOffset CommittedAt { get; init; }
    [Id(4)] public int MessagesWritten { get; init; }
    [Id(5)] public int EventsWritten { get; init; }
    [Id(6)] public int RecollectionsWritten { get; init; }
    [Id(7)] public int ConceptLinks { get; init; }
    [Id(8)] public List<string> PersonasMinted { get; init; } = new();
    [Id(9)] public int CorrectionsRecorded { get; init; }

    /// <summary>Episode participants no cast name or concept-routed registry entry
    /// claimed — their Events carry no Recollection for them (counted, never minted).</summary>
    [Id(10)] public List<string> UnmatchedParticipants { get; init; } = new();

    /// <summary>Already-minted/matched personas whose card this commit refreshed.</summary>
    [Id(11)] public List<string> PersonasUpdated { get; init; } = new();

    /// <summary>Card updates skipped because the live persona drifted from the last
    /// committed snapshot — a human edited it in the library, and human edits win.</summary>
    [Id(12)] public List<string> PersonaUpdatesSkipped { get; init; } = new();
}

// ── read models ─────────────────────────────────────────────────────────────────

[GenerateSerializer, Alias("PartyTown.Services.Import.ChunkSummary")]
public sealed record ChunkSummary
{
    [Id(0)] public int Index { get; init; }
    [Id(1)] public string Role { get; init; } = string.Empty;
    [Id(2)] public string Category { get; init; } = string.Empty;
    [Id(3)] public int Chars { get; init; }
    [Id(4)] public string Head { get; init; } = string.Empty;
}

[GenerateSerializer, Alias("PartyTown.Services.Import.ImportSessionOverview")]
public sealed record ImportSessionOverview
{
    [Id(0)] public Guid Id { get; init; }
    [Id(1)] public string? FileName { get; init; }
    [Id(2)] public DateTimeOffset CreatedAt { get; init; }
    [Id(3)] public int ChunkCount { get; init; }
    [Id(4)] public Dictionary<string, int> Categories { get; init; } = new();
    [Id(5)] public List<ChunkSummary> Chunks { get; init; } = new();
    [Id(6)] public ImportSettings Settings { get; init; } = new();
    [Id(7)] public List<ImportScene> Scenes { get; init; } = new();
    [Id(8)] public int DraftItemCount { get; init; }
    [Id(9)] public ImportCommitTarget? CommitTarget { get; init; }
}

[GenerateSerializer, Alias("PartyTown.Services.Import.ImportDraftView")]
public sealed record ImportDraftView
{
    [Id(0)] public List<ImportDraftItem> Items { get; init; } = new();
    [Id(1)] public List<ImportConcept> Concepts { get; init; } = new();

    /// <summary>Reviewed persona cards (finalize output + human edits), keyed nowhere —
    /// each card names its persona.</summary>
    [Id(2)] public List<PersonaCardDraft> Cards { get; init; } = new();
}

[GenerateSerializer, Alias("PartyTown.Services.Import.ImportLedger")]
public sealed record ImportLedger
{
    [Id(0)] public int TotalChunks { get; init; }
    [Id(1)] public Dictionary<string, int> ByDisposition { get; init; } = new();

    /// <summary>True when every chunk has exactly one disposition and the counts sum to
    /// the total — the conservation invariant.</summary>
    [Id(2)] public bool Reconciles { get; init; }

    [Id(3)] public List<ChunkRouting> Chunks { get; init; } = new();
}

/// <summary>Everything <see cref="SceneMapService"/> needs to run one scene's extraction.</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.SceneRunInput")]
public sealed record SceneRunInput
{
    [Id(0)] public Guid SceneId { get; init; }
    [Id(1)] public List<ImportChunk> Chunks { get; init; } = new();

    /// <summary>Dossier text for the canon path; empty unless the scene opted in.</summary>
    [Id(2)] public string SystemInstruction { get; init; } = string.Empty;

    [Id(3)] public string? Note { get; init; }

    /// <summary>Confirmed registry cast, injected into map calls as canonical-name hints.
    /// Empty for a registry-less run (always valid).</summary>
    [Id(4)] public List<RegistryCastEntry> Cast { get; init; } = new();

    /// <summary>Confirmed registry concepts, injected as canonical vocabulary.</summary>
    [Id(5)] public List<ImportConcept> Concepts { get; init; } = new();
}

/// <summary>Grain state for one import session. Plain persistent state, not event-sourced.</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.ImportSessionState")]
public sealed class ImportSessionState
{
    [Id(0)] public bool Initialized { get; set; }
    [Id(1)] public string? FileName { get; set; }
    [Id(2)] public DateTimeOffset CreatedAt { get; set; }
    [Id(3)] public string SystemInstruction { get; set; } = string.Empty;
    [Id(4)] public List<ImportChunk> Chunks { get; set; } = new();
    [Id(5)] public ImportSettings Settings { get; set; } = new();
    [Id(6)] public List<ImportScene> Scenes { get; set; } = new();
    [Id(7)] public List<ImportDraftItem> Items { get; set; } = new();
    [Id(8)] public List<ImportConcept> Concepts { get; set; } = new();
    [Id(9)] public List<SceneRunRecord> RunRecords { get; set; } = new();
    [Id(10)] public ImportCommitTarget? CommitTarget { get; set; }

    /// <summary>Canonical cast name → persona id, one entry per persona this session has
    /// minted. Feeds later commits (no re-mint) and event participant resolution.</summary>
    [Id(11)] public Dictionary<string, Guid> CommittedPersonas { get; set; } = new();

    /// <summary>Registry cast: confirmed entries + open scene-run proposals.</summary>
    [Id(12)] public List<RegistryCastEntry> Cast { get; set; } = new();

    /// <summary>Canonical cast name → reviewed persona card (finalize output).</summary>
    [Id(13)] public Dictionary<string, PersonaCardDraft> Cards { get; set; } = new();
}
