using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PartyTown.World.Work.Cdu;

namespace PartyTown.Controllers;

/// <summary>The CDU for spectators (ADR 0039): who spoke to it, what it said, its trunk and its PR. The One judges on GitHub.</summary>
[ApiController]
[Route("worlds/{partyId:guid}/cdu")]
public sealed class CduController(IGrainFactory grains, ICdu cdu, Cytheurgy site) : ControllerBase
{
    private const int ShownBack = 400;

    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType<CduOverview>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CduOverview>> GetOverview(Guid partyId, CancellationToken ct)
    {
        var status = await site.StatusAsync(ct);
        var state = await grains.GetGrain<ICduGrain>(partyId).GetAsync();
        return Ok(new CduOverview(
            site.Enabled, status is not null, status?.Awake ?? false, status?.Hearing, status?.Model,
            status?.Trunk?.Ahead.Select(c => new CduCommitSummary(c.Sha, c.Subject, c.Author)).ToList() ?? new List<CduCommitSummary>(),
            status?.Pr is { } pr ? new CduPrSummary(pr.Number, pr.Url, pr.Title, pr.State, pr.Checks) : null,
            status?.Allowance is { } a ? new CduAllowanceSummary(a.Usd, a.CapUsd) : null,
            state.Exchanges.AsEnumerable().Reverse().Select(e => new CduExchangeSummary(
                e.TurnId, e.SpeakerId, e.SpeakerName, e.Kind, e.Text, e.Status, e.Reply, e.Asked, e.Answered, e.Commits, e.At, e.DoneAt, e.Model)).ToList(),
            status?.Models?.Select(m => new CduModelSummary(m.Id, m.Label, m.Tier, m.In, m.Out)).ToList() ?? new List<CduModelSummary>()));
    }

    /// <summary>The CDU's transcript so far; realtime frames carry what comes after.</summary>
    [AllowAnonymous]
    [HttpGet("lines")]
    [ProducesResponseType<List<CduLineSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CduLineSummary>>> GetLines(Guid partyId, CancellationToken ct)
    {
        try
        {
            var status = await site.StatusAsync(ct);
            var lines = await cdu.LinesAsync(Math.Max(0, (status?.Lines ?? 0) - ShownBack), ct);
            return Ok(lines.Select(l => new CduLineSummary(l.N, l.At, l.Kind, l.Text)).ToList());
        }
        catch (CduException) { return Ok(new List<CduLineSummary>()); }
    }
}

public sealed record CduOverview(
    bool Enabled, bool Up, bool Awake, string? Hearing, string? Model, List<CduCommitSummary> Trunk, CduPrSummary? Pr,
    CduAllowanceSummary? Allowance, List<CduExchangeSummary> Exchanges, List<CduModelSummary> Models);

public sealed record CduModelSummary(string Id, string Label, string? Tier, double In, double Out);

public sealed record CduCommitSummary(string Sha, string Subject, string Author);

public sealed record CduPrSummary(int Number, string Url, string Title, string State, string Checks);

public sealed record CduAllowanceSummary(double Usd, double CapUsd);

public sealed record CduExchangeSummary(
    string TurnId, Guid SpeakerId, string SpeakerName, string Kind, string Text, string Status, string? Reply, bool Asked, bool Answered,
    int Commits, long At, long? DoneAt, string? Model);

public sealed record CduLineSummary(long N, long At, string Kind, string Text);
