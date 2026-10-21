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

    /// <summary>The Player's tab is idle (Body dozes) or back.</summary>
    [HttpPost("idle")]
    public async Task<ActionResult> SetIdle(Guid partyId, [FromBody] IdleRequest request)
    {
        var who = request.ParticipantId ?? (await grains.GetGrain<IWorldClockGrain>(partyId).GetStatusAsync()).PlayerId;
        if (who is null) return BadRequest("No participant and no Player yet.");
        await Places(partyId).SetIdleAsync(who.Value, request.Idle);
        return NoContent();
    }

    /// <summary>Set a look by hand (default: the Player's own). Unknown keys or values keep the current choice.</summary>
    [HttpPut("appearance")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Appearance>> SetAppearance(Guid partyId, [FromBody] AppearanceRequest request)
    {
        var who = await Who(partyId, request.ParticipantId);
        if (who is null) return BadRequest("No participant and no Player yet.");
        return await Places(partyId).SetAppearanceAsync(who.Value, request.Picked) is { } look ? Ok(look) : NotFound();
    }

    /// <summary>The palette a look is chosen from, field by field.</summary>
    [HttpGet("appearance/options")]
    public ActionResult<AppearanceField[]> GetAppearanceOptions() => Ok(AppearanceRules.Fields);

    /// <summary>Change a look from words ("dye my hair pink"); the LLM maps it onto the palette.</summary>
    [HttpPost("restyle")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RestyleResult>> Restyle(Guid partyId, [FromBody] TextRequest request)
    {
        var who = await Who(partyId, request.ParticipantId);
        if (who is null) return BadRequest("No participant and no Player yet.");
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 300) return BadRequest("Say what to change, under 300 characters.");
        return await Places(partyId).RestyleAsync(who.Value, request.Text) is { } r ? Ok(r) : Conflict("Couldn't restyle right now.");
    }

    /// <summary>Strike a pose at your spot (null pose ends it); pair poses take someone at the same spot.</summary>
    [HttpPost("pose")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FurniActionResponse>> Pose(Guid partyId, [FromBody] PoseRequest request)
    {
        var who = await Who(partyId, request.ParticipantId);
        if (who is null) return BadRequest("No participant and no Player yet.");
        var done = await Places(partyId).SetPoseAsync(who.Value, request.Pose, request.With);
        return done is null ? Conflict("That pose isn't possible here.") : Ok(new FurniActionResponse(done));
    }

    /// <summary>Add a thing to a Place; its 3D model is generated shortly after.</summary>
    [HttpPost("things")]
    public async Task<ActionResult<FurniView>> AddThing(Guid partyId, [FromBody] ThingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 200) return BadRequest("Describe it in under 200 characters.");
        return await Places(partyId).AddThingAsync(request.PlaceId, request.OwnerId, request.Text) is { } f ? Ok(f) : NotFound();
    }

    /// <summary>Move or turn a thing (owners; default: the Player). Null tile hands it back to the scene.</summary>
    [HttpPost("things/arrange")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Arrange(Guid partyId, [FromBody] ArrangeRequest request)
    {
        var who = await Who(partyId, request.ParticipantId);
        if (who is null) return BadRequest("No participant and no Player yet.");
        return await Places(partyId).ArrangeAsync(who.Value, request.PlaceId, request.FurniId, request.X, request.Z, request.Rot)
            ? NoContent() : Conflict("Not yours to move.");
    }

    [HttpPost("things/remove")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> RemoveThing(Guid partyId, [FromBody] ThingRefRequest request)
    {
        var who = await Who(partyId, request.ParticipantId);
        if (who is null) return BadRequest("No participant and no Player yet.");
        return await Places(partyId).RemoveThingAsync(who.Value, request.PlaceId, request.FurniId) ? NoContent() : Conflict("Not yours to take down.");
    }

    /// <summary>Author: generate a thing's model again.</summary>
    [HttpPost("things/remodel")]
    public async Task<ActionResult> Remodel(Guid partyId, [FromBody] ThingRefRequest request)
    {
        await Places(partyId).RemodelAsync(request.PlaceId, request.FurniId);
        return Accepted();
    }

    /// <summary>Wall and floor colours of a Place you own or made (hex, null = the scene's).</summary>
    [HttpPost("style")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> SetStyle(Guid partyId, [FromBody] PlaceStyleRequest request)
    {
        var who = await Who(partyId, request.ParticipantId);
        if (who is null) return BadRequest("No participant and no Player yet.");
        return await Places(partyId).SetPlaceStyleAsync(who.Value, request.PlaceId, request.Wall, request.Floor) ? NoContent() : Conflict("Not yours to restyle.");
    }

    /// <summary>Design a new public Place from a description (LLM, validated). 409 when it couldn't be made.</summary>
    [HttpPost("create")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Place>> CreatePlace(Guid partyId, [FromBody] CreatePlaceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 400) return BadRequest("Describe it in under 400 characters.");
        return await Places(partyId).CreatePlaceAsync(request.MadeBy, request.Text, request.Outdoor) is { } p
            ? Ok(p) : Conflict("Couldn't design that one (or there are too many made Places).");
    }

    /// <summary>Author: remove a generated Place; anyone there goes home.</summary>
    [HttpDelete("{placeId:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RemovePlace(Guid partyId, Guid placeId)
        => await Places(partyId).RemovePlaceAsync(placeId) ? NoContent() : NotFound();

    /// <summary>Author: let Personas pick their look again (all, or one), each seeing the others'.</summary>
    [HttpPost("redress")]
    public async Task<ActionResult> Redress(Guid partyId, [FromQuery] Guid? participantId)
    {
        await Places(partyId).RedressAsync(participantId);
        return Accepted();
    }

    private IPlacesGrain Places(Guid partyId) => grains.GetGrain<IPlacesGrain>(partyId);

    private async Task<Guid?> Who(Guid partyId, Guid? participantId)
        => participantId ?? (await grains.GetGrain<IWorldClockGrain>(partyId).GetStatusAsync()).PlayerId;
}

public sealed record MoveBodyRequest(Guid PlaceId, string? SpotId, Guid? ParticipantId);
public sealed record SituationResponse(string? Situation);
public sealed record FurniActionRequest(Guid PlaceId, string FurniId, bool Care, string? Text, Guid? ParticipantId);
public sealed record FurniActionResponse(string Done);
public sealed record AppearanceRequest(Dictionary<string, string?> Picked, Guid? ParticipantId);
public sealed record TextRequest(string Text, Guid? ParticipantId);
public sealed record PoseRequest(string? Pose, Guid? With, Guid? ParticipantId);
public sealed record ThingRequest(Guid PlaceId, string Text, Guid? OwnerId);
public sealed record ThingRefRequest(Guid PlaceId, string FurniId, Guid? ParticipantId);
public sealed record ArrangeRequest(Guid PlaceId, string FurniId, int? X, int? Z, int Rot, Guid? ParticipantId);
public sealed record PlaceStyleRequest(Guid PlaceId, string? Wall, string? Floor, Guid? ParticipantId);
public sealed record CreatePlaceRequest(string Text, bool Outdoor, Guid? MadeBy);
public sealed record IdleRequest(bool Idle, Guid? ParticipantId);
public sealed record DecorateRequest(string Text, Guid? ParticipantId);
public sealed record KnockRequest(Guid HomeId, Guid? ParticipantId);
public sealed record LetInRequest(Guid VisitorId, Guid? OwnerId);
public sealed record VoiceRequest(string Volume, Guid? To, Guid? ParticipantId);
