using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Auth;
using PartyTown.World;
using PartyTown.World.Work;
using PartyTown.World.Work.Companies;
using PartyTown.World.Work.Cdu;
using PartyTown.World.Work.Markets;

namespace PartyTown.Controllers;

/// <summary>Companies (ADR 0030): found one, staff it, read its Book, and trade as anyone on the staff.</summary>
[ApiController]
[Route("worlds/{partyId:guid}/companies")]
public sealed class CompaniesController(IGrainFactory grains, Desks desks, Cytheurgy site, Actors actors) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<List<CompanySummary>>> GetCompanies(Guid partyId)
    {
        var companies = await grains.GetGrain<ICompaniesGrain>(partyId).GetAllAsync();
        var result = new List<CompanySummary>();
        foreach (var c in companies) result.Add(Summary(c, await desks.StaffAsync(partyId, c.Id)));
        return Ok(result);
    }

    /// <summary>Start a Company: "trades AI questions" on predictions, crypto or stocks, with its own office.</summary>
    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpPost]
    [ProducesResponseType<CompanySummary>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CompanySummary>> Found(Guid partyId, [FromBody] FoundCompanyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Mandate)) return BadRequest("A Company needs a name and a mandate.");
        if (CompanyKinds.IsCdu(request.Kind))
        {
            var cdu = await site.FoundAsync(partyId, request.Name.Trim(), request.Mandate.Trim());
            return cdu is null ? Conflict("That name is taken.") : Ok(Summary(cdu, new List<ParticipantView>()));
        }
        if (!MarketKinds.All.Contains(request.Market)) return BadRequest($"Market is one of: {string.Join(", ", MarketKinds.All)}.");
        if (request.Market == MarketKinds.Stocks && string.IsNullOrWhiteSpace(request.Focus)) return BadRequest("Stocks need a focus to search for.");
        var company = await desks.FoundAsync(partyId, request.Name.Trim(), request.Mandate.Trim(), request.Market, request.Focus?.Trim(), request.Capital);
        if (company is null) return Conflict("That name is taken.");
        return Ok(Summary(company, new List<ParticipantView>()));
    }

    /// <summary>Put someone on the staff. Days like "Mon-Fri"; hours like "09:00-17:00".</summary>
    [HttpPut("{companyId:guid}/staff/{participantId:guid}")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CompanySummary>> Hire(Guid partyId, Guid companyId, Guid participantId, [FromBody] HireRequest request)
    {
        await actors.ActAsync(User, partyId, participantId);
        if ((await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync()).All(c => c.Id != participantId)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Title)) return BadRequest("A job needs a title.");
        if (Shifts.ParseHours(request.Hours) is not var (start, end)) return BadRequest("Hours like 09:00-17:00, within one day.");
        if (await desks.HireAsync(partyId, companyId, participantId, request.Title.Trim(), Shifts.ParseDays(request.Days), start, end) is null) return NotFound();
        return await OneAsync(partyId, companyId);
    }

    [HttpDelete("{companyId:guid}/staff/{participantId:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CompanySummary>> Dismiss(Guid partyId, Guid companyId, Guid participantId)
    {
        await actors.ActAsync(User, partyId, participantId);
        var occupation = grains.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, participantId));
        if ((await occupation.GetAsync()).Job?.CompanyId != companyId) return NotFound();
        await occupation.SetAsync(null);
        return await OneAsync(partyId, companyId);
    }

    /// <summary>One order as a staff member. Percent is of the Book to buy, of the Position to sell.</summary>
    [HttpPost("{companyId:guid}/trades")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TradeSummary>> Trade(Guid partyId, Guid companyId, [FromBody] TradeRequest request)
    {
        await actors.ActAsync(User, partyId, request.TraderId);
        var who = (await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync()).FirstOrDefault(c => c.Id == request.TraderId);
        if (who is null || await grains.GetGrain<ICompaniesGrain>(partyId).GetAsync(companyId) is not { } company) return NotFound();
        if ((await grains.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, who.Id)).GetAsync()).Job?.CompanyId != companyId) return Forbid();
        var side = request.Side?.ToLowerInvariant() switch { "buy" => TradeSide.Buy, "sell" => TradeSide.Sell, _ => (TradeSide?)null };
        if (side is not { } chosen) return BadRequest("Side is buy or sell.");
        var result = await desks.TradeAsync(partyId, companyId, new TradeOrder(request.InstrumentId, chosen, request.Percent / 100, who.Id, who.Name,
            string.IsNullOrWhiteSpace(request.Thesis) ? null : WorldStream.Clip(request.Thesis.Trim(), 300),
            company.Market == MarketKinds.Predictions && request.Chance is { } chance ? Math.Clamp(chance, 0, 100) / 100 : null));
        if (result.Trade is not { } trade) return BadRequest(result.Refusal);
        await grains.GetGrain<IWorldClockGrain>(partyId).RecordAsync(new WorldActivity
        {
            PersonaId = who.Id, PersonaName = who.Name, App = DeskApp.AppId, Action = "trade", Summary = DeskScreen.Did(trade, company), TargetId = companyId,
        });
        return Ok(Of(trade));
    }

    private async Task<ActionResult<CompanySummary>> OneAsync(Guid partyId, Guid companyId)
        => await grains.GetGrain<ICompaniesGrain>(partyId).GetAsync(companyId) is { } company
            ? Ok(Summary(company, await desks.StaffAsync(partyId, companyId)))
            : NotFound();

    private static CompanySummary Summary(Company c, List<ParticipantView> staff) => new(
        c.Id, c.Name, CompanyKinds.IsCdu(c.Kind) ? CompanyKinds.Cdu : c.Kind, c.Mandate, c.Market, c.Focus, c.Workplace,
        c.Book.Capital, c.Book.Cash, c.Book.Value, BookRules.ScoreOf(c.Book, null), c.MarkedAt,
        staff.Select(s => new StaffSummary(s.Id, s.Name, BookRules.ScoreOf(c.Book, s.Id))).ToList(),
        c.Book.Positions.Select(p => new PositionSummary(p.InstrumentId, p.Title, p.Units, p.Cost, p.Mark, p.Value, p.Fresh)).ToList(),
        c.Watchlist.Select(q => new WatchSummary(q.Id, q.Title, q.Bid, q.Ask, q.Previous, q.Open)).ToList(),
        c.Book.Trades.AsEnumerable().Reverse().Take(30).Select(Of).ToList());

    private static TradeSummary Of(Trade t) => new(
        t.Id, t.At, t.TraderId, t.TraderName, t.InstrumentId, t.Title, t.Side.ToString(), t.Units, t.Price, t.Fee, t.Realized, t.Thesis, t.Forecast, t.Score);
}

