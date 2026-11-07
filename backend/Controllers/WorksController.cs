using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PartyTown.Services.Auth;
using PartyTown.World.Work.Works;

namespace PartyTown.Controllers;

/// <summary>The Works for spectators and the world-spirits (ADR 0039): what's on the bench and what waits to be merged.</summary>
[ApiController]
[Route("worlds/{partyId:guid}/works")]
public sealed class WorksController(IGrainFactory grains, IRunner runner, Worksite site) : ControllerBase
{
    private const int ShownBack = 400;

    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType<WorksOverview>(StatusCodes.Status200OK)]
    public async Task<ActionResult<WorksOverview>> GetOverview(Guid partyId, CancellationToken ct)
    {
        var status = await site.StatusAsync(ct);
        var commissions = await Commissions(partyId).GetAllAsync();
        return Ok(new WorksOverview(
            site.Enabled, status is not null, status?.Busy ?? false, status?.ReserveUsd,
            status?.Models.Select(m => new WorksModel(m.Id, m.Label, m.In, m.Out)).ToList() ?? new List<WorksModel>(),
            commissions.AsEnumerable().Reverse().Select(Summary).ToList()));
    }

    /// <summary>The builder's transcript so far; realtime frames carry what comes after.</summary>
    [AllowAnonymous]
    [HttpGet("commissions/{commissionId:guid}/lines")]
    [ProducesResponseType<List<WorksLineSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<WorksLineSummary>>> GetLines(Guid partyId, Guid commissionId, CancellationToken ct)
    {
        if (await Commissions(partyId).GetAsync(commissionId) is not { RunId: { } runId }) return NotFound();
        try
        {
            var read = await runner.ReadAsync(runId, 0, ct);
            return Ok(read.Events.TakeLast(ShownBack).Select(e => new WorksLineSummary(e.N, e.At, e.Kind, e.Text)).ToList());
        }
        catch (RunnerException) { return Ok(new List<WorksLineSummary>()); }
    }

    /// <summary>A Proposition as a patch. Merging is <c>curl ... | git am</c>, and the Runner sees it land.</summary>
    [AllowAnonymous]
    [HttpGet("commissions/{commissionId:guid}/patch")]
    [Produces("text/plain")]
    [ProducesResponseType<string>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetPatch(Guid partyId, Guid commissionId, CancellationToken ct)
    {
        if (await Commissions(partyId).GetAsync(commissionId) is not { Proposition: { } number }) return NotFound();
        return Content(await runner.PatchAsync(number, ct), "text/plain");
    }

    /// <summary>The world-spirits turn a Proposition down, and with it whatever is stacked on it.</summary>
    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpPost("commissions/{commissionId:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Reject(Guid partyId, Guid commissionId, [FromBody] RejectRequest request, CancellationToken ct)
    {
        if (await Commissions(partyId).GetAsync(commissionId) is not { } commission) return NotFound();
        return await site.RejectAsync(partyId, commission, request.Note?.Trim(), ct) ? NoContent() : NotFound();
    }

    private ICommissionsGrain Commissions(Guid partyId) => grains.GetGrain<ICommissionsGrain>(partyId);

    private static CommissionSummary Summary(Commission c) => new(
        c.Id, c.ArchitectId, c.ArchitectName, c.Kind.ToString(), c.Title, c.Why, c.Brief, c.Model, c.BudgetUsd, c.Status.ToString(), c.Report,
        c.SpentUsd, c.Tokens, c.Files, c.Added, c.Removed, c.Paths, c.Stop, c.Error, c.Proposition,
        c.Checks.Select(k => new CommissionCheckSummary(k.Name, k.Passed, k.Seconds, k.Detail)).ToList(), c.Note, c.FiledAt, c.UpdatedAt);
}

public sealed record WorksOverview(bool Enabled, bool BuilderUp, bool BuilderBusy, double? ReserveUsd, List<WorksModel> Models, List<CommissionSummary> Commissions);

public sealed record WorksModel(string Id, string Label, double In, double Out);

public sealed record CommissionCheckSummary(string Name, bool Passed, int Seconds, string Detail);

public sealed record CommissionSummary(
    Guid Id, Guid ArchitectId, string ArchitectName, string Kind, string Title, string Why, string Brief, string Model, double BudgetUsd,
    string Status, string? Report, double SpentUsd, long Tokens, int Files, int Added, int Removed, List<string> Paths,
    string? Stop, string? Error, int? Proposition, List<CommissionCheckSummary> Checks, string? Note, long FiledAt, long UpdatedAt);

public sealed record WorksLineSummary(long N, long At, string Kind, string Text);

public sealed record RejectRequest(string? Note);
