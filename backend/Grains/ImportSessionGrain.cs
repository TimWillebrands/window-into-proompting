using PartyTown.Services.Import;

namespace PartyTown.Grains;

/// <summary>
/// One import session (ADR 0017): plain persistent-state grain holding the IR chunks
/// (stored once; scenes reference by index range), scene definitions, accumulated draft
/// and run records. The grain never calls the LLM — the controller runs the map through
/// <see cref="ISceneMapService"/> and hands the result to <see cref="ApplySceneRunAsync"/>,
/// which folds it in via the pure <see cref="ImportFold"/>. Disposable after the workshop.
/// </summary>
public sealed class ImportSessionGrain(
    [PersistentState(stateName: "importSession", storageName: "imports")]
    IPersistentState<ImportSessionState> state,
    ILogger<ImportSessionGrain> logger)
    : Grain, IImportSessionGrain
{
    private const int MaxNoteChars = 4_000;

    public async Task<ImportSessionOverview> InitializeAsync(ImportSource source)
    {
        if (state.State.Initialized)
            throw new InvalidOperationException("Import session already initialized.");
        if (source.Chunks.Count == 0)
            throw new ArgumentException("Export contains no chunks.");

        state.State = new ImportSessionState
        {
            Initialized = true,
            FileName = source.FileName,
            CreatedAt = DateTimeOffset.UtcNow,
            SystemInstruction = source.SystemInstruction,
            Chunks = source.Chunks.ToList(),
            Settings = new ImportSettings { Anchor = DateTimeOffset.UtcNow.AddDays(-30) },
        };
        await state.WriteStateAsync();

        logger.LogInformation(
            "Import session {SessionId} created: {Chunks} chunks from '{FileName}'",
            this.GetPrimaryKey(), source.Chunks.Count, source.FileName);
        return BuildOverview();
    }

    public Task<bool> IsInitializedAsync() => Task.FromResult(state.State.Initialized);

    public Task<ImportSessionOverview> GetOverviewAsync()
    {
        EnsureInitialized();
        return Task.FromResult(BuildOverview());
    }

    public Task<ImportChunk> GetChunkAsync(int index)
    {
        EnsureInitialized();
        if (index < 0 || index >= state.State.Chunks.Count)
            throw new KeyNotFoundException($"Chunk {index} out of range (0..{state.State.Chunks.Count - 1}).");
        return Task.FromResult(state.State.Chunks[index]);
    }

    // ── scenes ───────────────────────────────────────────────────────────────────

    public async Task<ImportScene> CreateSceneAsync(SceneDefinition definition)
    {
        EnsureInitialized();
        ValidateScene(definition);
        var scene = new ImportScene
        {
            Id = Guid.NewGuid(),
            FromChunk = definition.FromChunk,
            ToChunk = definition.ToChunk,
            Note = NormalizeNote(definition.Note),
            IncludeDossier = definition.IncludeDossier,
        };
        state.State.Scenes.Add(scene);
        await state.WriteStateAsync();
        return scene;
    }

    public async Task<ImportScene> UpdateSceneAsync(Guid sceneId, SceneDefinition definition)
    {
        EnsureInitialized();
        var scene = FindScene(sceneId);
        EnsureNotCommitted(scene);
        ValidateScene(definition);
        scene.FromChunk = definition.FromChunk;
        scene.ToChunk = definition.ToChunk;
        scene.Note = NormalizeNote(definition.Note);
        scene.IncludeDossier = definition.IncludeDossier;
        await state.WriteStateAsync();
        return scene;
    }

    public async Task DeleteSceneAsync(Guid sceneId)
    {
        EnsureInitialized();
        var scene = FindScene(sceneId);
        EnsureNotCommitted(scene);
        state.State.Scenes.Remove(scene);
        state.State.Items.RemoveAll(i => i.SceneId == sceneId);
        state.State.RunRecords.RemoveAll(r => r.SceneId == sceneId);
        await state.WriteStateAsync();
    }

    // ── scene runs ───────────────────────────────────────────────────────────────

    public Task<SceneRunInput> GetSceneRunInputAsync(Guid sceneId)
    {
        EnsureInitialized();
        var scene = FindScene(sceneId);
        // Pre-LLM guard: refuse before the controller spends map calls; the fold repeats
        // the check for callers that skip this read.
        EnsureNotCommitted(scene);
        return Task.FromResult(new SceneRunInput
        {
            SceneId = scene.Id,
            Chunks = state.State.Chunks
                .Where(c => c.Index >= scene.FromChunk && c.Index <= scene.ToChunk)
                .ToList(),
            SystemInstruction = scene.IncludeDossier ? state.State.SystemInstruction : string.Empty,
            Note = scene.Note,
        });
    }

    public async Task<SceneRunResult> ApplySceneRunAsync(Guid sceneId, SceneMapResult mapResult)
    {
        EnsureInitialized();
        var result = ImportFold.Apply(state.State, sceneId, mapResult, DateTimeOffset.UtcNow);
        await state.WriteStateAsync();
        logger.LogInformation(
            "Import session {SessionId} scene {SceneId} folded: {Items} items ({Replaced} replaced, {Deduped} deduped, {Degraded} degraded) from {Calls} calls",
            this.GetPrimaryKey(), sceneId, result.Items.Count, result.ReplacedItems, result.Deduped.Count, result.Degraded.Count, result.LlmCalls);
        return result;
    }

    // ── draft + ledger ───────────────────────────────────────────────────────────

    public Task<ImportDraftView> GetDraftAsync()
    {
        EnsureInitialized();
        return Task.FromResult(new ImportDraftView
        {
            Items = state.State.Items.ToList(),
            Concepts = state.State.Concepts.ToList(),
        });
    }

    public Task<ImportLedger> GetLedgerAsync()
    {
        EnsureInitialized();
        return Task.FromResult(ImportFold.BuildLedger(state.State));
    }

    public async Task<ImportDraftItem> UpdateDraftItemAsync(Guid itemId, DraftItemEdit edit)
    {
        EnsureInitialized();
        var item = ImportFold.Edit(state.State, itemId, edit);
        await state.WriteStateAsync();
        return item;
    }

    public async Task<ImportSettings> UpdateSettingsAsync(ImportSettingsUpdate update)
    {
        EnsureInitialized();
        var current = state.State.Settings;
        var next = new ImportSettings
        {
            WeightFloor = update.WeightFloor ?? current.WeightFloor,
            Anchor = update.Anchor ?? current.Anchor,
            SpacingMinutes = update.SpacingMinutes ?? current.SpacingMinutes,
        };
        if (next.WeightFloor is < 0.0 or > 1.0)
            throw new ArgumentException("Weight floor must be between 0 and 1.");
        if (next.SpacingMinutes <= 0)
            throw new ArgumentException("Spacing must be positive.");

        state.State.Settings = next;
        ImportFold.ReapplySettings(state.State);
        await state.WriteStateAsync();
        return next;
    }

    // ── commit (ADR 0017 slice 3) ────────────────────────────────────────────────
    // The grain owns commit *state* only; ImportCommitService performs the external
    // writes (Room, personas, AGE, correction ledger) and drives these transitions.

    public Task<SceneCommitInput> GetSceneCommitInputAsync(Guid sceneId)
    {
        EnsureInitialized();
        var scene = FindScene(sceneId);
        EnsureNotCommitted(scene);
        if (scene.RunCount == 0)
            throw new InvalidOperationException($"Scene {sceneId} has not been run — nothing to commit.");

        return Task.FromResult(new SceneCommitInput
        {
            SessionId = this.GetPrimaryKey(),
            FileName = state.State.FileName,
            Settings = state.State.Settings,
            Scene = scene,
            Items = state.State.Items.Where(i => i.SceneId == sceneId).ToList(),
            TraitItems = state.State.Items.Where(i => i.Type == DraftItemTypes.Trait).ToList(),
            Chunks = state.State.Chunks
                .Where(c => c.Index >= scene.FromChunk && c.Index <= scene.ToChunk)
                .ToList(),
            ChunkRoutings = ImportFold.BuildChunkRoutings(state.State, scene),
            Concepts = state.State.Concepts.ToList(),
            Target = state.State.CommitTarget,
            CommittedPersonas = new Dictionary<string, Guid>(state.State.CommittedPersonas),
        });
    }

    public async Task SetCommitTargetAsync(ImportCommitTarget target)
    {
        EnsureInitialized();
        if (state.State.CommitTarget is { } existing && existing.RoomId != target.RoomId)
            throw new InvalidOperationException(
                $"Session already commits into room {existing.RoomId} — one Room per import session.");
        state.State.CommitTarget = target;
        await state.WriteStateAsync();
    }

    public Task<ImportCommitTarget?> GetCommitTargetAsync()
        => Task.FromResult(state.State.CommitTarget);

    public async Task RecordCommittedPersonasAsync(Dictionary<string, Guid> personas)
    {
        EnsureInitialized();
        foreach (var (name, id) in personas)
            state.State.CommittedPersonas[name] = id;
        await state.WriteStateAsync();
    }

    public async Task RecordSceneMessagesWrittenAsync(Guid sceneId)
    {
        EnsureInitialized();
        FindScene(sceneId).MessagesWritten = true;
        await state.WriteStateAsync();
    }

    public async Task<ImportScene> CompleteSceneCommitAsync(Guid sceneId)
    {
        EnsureInitialized();
        var scene = FindScene(sceneId);
        scene.Committed = true;
        scene.CommittedAt = DateTimeOffset.UtcNow;
        await state.WriteStateAsync();
        logger.LogInformation(
            "Import session {SessionId} scene {SceneId} committed", this.GetPrimaryKey(), sceneId);
        return scene;
    }

    /// <summary>Whole-import rollback bookkeeping: the Room and its AGE events are already
    /// gone (ImportCommitService), so every scene returns to editable draft. Minted
    /// personas keep their library entries — deterministic ids make a re-commit converge.</summary>
    public async Task ResetCommitsAsync()
    {
        EnsureInitialized();
        state.State.CommitTarget = null;
        state.State.CommittedPersonas.Clear();
        foreach (var scene in state.State.Scenes)
        {
            scene.Committed = false;
            scene.CommittedAt = null;
            scene.MessagesWritten = false;
        }
        await state.WriteStateAsync();
    }

    public async Task DeleteAsync()
    {
        await state.ClearStateAsync();
        DeactivateOnIdle();
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private void EnsureInitialized()
    {
        if (!state.State.Initialized)
            throw new InvalidOperationException("Import session not initialized.");
    }

    private ImportScene FindScene(Guid sceneId)
        => state.State.Scenes.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new KeyNotFoundException($"Scene {sceneId} not found.");

    private static void EnsureNotCommitted(ImportScene scene)
    {
        if (scene.Committed)
            throw new InvalidOperationException(
                $"Scene {scene.Id} is committed — committed scenes are settled (rollback = delete the Room).");
    }

    private void ValidateScene(SceneDefinition definition)
    {
        var max = state.State.Chunks.Count - 1;
        if (definition.FromChunk < 0 || definition.ToChunk > max || definition.FromChunk > definition.ToChunk)
            throw new ArgumentException($"Scene range must satisfy 0 <= from <= to <= {max}.");
    }

    // User free-text crosses the trust boundary here — cap it (multi-user instance).
    private static string? NormalizeNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note)) return null;
        var trimmed = note.Trim();
        return trimmed.Length <= MaxNoteChars ? trimmed : trimmed[..MaxNoteChars];
    }

    private ImportSessionOverview BuildOverview()
    {
        var s = state.State;
        return new ImportSessionOverview
        {
            Id = this.GetPrimaryKey(),
            FileName = s.FileName,
            CreatedAt = s.CreatedAt,
            ChunkCount = s.Chunks.Count,
            Categories = s.Chunks
                .GroupBy(c => c.Category)
                .ToDictionary(g => g.Key, g => g.Count()),
            Chunks = s.Chunks.Select(c => new ChunkSummary
            {
                Index = c.Index,
                Role = c.Role,
                Category = c.Category,
                Chars = c.Text.Length,
                Head = FirstLine(c.Text),
            }).ToList(),
            Settings = s.Settings,
            Scenes = s.Scenes.ToList(),
            DraftItemCount = s.Items.Count,
            CommitTarget = s.CommitTarget,
        };
    }

    private static string FirstLine(string text)
    {
        var nl = text.IndexOf('\n');
        var line = (nl >= 0 ? text[..nl] : text).Trim();
        return line.Length <= 80 ? line : line[..80] + "…";
    }
}

