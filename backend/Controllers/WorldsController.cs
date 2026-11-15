using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Analytics;
using PartyTown.Services.Auth;
using PartyTown.World;
using PostHog;

namespace PartyTown.Controllers;

[ApiController]
[Route("worlds")]
public sealed class WorldsController(IGrainFactory grains, WorldMove move, IPostHogClient posthog) : ControllerBase
{
    private IPartyRootGrain Root => grains.GetGrain<IPartyRootGrain>(Guid.Empty);

    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<WorldList>> GetWorlds()
    {
        if (!await Root.HasPartyId(Guid.Empty)) await Root.AddParty(new PartyInfo { Id = Guid.Empty, Name = "Default Party" });
        return Ok(new WorldList((await Root.GetWorldsAsync()).Select(Tile).ToList(), Actors.IsAdmin(User)));
    }

    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [AllowWhenStopped]
    [HttpPost("{partyId:guid}/move")]
    [ProducesResponseType<WorldTile>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorldTile>> Move(Guid partyId, [FromBody] MoveWorldRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxName) return BadRequest($"A name, up to {MaxName} characters.");
        var plan = new MovePlan(name, request.ParticipantIds, request.RelationIds ?? new List<Guid>(), request.PlaceIds);
        if (await move.MoveAsync(partyId, plan) is not { } toId) return NotFound();
        posthog.Track(HttpContext, "world_moved", new Dictionary<string, object> { ["party_id"] = toId });
        return Ok(await TileAsync(toId));
    }

    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpGet("{partyId:guid}/movable-places")]
    public async Task<ActionResult<List<MovablePlace>>> GetMovablePlaces(Guid partyId)
        => Ok(await move.MovablePlacesAsync(partyId));

    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [AllowWhenStopped]
    [HttpPut("{partyId:guid}/stopped")]
    [ProducesResponseType<WorldTile>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorldTile>> SetStopped(Guid partyId, [FromBody] SetWorldStoppedRequest request)
    {
        if (!await Root.SetStoppedAsync(partyId, request.Stopped)) return NotFound();
        if (request.Stopped) await grains.GetGrain<IWorldClockGrain>(partyId).UpdateAsync(false, null, null);
        return Ok(await TileAsync(partyId));
    }

    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpPut("current")]
    [ProducesResponseType<WorldTile>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorldTile>> SetCurrent([FromBody] SetCurrentWorldRequest request)
        => await Root.SetCurrentAsync(request.WorldId) ? Ok(await TileAsync(request.WorldId)) : NotFound();

    private const int MaxName = 60;

    private async Task<WorldTile> TileAsync(Guid id) => Tile((await Root.GetWorldsAsync()).Single(w => w.Id == id));

    private static WorldTile Tile(WorldEntry w)
    {
        var people = w.Participants.Where(p => p.Id != Narrator.PersonaId).ToList();
        return new WorldTile(w.Id, w.Name, w.Stopped, w.Current, people.Count, people.Count(p => p.Driver == DriverKind.User));
    }
}

public sealed record WorldTile(Guid Id, string Name, bool Stopped, bool Current, int People, int Humans);

public sealed record WorldList(List<WorldTile> Worlds, bool IsAdmin);

public sealed record MoveWorldRequest(string? Name, List<Guid>? ParticipantIds, List<Guid>? RelationIds, List<Guid>? PlaceIds);

public sealed record SetWorldStoppedRequest(bool Stopped);

public sealed record SetCurrentWorldRequest(Guid WorldId);
