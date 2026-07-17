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
    ISceneMapService sceneMap,
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
        await grain.DeleteAsync();
        return NoContent();
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
    }

    /// <summary>
    /// Runs (or reruns) the extraction map for one scene — General-tier LLM calls, then a
    /// deterministic fold into the draft. Rerunning replaces the scene's draft slice;
    /// other scenes are never touched.
    /// </summary>
    [HttpPost("{id:guid}/scenes/{sceneId:guid}/run")]
    public async Task<ActionResult<SceneRunResult>> RunScene(Guid id, Guid sceneId, CancellationToken ct)
    {
        var grain = grains.GetGrain<IImportSessionGrain>(id);
        if (!await grain.IsInitializedAsync()) return NotFound();

        SceneRunInput input;
        try
        {
            input = await grain.GetSceneRunInputAsync(sceneId);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }

        logger.LogInformation(
            "Import session {SessionId}: running scene {SceneId} ({Chunks} chunks, dossier: {Dossier})",
            id, sceneId, input.Chunks.Count, input.SystemInstruction.Length > 0);

        var map = await sceneMap.RunAsync(input, ct);
        var result = await grain.ApplySceneRunAsync(sceneId, map);
        return Ok(result);
    }

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
    }
}
