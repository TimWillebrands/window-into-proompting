using Microsoft.AspNetCore.Mvc;
using PartyTown.Grains;
using PartyTown.World.Growth;
using PartyTown.World.Places;

namespace PartyTown.Controllers;

/// <summary>World growth (ADR 0027): footfall on Locales, for the author's Growing panel.</summary>
[ApiController]
[Route("worlds/{partyId:guid}/growth")]
public sealed class GrowthController(IGrainFactory grains) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<GrowthRow>>> GetGrowth(Guid partyId)
    {
        var readings = await grains.GetGrain<IGrowthGrain>(partyId).GetReadingsAsync();
        var wouldOpen = await grains.GetGrain<IGrowthGrain>(partyId).GetWouldOpenAsync();
        var snapshot = await grains.GetGrain<IPlacesGrain>(partyId).GetSnapshotAsync();
        var names = (await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync()).ToDictionary(c => c.Id, c => c.Name);
        var rows = readings
            .Select(r => (Reading: r, Place: snapshot.Places.FirstOrDefault(p => p.Id == r.LocaleId)))
            .Where(x => x.Place is { Kind: PlaceKinds.Locale })
            .Select(x => new GrowthRow(
                x.Reading.LocaleId, x.Place!.Name,
                snapshot.Districts.FirstOrDefault(d => d.Id == x.Place.DistrictId)?.Name,
                x.Reading.Footfall, x.Reading.People, x.Reading.Days, x.Reading.Missing,
                x.Reading.Contributors.Select(id => names.GetValueOrDefault(id, "someone")).ToList(),
                wouldOpen.GetValueOrDefault(x.Reading.LocaleId)))
            .ToList();
        return Ok(rows);
    }
}

/// <summary>One Locale's footfall: how close it is to opening and who's pulling it there.</summary>
public sealed record GrowthRow(
    Guid LocaleId, string Name, string? District, double Footfall, int People, int Days,
    string? Missing, List<string> Contributors, string? WouldOpenOn);
