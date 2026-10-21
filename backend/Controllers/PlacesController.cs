using Microsoft.AspNetCore.Mvc;
using PartyTown.World;
using PartyTown.World.Places;

namespace PartyTown.Controllers;

/// <summary>Places: where every Body is (spec .scratch/places). Place talk uses the Room message routes.</summary>
[ApiController]
[Route("worlds/{partyId:guid}/places")]
public sealed class PlacesController(IGrainFactory grains) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PlacesSnapshot>> GetPlaces(Guid partyId)
        => Ok(await Places(partyId).GetSnapshotAsync());

    /// <summary>The one-line Situation a Participant's prompts get.</summary>
    [HttpGet("situation/{participantId:guid}")]
    public async Task<ActionResult<SituationResponse>> GetSituation(Guid partyId, Guid participantId)
        => Ok(new SituationResponse(await Places(partyId).GetSituationAsync(participantId)));

    /// <summary>Walk a Body (default: the Player) to a Place and optionally a spot.</summary>
    [HttpPost("move")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Body>> Move(Guid partyId, [FromBody] MoveBodyRequest request)
    {
        var who = request.ParticipantId ?? (await grains.GetGrain<IWorldClockGrain>(partyId).GetStatusAsync()).PlayerId;
        if (who is null) return BadRequest("No participant and no Player yet.");
        var body = await Places(partyId).MoveAsync(who.Value, request.PlaceId, request.SpotId);
        return body is null ? Conflict("Can't go there (private, full, or unknown).") : Ok(body);
    }

    private IPlacesGrain Places(Guid partyId) => grains.GetGrain<IPlacesGrain>(partyId);
}

public sealed record MoveBodyRequest(Guid PlaceId, string? SpotId, Guid? ParticipantId);
public sealed record SituationResponse(string? Situation);
