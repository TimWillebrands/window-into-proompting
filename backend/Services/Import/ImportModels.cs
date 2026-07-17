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
}

[GenerateSerializer, Alias("PartyTown.Services.Import.ImportDraftView")]
public sealed record ImportDraftView
{
    [Id(0)] public List<ImportDraftItem> Items { get; init; } = new();
    [Id(1)] public List<ImportConcept> Concepts { get; init; } = new();
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
}
