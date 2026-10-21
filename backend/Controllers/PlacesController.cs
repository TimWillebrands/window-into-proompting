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

    /// <summary>Use or care for a furni where the Body (default: the Player) is.</summary>
    [HttpPost("furni")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FurniActionResponse>> ActOnFurni(Guid partyId, [FromBody] FurniActionRequest request)
    {
        var who = request.ParticipantId ?? (await grains.GetGrain<IWorldClockGrain>(partyId).GetStatusAsync()).PlayerId;
        if (who is null) return BadRequest("No participant and no Player yet.");
        var done = await Places(partyId).ActOnFurniAsync(who.Value, request.PlaceId, request.FurniId, request.Care, request.Text);
        return done is null ? Conflict("Not here, or nothing to do with it right now.") : Ok(new FurniActionResponse(done));
    }

    /// <summary>Set the volume of the Player's next line in Place talk (whisper needs an addressee at their spot).</summary>
    [HttpPost("voice")]
    public async Task<ActionResult> SetVoice(Guid partyId, [FromBody] VoiceRequest request)
    {
        if (request.Volume is not (Volumes.Whisper or Volumes.Talk or Volumes.Shout)) return BadRequest("whisper, talk or shout");
        var who = request.ParticipantId ?? (await grains.GetGrain<IWorldClockGrain>(partyId).GetStatusAsync()).PlayerId;
        if (who is null) return BadRequest("No participant and no Player yet.");
        await Places(partyId).SetVoiceAsync(who.Value, request.Volume, request.To);
        return NoContent();
    }

    /// <summary>Talk the Participant noticed but couldn't make out (tone, maybe their name).</summary>
    [HttpGet("overheard/{participantId:guid}")]
    public async Task<ActionResult<List<Overheard>>> GetOverheard(Guid partyId, Guid participantId)
        => Ok(await Places(partyId).GetOverheardAsync(participantId));

    /// <summary>Knock on someone's Home (default: as the Player). 409 when they're out or asleep.</summary>
    [HttpPost("knock")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Knock(Guid partyId, [FromBody] KnockRequest request)
    {
        var who = request.ParticipantId ?? (await grains.GetGrain<IWorldClockGrain>(partyId).GetStatusAsync()).PlayerId;
        if (who is null) return BadRequest("No participant and no Player yet.");
        return await Places(partyId).KnockAsync(who.Value, request.HomeId) ? NoContent() : Conflict("Nobody's home (or they're asleep).");
    }

    /// <summary>Let a knocker in, or invite someone over (default host: the Player).</summary>
    [HttpPost("let-in")]
    public async Task<ActionResult> LetIn(Guid partyId, [FromBody] LetInRequest request)
    {
        var who = request.OwnerId ?? (await grains.GetGrain<IWorldClockGrain>(partyId).GetStatusAsync()).PlayerId;
        if (who is null) return BadRequest("No participant and no Player yet.");
        return await Places(partyId).LetInAsync(who.Value, request.VisitorId) ? NoContent() : Conflict("No home to let them into.");
    }

    /// <summary>Put something up in your own Home (default: the Player). 409 when not home or over today's allowance.</summary>
    [HttpPost("decorate")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FurniActionResponse>> Decorate(Guid partyId, [FromBody] DecorateRequest request)
    {
        var who = request.ParticipantId ?? (await grains.GetGrain<IWorldClockGrain>(partyId).GetStatusAsync()).PlayerId;
        if (who is null) return BadRequest("No participant and no Player yet.");
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 200) return BadRequest("Describe it in under 200 characters.");
        var done = await Places(partyId).DecorateAsync(who.Value, request.Text);
        return done is null ? Conflict("Only at home, and at most two new things a day.") : Ok(new FurniActionResponse(done));
    }

    private IPlacesGrain Places(Guid partyId) => grains.GetGrain<IPlacesGrain>(partyId);
}

public sealed record MoveBodyRequest(Guid PlaceId, string? SpotId, Guid? ParticipantId);
public sealed record SituationResponse(string? Situation);
public sealed record FurniActionRequest(Guid PlaceId, string FurniId, bool Care, string? Text, Guid? ParticipantId);
public sealed record FurniActionResponse(string Done);
public sealed record DecorateRequest(string Text, Guid? ParticipantId);
public sealed record KnockRequest(Guid HomeId, Guid? ParticipantId);
public sealed record LetInRequest(Guid VisitorId, Guid? OwnerId);
public sealed record VoiceRequest(string Volume, Guid? To, Guid? ParticipantId);
