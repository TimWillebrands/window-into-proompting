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

    // Registry/card text is user-supplied — cap it at the trust boundary (multi-user
    // instance), same rationale as NormalizeNote.
    private const int MaxNameChars = 64;
    private const int MaxAliases = 12;
    private const int MaxCardChars = 8_000;
    private const int MaxBioChars = 280;

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
            // Only human-confirmed registry entries feed the map — proposals stay
            // suggestions until blessed. Empty registry = valid run (quality dial).
            Cast = state.State.Cast.Where(c => c.Confirmed).ToList(),
            Concepts = state.State.Concepts.Where(c => c.Confirmed).ToList(),
        });
    }

    public async Task<SceneRunResult> ApplySceneRunAsync(Guid sceneId, SceneMapResult mapResult)
    {
        EnsureInitialized();
        var result = ImportFold.Apply(state.State, sceneId, mapResult, DateTimeOffset.UtcNow);
        await ProposeLibraryMatchesAsync();
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
            Cards = state.State.Cards.Values.ToList(),
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

    // ── registry (ADR 0017: optional context, nouns not narrative) ───────────────

    public Task<ImportRegistryView> GetRegistryAsync()
    {
        EnsureInitialized();
        return Task.FromResult(new ImportRegistryView
        {
            Cast = state.State.Cast.ToList(),
            Concepts = state.State.Concepts.ToList(),
        });
    }

    public async Task<RegistryCastEntry> UpsertCastEntryAsync(string name, RegistryCastEdit edit)
    {
        EnsureInitialized();
        var canonical = NormalizeShortText(name, MaxNameChars)
            ?? throw new ArgumentException("Cast name cannot be empty.");

        var entry = FindCastEntry(canonical);
        if (entry is null)
        {
            entry = new RegistryCastEntry
            {
                Name = canonical,
                // A manual add is its own confirmation; scene-run proposals arrive
                // through the fold with Confirmed = false instead.
                Confirmed = edit.Confirmed ?? true,
            };
            state.State.Cast.Add(entry);
        }

        if (edit.Aliases is not null)
        {
            entry.Aliases = edit.Aliases
                .Select(a => NormalizeShortText(a, MaxNameChars))
                .Where(a => a is not null && !a.Equals(entry.Name, StringComparison.OrdinalIgnoreCase))
                .Select(a => a!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxAliases)
                .ToList();

            // Aliasing a name absorbs its standalone proposal ("Denyse" aliased onto
            // "Denise" removes the auto-proposed "Denyse" entry). Confirmed entries and
            // decided matches are human work — never absorbed silently.
            state.State.Cast.RemoveAll(c =>
                c != entry && !c.Confirmed && c.MatchState == CastMatchStates.Unmatched &&
                entry.Aliases.Contains(c.Name, StringComparer.OrdinalIgnoreCase));
        }

        if (edit.Routing is not null)
        {
            if (edit.Routing is not (CastRoutingModes.Persona or CastRoutingModes.Concept))
                throw new ArgumentException($"Unknown cast routing '{edit.Routing}' — use persona or concept.");
            entry.Routing = edit.Routing;
        }

        if (edit.Confirmed is { } confirmed)
            entry.Confirmed = confirmed;

        await ProposeLibraryMatchesAsync();
        await state.WriteStateAsync();
        return entry;
    }

    public async Task<RegistryCastEntry> DecideCastMatchAsync(string name, CastMatchDecision decision)
    {
        EnsureInitialized();
        var entry = FindCastEntry(name.Trim())
            ?? throw new KeyNotFoundException($"Cast entry '{name}' not found.");
        if (state.State.CommittedPersonas.Keys.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"'{entry.Name}' is already committed — match decisions are settled with the persona.");

        switch (decision.Decision.ToLowerInvariant())
        {
            case "mint":
                entry.MatchState = CastMatchStates.ConfirmedMint;
                entry.MatchedPersonaId = null;
                break;
            case "match":
                var personaId = decision.PersonaId ?? entry.ProposedPersonaId
                    ?? throw new ArgumentException($"No personaId given and no proposal exists for '{entry.Name}'.");
                var personaRoot = GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty);
                if (!await personaRoot.HasPersonaId(personaId))
                    throw new ArgumentException($"Persona {personaId} not found in the library.");
                entry.MatchState = CastMatchStates.ConfirmedMatch;
                entry.MatchedPersonaId = personaId;
                break;
            default:
                throw new ArgumentException($"Unknown decision '{decision.Decision}' — use match or mint.");
        }

        // Deciding is confirming: the entry stops being a mere proposal.
        entry.Confirmed = true;
        await state.WriteStateAsync();
        return entry;
    }

    public async Task<ImportConcept> UpdateConceptAsync(string name, RegistryConceptEdit edit)
    {
        EnsureInitialized();
        var canonical = NormalizeShortText(name, MaxNameChars)
            ?? throw new ArgumentException("Concept name cannot be empty.");
        var concept = state.State.Concepts.FirstOrDefault(c =>
            c.Name.Equals(canonical, StringComparison.OrdinalIgnoreCase) ||
            c.Aliases.Contains(canonical, StringComparer.OrdinalIgnoreCase));
        if (concept is null)
        {
            concept = new ImportConcept { Name = canonical, Confirmed = edit.Confirmed ?? true };
            state.State.Concepts.Add(concept);
        }

        if (edit.Aliases is not null)
            concept.Aliases = edit.Aliases
                .Select(a => NormalizeShortText(a, MaxNameChars))
                .Where(a => a is not null && !a.Equals(concept.Name, StringComparison.OrdinalIgnoreCase))
                .Select(a => a!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxAliases)
                .ToList();
        if (edit.Confirmed is { } confirmed)
            concept.Confirmed = confirmed;

        await state.WriteStateAsync();
        return concept;
    }

    // ── persona cards (finalize output, human-reviewed) ──────────────────────────

    public async Task<List<PersonaCardDraft>> StoreFinalizedCardsAsync(List<PersonaCardDraft> cards)
    {
        EnsureInitialized();
        var stored = new List<PersonaCardDraft>();
        foreach (var incoming in cards)
        {
            var existing = FindCard(incoming.Persona);
            if (existing is { HumanEdited: true })
            {
                // Human edits win — a re-finalize may only propose, never clobber.
                existing.ProposedSystemPrompt = incoming.SystemPrompt;
                existing.ProposedBio = incoming.Bio;
                existing.TraitFingerprint = incoming.TraitFingerprint;
                existing.FinalizedAt = DateTimeOffset.UtcNow;
                stored.Add(existing);
                continue;
            }

            var card = existing ?? new PersonaCardDraft { Persona = incoming.Persona };
            card.SystemPrompt = Truncate(incoming.SystemPrompt, MaxCardChars);
            card.Bio = incoming.Bio is null ? null : Truncate(incoming.Bio, MaxBioChars);
            card.ProposedSystemPrompt = null;
            card.ProposedBio = null;
            card.TraitFingerprint = incoming.TraitFingerprint;
            card.FinalizedAt = DateTimeOffset.UtcNow;
            if (existing is null) state.State.Cards[card.Persona] = card;
            stored.Add(card);
        }
        await state.WriteStateAsync();
        return stored;
    }

    public async Task<PersonaCardDraft> UpdateCardAsync(string personaName, PersonaCardEdit edit)
    {
        EnsureInitialized();
        var card = FindCard(personaName.Trim());

        if (edit.AcceptProposal == true)
        {
            if (card?.ProposedSystemPrompt is null)
                throw new ArgumentException($"No pending proposal on the card for '{personaName}'.");
            card.SystemPrompt = card.ProposedSystemPrompt;
            card.Bio = card.ProposedBio;
            card.ProposedSystemPrompt = null;
            card.ProposedBio = null;
            card.HumanEdited = false;
            await state.WriteStateAsync();
            return card;
        }

        if (card is null)
        {
            if (string.IsNullOrWhiteSpace(edit.SystemPrompt))
                throw new KeyNotFoundException($"No card for '{personaName}' — finalize first or provide a systemPrompt.");
            card = new PersonaCardDraft { Persona = personaName.Trim() };
            state.State.Cards[card.Persona] = card;
        }

        if (edit.SystemPrompt is not null)
        {
            if (string.IsNullOrWhiteSpace(edit.SystemPrompt))
                throw new ArgumentException("Card systemPrompt cannot be empty.");
            card.SystemPrompt = Truncate(edit.SystemPrompt.Trim(), MaxCardChars);
        }
        if (edit.Bio is not null)
            card.Bio = string.IsNullOrWhiteSpace(edit.Bio) ? null : Truncate(edit.Bio.Trim(), MaxBioChars);

        card.HumanEdited = true;
        await state.WriteStateAsync();
        return card;
    }

    public async Task MarkCardsCommittedAsync(List<string> personaNames)
    {
        EnsureInitialized();
        foreach (var name in personaNames)
        {
            if (FindCard(name) is not { } card) continue;
            card.CommittedSystemPrompt = card.SystemPrompt;
            card.CommittedBio = card.Bio;
        }
        await state.WriteStateAsync();
    }

    /// <summary>Propose a library persona for unmatched persona-routed cast entries
    /// (exact + Levenshtein over name and aliases). Skips personas this session itself
    /// minted — matching your own output is noise, not a decision.</summary>
    private async Task ProposeLibraryMatchesAsync()
    {
        var unproposed = state.State.Cast
            .Where(c => c.Routing == CastRoutingModes.Persona
                        && c.MatchState == CastMatchStates.Unmatched
                        && c.ProposedPersonaId is null)
            .ToList();
        if (unproposed.Count == 0) return;

        var library = await GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty).GetAllMetadata();
        var ownMints = state.State.CommittedPersonas.Values.ToHashSet();
        foreach (var entry in unproposed)
        {
            var match = ImportCastMatcher.ProposeMatch(entry.Name, entry.Aliases, library);
            if (match is null || ownMints.Contains(match.Id)) continue;
            entry.MatchState = CastMatchStates.Proposed;
            entry.ProposedPersonaId = match.Id;
            entry.ProposedPersonaName = match.Name;
        }
    }

    private RegistryCastEntry? FindCastEntry(string name)
        => state.State.Cast.FirstOrDefault(c =>
            c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            c.Aliases.Contains(name, StringComparer.OrdinalIgnoreCase));

    private PersonaCardDraft? FindCard(string personaName)
        => state.State.Cards.Values.FirstOrDefault(c =>
            c.Persona.Equals(personaName, StringComparison.OrdinalIgnoreCase));

    private static string? NormalizeShortText(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

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
            Cast = state.State.Cast.ToList(),
            Cards = new Dictionary<string, PersonaCardDraft>(state.State.Cards),
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
        // Card CommittedSystemPrompt/Bio snapshots are kept deliberately: rollback leaves
        // minted personas in the library, so the human-drift guard still needs a baseline.
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

    [Alias("GetRegistryAsync")]
    Task<ImportRegistryView> GetRegistryAsync();

    [Alias("UpsertCastEntryAsync")]
    Task<RegistryCastEntry> UpsertCastEntryAsync(string name, RegistryCastEdit edit);

    [Alias("DecideCastMatchAsync")]
    Task<RegistryCastEntry> DecideCastMatchAsync(string name, CastMatchDecision decision);

    [Alias("UpdateConceptAsync")]
    Task<ImportConcept> UpdateConceptAsync(string name, RegistryConceptEdit edit);

    [Alias("StoreFinalizedCardsAsync")]
    Task<List<PersonaCardDraft>> StoreFinalizedCardsAsync(List<PersonaCardDraft> cards);

    [Alias("UpdateCardAsync")]
    Task<PersonaCardDraft> UpdateCardAsync(string personaName, PersonaCardEdit edit);

    [Alias("MarkCardsCommittedAsync")]
    Task MarkCardsCommittedAsync(List<string> personaNames);

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
