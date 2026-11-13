using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Analytics;
using PartyTown.Services.Auth;
using PartyTown.World;
using PostHog;

namespace PartyTown.Controllers;

/// <summary>The Worlds there are, as the login screen offers them: which is current, which are stopped, moving to a new one.</summary>
[ApiController]
[Route("worlds")]
public sealed class WorldsController(IGrainFactory grains, WorldMove move, IPostHogClient posthog) : ControllerBase
{
    private IPartyRootGrain Root => grains.GetGrain<IPartyRootGrain>(Guid.Empty);

    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<WorldList>> GetWorlds()
    {
        // A fresh database has no Party until something asks for the root; the login screen is that something.
        if (!await Root.HasPartyId(Guid.Empty)) await Root.AddParty(new PartyInfo { Id = Guid.Empty, Name = "Default Party" });
        return Ok(new WorldList((await Root.GetWorldsAsync()).Select(Tile).ToList(), Actors.IsAdmin(User)));
    }

    /// <summary>Everyone picked moves to a new World with the Relations picked; this one stops and the new one is current.</summary>
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

    /// <summary>The Places a Move out of this World can bring: made ones and humans' Homes, the authored ones suggested.</summary>
    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpGet("{partyId:guid}/movable-places")]
    public async Task<ActionResult<List<MovablePlace>>> GetMovablePlaces(Guid partyId)
        => Ok(await move.MovablePlacesAsync(partyId));

    /// <summary>Stop a World or start it again; starting again leaves its clock paused.</summary>
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

    /// <summary>The World new visitors and MCP agents land in.</summary>
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

/// <param name="Humans">Players at the keyboard; Players on auto count as people only.</param>
public sealed record WorldTile(Guid Id, string Name, bool Stopped, bool Current, int People, int Humans);

public sealed record WorldList(List<WorldTile> Worlds, bool IsAdmin);

/// <param name="ParticipantIds">Who comes along; null is everyone.</param>
/// <param name="RelationIds">Relations, from this World's, that come along with their story.</param>
/// <param name="PlaceIds">Places from movable-places that come along; null is the suggested ones.</param>
public sealed record MoveWorldRequest(string? Name, List<Guid>? ParticipantIds, List<Guid>? RelationIds, List<Guid>? PlaceIds);

public sealed record SetWorldStoppedRequest(bool Stopped);

public sealed record SetCurrentWorldRequest(Guid WorldId);