/// <summary>Grain contract for one import workshop session.</summary>
[Alias("backend.IImportSessionGrain")]
public interface IImportSessionGrain : IGrainWithGuidKey
{
    [Alias("InitializeAsync")]
    Task<ImportSessionOverview> InitializeAsync(ImportSource source);

    [Alias("IsInitializedAsync")]
    Task<bool> IsInitializedAsync();

    [Alias("GetOverviewAsync")]
    Task<ImportSessionOverview> GetOverviewAsync();

    [Alias("GetChunkAsync")]
    Task<ImportChunk> GetChunkAsync(int index);

    [Alias("CreateSceneAsync")]
    Task<ImportScene> CreateSceneAsync(SceneDefinition definition);

    [Alias("UpdateSceneAsync")]
    Task<ImportScene> UpdateSceneAsync(Guid sceneId, SceneDefinition definition);

    [Alias("DeleteSceneAsync")]
    Task DeleteSceneAsync(Guid sceneId);

    [Alias("GetSceneRunInputAsync")]
    Task<SceneRunInput> GetSceneRunInputAsync(Guid sceneId);

    [Alias("ApplySceneRunAsync")]
    Task<SceneRunResult> ApplySceneRunAsync(Guid sceneId, SceneMapResult mapResult);

    [Alias("GetDraftAsync")]
    Task<ImportDraftView> GetDraftAsync();

