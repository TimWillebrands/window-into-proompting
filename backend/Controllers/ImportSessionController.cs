using Microsoft.AspNetCore.Mvc;
using PartyTown.Grains;
using PartyTown.Services.Import;

namespace PartyTown.Controllers;

/// <summary>
/// REST surface of the import scene workshop (ADR 0017): upload an AI Studio export,
/// define scenes over the chunk strip, run the extraction map, read and edit the folded
/// draft. Replaces the legacy <see cref="ImportController"/> flow, which stays untouched
/// until the workshop surface is complete (deleted in the final slice).
/// </summary>
[ApiController]
[Route("import")]
public sealed class ImportSessionController(
    IGrainFactory grains,
    IImportRunCoordinator runCoordinator,
    IImportPersonaFinalizeService personaFinalize,
    ImportCommitService commit,
    IImportCorrectionStore corrections,
    PartyTown.Services.Realtime.IPartyRealtimeHub realtimeHub,
    ILogger<ImportSessionController> logger) : ControllerBase
{
    // The reference export is ~1.5 MB; leave generous headroom over Kestrel's default.
    private const long MaxUploadBytes = 64 * 1024 * 1024;

    /// <summary>
    /// Uploads a Gemini AI Studio export (raw JSON body), parses it into deterministic IR
    /// chunks and creates a new import session. No LLM involved.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(MaxUploadBytes)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ImportSessionOverview>> Create(
        [FromQuery] string? fileName, [FromBody] System.Text.Json.JsonElement export)
    {
        ImportSource source;
        try
        {
            source = AiStudioImportParser.Parse(export.GetRawText(), fileName);
        }
        catch (FormatException ex)
        {
            return BadRequest(ex.Message);
        }

        var id = Guid.NewGuid();
        var grain = grains.GetGrain<IImportSessionGrain>(id);
        try
        {
            var overview = await grain.InitializeAsync(source);
            return CreatedAtAction(nameof(GetOverview), new { id }, overview);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Returns the session overview: chunk strip, categories, scenes, settings.</summary>
    [HttpGet("{id:guid}")]
    public Task<ActionResult<ImportSessionOverview>> GetOverview(Guid id)
        => OnSession(id, g => g.GetOverviewAsync());

    /// <summary>Abandons the session; the uncommitted draft evaporates.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var grain = grains.GetGrain<IImportSessionGrain>(id);
        if (!await grain.IsInitializedAsync()) return NotFound();
        runCoordinator.CancelSession(id);
        await grain.DeleteAsync();
        return NoContent();
    }

    /// <summary>
    /// Opens the realtime websocket for this session: scene-run progress, pre-fold draft
    /// item previews and run completion, in the standard realtime envelope format. On
    /// connect the client receives a snapshot of runs already in flight.
    /// </summary>
    [HttpGet("{id:guid}/ws")]
    public async Task Realtime(Guid id)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsync("Expected a websocket upgrade request.", HttpContext.RequestAborted);
            return;
        }

        var grain = grains.GetGrain<IImportSessionGrain>(id);
        if (!await grain.IsInitializedAsync())
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        await realtimeHub.HandleImportConnectionAsync(id, socket, HttpContext.RequestAborted);
    }

    /// <summary>Returns one chunk's full text.</summary>
    [HttpGet("{id:guid}/chunks/{index:int}")]
    public Task<ActionResult<ImportChunk>> GetChunk(Guid id, int index)
        => OnSession(id, g => g.GetChunkAsync(index));

    // ── scenes ───────────────────────────────────────────────────────────────────

    /// <summary>Creates a scene: a chunk range plus an optional operator note.</summary>
    [HttpPost("{id:guid}/scenes")]
    public Task<ActionResult<ImportScene>> CreateScene(Guid id, [FromBody] SceneDefinition definition)
        => OnSession(id, g => g.CreateSceneAsync(definition));

    /// <summary>Updates a scene's range, note or dossier flag. Rerun to refresh its draft slice.</summary>
    [HttpPut("{id:guid}/scenes/{sceneId:guid}")]
    public Task<ActionResult<ImportScene>> UpdateScene(Guid id, Guid sceneId, [FromBody] SceneDefinition definition)
        => OnSession(id, g => g.UpdateSceneAsync(sceneId, definition));

    /// <summary>Deletes a scene along with its draft slice and run record.</summary>
    [HttpDelete("{id:guid}/scenes/{sceneId:guid}")]
    public async Task<IActionResult> DeleteScene(Guid id, Guid sceneId)
    {
        var grain = grains.GetGrain<IImportSessionGrain>(id);
        if (!await grain.IsInitializedAsync()) return NotFound();
        try
        {
            await grain.DeleteSceneAsync(sceneId);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    /// <summary>
    /// Runs (or reruns) the extraction map for one scene — General-tier LLM calls, then a
    /// deterministic fold into the draft. Rerunning replaces the scene's draft slice;
    /// other scenes are never touched. The run is detached from this request: a dropped
    /// connection does not abort it, progress streams over the session websocket, and a
    /// re-POST while the scene is running joins the run in flight.
    /// </summary>
    [HttpPost("{id:guid}/scenes/{sceneId:guid}/run")]
    public async Task<ActionResult<SceneRunResult>> RunScene(Guid id, Guid sceneId)
    {
        var grain = grains.GetGrain<IImportSessionGrain>(id);
        if (!await grain.IsInitializedAsync()) return NotFound();

        logger.LogInformation("Import session {SessionId}: running scene {SceneId}", id, sceneId);
        try
        {
            return Ok(await runCoordinator.RunAsync(id, sceneId));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            // Committed scenes are settled — regeneration is a draft-only power.
            return Conflict(ex.Message);
        }
    }

    // ── registry + cards ─────────────────────────────────────────────────────────

    /// <summary>Returns the session registry: cast entries (aliases, persona-vs-concept
    /// routing, match-or-mint state) and concepts, confirmed ones plus open proposals.</summary>
    [HttpGet("{id:guid}/registry")]
    public Task<ActionResult<ImportRegistryView>> GetRegistry(Guid id)
        => OnSession(id, g => g.GetRegistryAsync());

    /// <summary>Creates or edits a registry cast entry (aliases, routing, confirmation).
    /// Scene runs propose entries; this is where the human blesses or reshapes them.</summary>
    [HttpPut("{id:guid}/registry/cast/{name}")]
    public Task<ActionResult<RegistryCastEntry>> UpsertCastEntry(Guid id, string name, [FromBody] RegistryCastEdit edit)
        => OnSession(id, g => g.UpsertCastEntryAsync(name, edit));

    /// <summary>Records the match-or-mint decision for one cast member ("match" with an
    /// optional personaId — defaults to the proposal — or "mint"). Commit executes the
    /// recorded decision; it never prompts.</summary>
    [HttpPost("{id:guid}/registry/cast/{name}/decision")]
    public Task<ActionResult<RegistryCastEntry>> DecideCastMatch(Guid id, string name, [FromBody] CastMatchDecision decision)
        => OnSession(id, g => g.DecideCastMatchAsync(name, decision));

    /// <summary>Creates or edits a registry concept (aliases, confirmation). Confirmed
    /// concepts are injected into map calls as canonical vocabulary.</summary>
    [HttpPut("{id:guid}/registry/concepts/{name}")]
    public Task<ActionResult<ImportConcept>> UpdateConcept(Guid id, string name, [FromBody] RegistryConceptEdit edit)
        => OnSession(id, g => g.UpdateConceptAsync(name, edit));

    /// <summary>
    /// Runs the persona finalize step for one scene — one General-tier LLM call per
    /// touched persona to compress/dedup its trait list and synthesise a one-line Bio.
    /// The resulting cards land in the draft for review; commit executes the reviewed
    /// card and refuses to mint without one. Personas whose traits are unchanged since
    /// the last finalize are skipped; human-edited cards only receive a proposal.
    /// </summary>
    [HttpPost("{id:guid}/scenes/{sceneId:guid}/finalize")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SceneFinalizeResult>> FinalizeScene(Guid id, Guid sceneId, CancellationToken ct)
    {
        var grain = grains.GetGrain<IImportSessionGrain>(id);
        if (!await grain.IsInitializedAsync()) return NotFound();

        SceneCommitInput input;
        try
        {
            input = await grain.GetSceneCommitInputAsync(sceneId);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }

        var required = ImportCommitPlanner.PersonasNeedingCards(input);
        var existingCards = input.Cards.Values.ToList();
        var skipped = new List<string>();
        var fresh = new List<PersonaCardDraft>();
        foreach (var requirement in required)
        {
            var existing = existingCards.FirstOrDefault(c =>
                c.Persona.Equals(requirement.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null && existing.TraitFingerprint == requirement.TraitFingerprint)
            {
                skipped.Add(requirement.Name);
                continue;
            }
            var finalized = await personaFinalize.FinalizeAsync(requirement.Name, requirement.Traits, ct);
            fresh.Add(new PersonaCardDraft
            {
                Persona = requirement.Name,
                SystemPrompt = finalized.SystemPrompt,
                Bio = finalized.Bio,
                TraitFingerprint = requirement.TraitFingerprint,
            });
        }

        var stored = fresh.Count > 0
            ? await grain.StoreFinalizedCardsAsync(fresh)
            : new List<PersonaCardDraft>();
        var cards = stored
            .Concat(skipped.Select(name => existingCards.First(c =>
                c.Persona.Equals(name, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        logger.LogInformation(
            "Import session {SessionId} scene {SceneId} finalized: {Fresh} cards refreshed, {Skipped} current",
            id, sceneId, fresh.Count, skipped.Count);
        return Ok(new SceneFinalizeResult { Cards = cards, Skipped = skipped, LlmCalls = fresh.Count });
    }

    /// <summary>Edits a persona card draft (human review of finalize output — the edit
    /// wins over later re-finalizes), or accepts a pending re-finalize proposal.</summary>
    [HttpPut("{id:guid}/cards/{name}")]
    public Task<ActionResult<PersonaCardDraft>> UpdateCard(Guid id, string name, [FromBody] PersonaCardEdit edit)
        => OnSession(id, g => g.UpdateCardAsync(name, edit));

    // ── commit ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Commits a reviewed scene: deterministic writes only (no LLM). Creates the Room on
    /// the session's first commit (body must carry <c>partyId</c>; later commits inherit
    /// the pinned target), appends the scene's history-routed messages, mints
    /// personas-as-drafted, seeds AGE Events/Recollections/Concepts at anchor-scheme
    /// timestamps, and appends the correction ledger. Retryable; out-of-chunk-order
    /// commits are allowed (ordering is the operator's trade).
    /// </summary>
    [HttpPost("{id:guid}/scenes/{sceneId:guid}/commit")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SceneCommitResult>> CommitScene(
        Guid id, Guid sceneId, [FromBody] SceneCommitRequest request, CancellationToken ct)
    {
        var grain = grains.GetGrain<IImportSessionGrain>(id);
        if (!await grain.IsInitializedAsync()) return NotFound();
        try
        {
            return Ok(await commit.CommitSceneAsync(id, sceneId, request, ct));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    /// <summary>Whole-import rollback: deletes the committed Room (messages + AGE events)
    /// and returns every scene to editable draft. Minted personas stay in the library.</summary>
    [HttpDelete("{id:guid}/commit")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ImportCommitTarget>> RollbackCommits(Guid id, CancellationToken ct)
    {
        var grain = grains.GetGrain<IImportSessionGrain>(id);
        if (!await grain.IsInitializedAsync()) return NotFound();
        try
        {
            return Ok(await commit.RollbackAsync(id, ct));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    /// <summary>Reads the durable correction ledger for this session. Works after the
    /// session grain is disposed — the ledger outlives the workshop by design.</summary>
    [HttpGet("{id:guid}/corrections")]
    public async Task<ActionResult<IReadOnlyList<ImportCorrection>>> GetCorrections(Guid id, CancellationToken ct)
        => Ok(await corrections.ListAsync(id, ct));

    // ── draft ────────────────────────────────────────────────────────────────────

    /// <summary>Returns the accumulated draft: all items across scenes plus merged concepts.</summary>
    [HttpGet("{id:guid}/draft")]
    public Task<ActionResult<ImportDraftView>> GetDraft(Guid id)
        => OnSession(id, g => g.GetDraftAsync());

    /// <summary>Returns the per-chunk conservation ledger. Every chunk is accounted for:
    /// event-routed | folded | history-only | discarded(reason) | unprocessed.</summary>
    [HttpGet("{id:guid}/ledger")]
    public Task<ActionResult<ImportLedger>> GetLedger(Guid id)
        => OnSession(id, g => g.GetLedgerAsync());

    /// <summary>Edits one draft item: flip an episode's routing (event | history | auto),
    /// tweak its summary or weight. Marks are draft state, not deletions.</summary>
    [HttpPatch("{id:guid}/draft/{itemId:guid}")]
    public Task<ActionResult<ImportDraftItem>> UpdateDraftItem(Guid id, Guid itemId, [FromBody] DraftItemEdit edit)
        => OnSession(id, g => g.UpdateDraftItemAsync(itemId, edit));

    /// <summary>Updates per-import settings (weight floor, anchor, spacing). Floor routing
    /// is re-derived for episodes the human has not overridden.</summary>
    [HttpPut("{id:guid}/settings")]
    public Task<ActionResult<ImportSettings>> UpdateSettings(Guid id, [FromBody] ImportSettingsUpdate update)
        => OnSession(id, g => g.UpdateSettingsAsync(update));

    // ── plumbing ─────────────────────────────────────────────────────────────────

    /// <summary>Runs one grain call with uniform not-found / validation mapping.</summary>
    private async Task<ActionResult<T>> OnSession<T>(Guid id, Func<IImportSessionGrain, Task<T>> call)
    {
        var grain = grains.GetGrain<IImportSessionGrain>(id);
        if (!await grain.IsInitializedAsync()) return NotFound();
        try
        {
            return Ok(await call(grain));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            // Committed scenes (and their items) are settled — surfaced as a conflict.
            return Conflict(ex.Message);
        }
    }
}
