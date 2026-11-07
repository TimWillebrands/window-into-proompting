using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Analytics;
using PartyTown.Services.Auth;
using PartyTown.World;
using PartyTown.World.Desires;
using PartyTown.World.Places;
using PartyTown.World.Relations;
using PostHog;

namespace PartyTown.Controllers;

/// <summary>Relations (ADR 0035), author side: past ones stay between the pair, so reading is admin too.</summary>
[ApiController]
[Route("worlds/{partyId:guid}/relations")]
[Authorize(Policy = ClerkAuth.AdminPolicy)]
public sealed class RelationsController(IGrainFactory grains, IPostHogClient posthog) : ControllerBase
{
    private IRelationsGrain Relations(Guid partyId) => grains.GetGrain<IRelationsGrain>(partyId);

    [HttpGet]
    public async Task<ActionResult<List<Relation>>> GetRelations(Guid partyId)
        => Ok((await Relations(partyId).GetAsync()).Relations);

    /// <summary>Closed Places and who's been at their doors: moments this week, keys, and what each side wants.</summary>
    [HttpGet("doors")]
    public async Task<ActionResult<List<DoorRow>>> GetDoors(Guid partyId)
    {
        var snapshot = await grains.GetGrain<IPlacesGrain>(partyId).GetSnapshotAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string Name(Guid id) => snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == id)?.Name ?? "Someone";
        async Task<string?> WantAsync(Guid who, Guid placeId)
            => (await grains.GetGrain<IDesiresGrain>(WorldKeys.Life(partyId, who)).GetAsync()).Acquired
                .FirstOrDefault(d => d.PlaceId == placeId && d.Status == DesireStatus.Active)?.Text;
        var rows = new List<DoorRow>();
        foreach (var place in snapshot.Places.Where(p => p.Keyed()))
        {
            var host = place.Hosts().FirstOrDefault();
            foreach (var visitor in place.Doorsteps.Keys.Union(place.Keyholders).Where(v => !place.Hosts().Contains(v)))
                rows.Add(new DoorRow(place.Name, Name(host), Name(visitor), place.DoorstepsLately(visitor, now), place.Keyholders.Contains(visitor),
                    await WantAsync(visitor, place.Id), await WantAsync(host, place.Id)));
        }
        return Ok(rows.OrderByDescending(r => r.HasKey).ThenByDescending(r => r.Doorsteps).ToList());
    }

    /// <summary>State what two people are, or with <c>Past</c> what they were.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Relation>> Declare(Guid partyId, [FromBody] DeclareRelationRequest request)
    {
        var cast = await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync();
        if (new[] { request.AId, request.BId }.Any(id => id == Narrator.PersonaId || cast.All(c => c.Id != id))) return NotFound();
        if (await Relations(partyId).DeclareAsync(request.AId, request.BId, request.Kind, request.Story, request.Past) is not { } relation) return BadRequest();
        posthog.Track(HttpContext, "relation_declared", new Dictionary<string, object> { ["party_id"] = partyId, ["kind"] = request.Kind.ToString(), ["past"] = request.Past });
        return Ok(relation);
    }

    /// <summary>It's over: stays on record as past, and a shared Home splits.</summary>
    [HttpPost("{relationId:guid}/end")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> End(Guid partyId, Guid relationId)
    {
        var relation = (await Relations(partyId).GetAsync()).Relations.FirstOrDefault(r => r.Id == relationId);
        return relation is not null && await Relations(partyId).EndAsync(relation.AId, relation.BId) ? NoContent() : NotFound();
    }

    /// <summary>Move in with <c>HostId</c>; null splits a shared Home. 409 when one of them already shares another.</summary>
    [HttpPut("{relationId:guid}/home")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> SetHome(Guid partyId, Guid relationId, [FromBody] RelationHomeRequest request)
        => await Relations(partyId).SetHomeAsync(relationId, request.HostId) ? NoContent() : Conflict();

    /// <summary>A Home designed from a description, owned by <c>HostId</c>. The Homes they had are removed. 409 when the design fails or someone else lives there.</summary>
    [HttpPost("{relationId:guid}/home/new")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> NewHome(Guid partyId, Guid relationId, [FromBody] NewHomeRequest request)
        => await Relations(partyId).NewHomeAsync(relationId, request.HostId, request.Description) ? NoContent() : Conflict();

    /// <summary>Strike it, as if it never was.</summary>
    [HttpDelete("{relationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Forget(Guid partyId, Guid relationId)
        => await Relations(partyId).ForgetAsync(relationId) ? NoContent() : NotFound();
}

public sealed record DeclareRelationRequest(Guid AId, Guid BId, RelationKind Kind, string? Story, bool Past);

public sealed record RelationHomeRequest(Guid? HostId);

public sealed record NewHomeRequest(Guid HostId, string Description);

public sealed record DoorRow(string Place, string Host, string Visitor, int Doorsteps, bool HasKey, string? VisitorWant, string? HostWant);