    [Alias("GetLedgerAsync")]
    Task<ImportLedger> GetLedgerAsync();

    [Alias("UpdateDraftItemAsync")]
    Task<ImportDraftItem> UpdateDraftItemAsync(Guid itemId, DraftItemEdit edit);

    [Alias("UpdateSettingsAsync")]
    Task<ImportSettings> UpdateSettingsAsync(ImportSettingsUpdate update);

    [Alias("GetSceneCommitInputAsync")]
    Task<SceneCommitInput> GetSceneCommitInputAsync(Guid sceneId);

    [Alias("SetCommitTargetAsync")]
    Task SetCommitTargetAsync(ImportCommitTarget target);

    [Alias("GetCommitTargetAsync")]
    Task<ImportCommitTarget?> GetCommitTargetAsync();

    [Alias("RecordCommittedPersonasAsync")]
    Task RecordCommittedPersonasAsync(Dictionary<string, Guid> personas);

    [Alias("RecordSceneMessagesWrittenAsync")]
    Task RecordSceneMessagesWrittenAsync(Guid sceneId);

    [Alias("CompleteSceneCommitAsync")]
    Task<ImportScene> CompleteSceneCommitAsync(Guid sceneId);

    [Alias("ResetCommitsAsync")]
    Task ResetCommitsAsync();

    [Alias("DeleteAsync")]
    Task DeleteAsync();
}