/// <param name="Market">predictions, crypto or stocks.</param>
/// <param name="Focus">Search term or coins/tickers; optional except for stocks.</param>
public sealed record FoundCompanyRequest(string Name, string Mandate, string Market, string? Focus, double? Capital, string? Kind = null);

public sealed record HireRequest(string Title, string? Days, string Hours);

/// <param name="Side">buy or sell.</param>
/// <param name="Chance">On a question: percent chance it pays out.</param>
public sealed record TradeRequest(Guid TraderId, string InstrumentId, string Side, double Percent, string? Thesis, double? Chance);

public sealed record StaffSummary(Guid ParticipantId, string Name, double? Brier);

public sealed record PositionSummary(string InstrumentId, string Title, double Units, double Cost, double Mark, double Value, bool Fresh);

public sealed record WatchSummary(string InstrumentId, string Title, double Bid, double Ask, double? Previous, bool Open);

public sealed record TradeSummary(
    Guid Id, long At, Guid? TraderId, string? TraderName, string InstrumentId, string Title, string Side,
    double Units, double Price, double Fee, double? Realized, string? Thesis, double? Forecast, double? Brier);

public sealed record CompanySummary(
    Guid Id, string Name, string Kind, string Mandate, string Market, string? Focus, string Workplace,
    double Capital, double Cash, double Value, double? Brier, long? MarkedAt,
    List<StaffSummary> Staff, List<PositionSummary> Positions, List<WatchSummary> Watchlist, List<TradeSummary> Trades);
