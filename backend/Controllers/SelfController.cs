using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PartyTown.Services.Auth;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.World;
using PartyTown.World.Self;

namespace PartyTown.Controllers;

[ApiController]
[Route("worlds/{partyId:guid}")]
public sealed class SelfController(IGrainFactory grains) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("selves")]
    public async Task<ActionResult<List<SelfSummary>>> GetSelves(Guid partyId)
    {
        var cast = await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync();
        var now = DateTimeOffset.Now;
        var minute = now.Hour * 60 + now.Minute;
        var personas = cast.Where(c => c.DefaultDriver == DriverKind.LLM && c.Id != Narrator.PersonaId).ToList();
        var states = await Task.WhenAll(personas.Select(c =>
            grains.GetGrain<IPersonaSelfGrain>(WorldKeys.Life(partyId, c.Id)).GetStateAsync()));
        var places = grains.GetGrain<World.Places.IPlacesGrain>(partyId);
        var asleep = await Task.WhenAll(personas.Select(c => places.IsAsleepAsync(c.Id)));
        return Ok(personas.Select((c, i) => (c, s: states[i], sleeping: asleep[i], body: Bio.Now(states[i], now))).Select(x => new SelfSummary(
                x.c.Id, x.c.Name, x.s.Mood, Bio.Energy(x.s.Energy, x.body), x.s.Preoccupation,
                Math.Round(SelfMath.Battery(x.s, now), 2),
                x.sleeping ? SelfMath.Asleep : SelfMath.Availability(x.s, now),
                x.sleeping ? "Asleep" : x.s.Beats.LastOrDefault(b => b.Minute <= minute)?.Text,
                x.s.Beats.Select(b => $"{b.Minute / 60:00}:{b.Minute % 60:00} {b.Text}").ToList(),
                x.s.Threads,
                x.body.Felt && !x.sleeping ? Math.Round(x.body.Fed, 2) : null,
                x.body.Felt && !x.sleeping ? Math.Round(x.body.Rested, 2) : null))
            .ToList());
    }

    [AllowAnonymous]

    [HttpGet("voice/lines")]
    public async Task<ActionResult<List<VoiceLine>>> GetVoiceLines(Guid partyId, [FromQuery] int limit = 40)
        => Ok(await grains.GetGrain<IWorldVoiceGrain>(partyId).GetLinesAsync(limit));

    [AllowAnonymous]

    [HttpGet("voice")]
    public async Task<ActionResult<VoiceWearReport>> GetVoice(Guid partyId)
        => Ok(await grains.GetGrain<IWorldVoiceGrain>(partyId).GetReportAsync());
}

public sealed record SelfSummary(
    Guid PersonaId,
    string Name,
    string? Mood,
    string Energy,
    string? Preoccupation,
    double Battery,
    double Availability,
    string? Doing,
    List<string> Day,
    List<string> Threads,
    double? Fed = null,
    double? Rested = null);
