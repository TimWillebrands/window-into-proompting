using Microsoft.AspNetCore.Mvc;
using PartyTown.Grains;
using PartyTown.World.Growth;
using PartyTown.World.Places;

namespace PartyTown.Controllers;

/// <summary>World growth (ADR 0027): footfall on Locales and Openings, for the author's Growing panel.</summary>
[ApiController]
[Route("worlds/{partyId:guid}/growth")]
public sealed class GrowthController(IGrainFactory grains) : ControllerBase
{
    private static readonly TimeSpan RecentlyOpened = TimeSpan.FromDays(7);

    [HttpGet]
    public async Task<ActionResult<GrowthReport>> GetGrowth(Guid partyId)
    {
        var growth = grains.GetGrain<IGrowthGrain>(partyId);
        var readings = await growth.GetReadingsAsync();
        var wouldOpen = await growth.GetWouldOpenAsync();
        var snapshot = await grains.GetGrain<IPlacesGrain>(partyId).GetSnapshotAsync();
        var names = (await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync()).ToDictionary(c => c.Id, c => c.Name);
        string? District(Place p) => snapshot.Districts.FirstOrDefault(d => d.Id == p.DistrictId)?.Name;
        var rows = readings
            .Select(r => (Reading: r, Place: snapshot.Places.FirstOrDefault(p => p.Id == r.LocaleId)))
            .Where(x => x.Place is { Kind: PlaceKinds.Locale })
            .Select(x => new GrowthRow(
                x.Reading.LocaleId, x.Place!.Name, District(x.Place),
                x.Reading.Footfall, x.Reading.People, x.Reading.Days, x.Reading.Missing,
                x.Reading.Contributors.Select(id => names.GetValueOrDefault(id, "someone")).ToList(),
                wouldOpen.GetValueOrDefault(x.Reading.LocaleId)))
            .ToList();
        var since = snapshot.Now - (long)RecentlyOpened.TotalMilliseconds;
        var opened = snapshot.Places
            .Where(p => p.OpenedAt >= since && p.Kind == PlaceKinds.Public)
            .Select(p => new OpenedRow(p.Id, p.Name, District(p), p.Archetype, p.OpenedAt!.Value))
            .ToList();
        return Ok(new GrowthReport(await growth.GetAutoAsync(), rows, opened));
    }

    /// <summary>Thing descriptions the catalogue had no model for, newest first: what to curate next.</summary>
    [HttpGet("catalogue-misses")]
    public async Task<ActionResult<List<string>>> GetCatalogueMisses(Guid partyId)
        => Ok(await grains.GetGrain<IPlacesGrain>(partyId).GetCatalogueMissesAsync());

    /// <summary>Author: open a Locale now, from its name's kit (or a given archetype).</summary>
    [HttpPost("open/{localeId:guid}")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Place>> Open(Guid partyId, Guid localeId, [FromQuery] string? archetype)
    {
        var place = await grains.GetGrain<IPlacesGrain>(partyId).OpenAsync(localeId, archetype, "author");
        return place is null ? Conflict("Not a Locale, or the world has opened enough for now.") : Ok(place);
    }

    /// <summary>Author veto: an opened Place goes back offscreen.</summary>
    [HttpPost("close/{placeId:guid}")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Close(Guid partyId, Guid placeId)
        => await grains.GetGrain<IPlacesGrain>(partyId).CloseAsync(placeId) ? NoContent() : Conflict("Only opened Places can close.");

    /// <summary>Author: open Locales that pass the gates automatically (one a day), or only log them.</summary>
    [HttpPut("auto")]
    public async Task<ActionResult> SetAuto(Guid partyId, [FromBody] GrowthAutoRequest request)
    {
        await grains.GetGrain<IGrowthGrain>(partyId).SetAutoAsync(request.On);
        return NoContent();
    }
}

/// <summary>One Locale's footfall: how close it is to opening and who's pulling it there.</summary>
public sealed record GrowthRow(
    Guid LocaleId, string Name, string? District, double Footfall, int People, int Days,
    string? Missing, List<string> Contributors, string? WouldOpenOn);

/// <summary>A Locale opened in the last week, which the author can still close.</summary>
public sealed record OpenedRow(Guid PlaceId, string Name, string? District, string? Archetype, long OpenedAt);

public sealed record GrowthReport(bool Auto, List<GrowthRow> Rows, List<OpenedRow> Opened);

public sealed record GrowthAutoRequest(bool On);
