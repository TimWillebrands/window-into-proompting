using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PartyTown.Services.Auth;
using PartyTown.World;
using PartyTown.World.Places;

namespace PartyTown.Controllers;

[ApiController]
[Route("worlds/{partyId:guid}/places")]
public sealed class PlacesController(IGrainFactory grains, Actors actors) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<PlacesSnapshot>> GetPlaces(Guid partyId)
        => Ok(await Places(partyId).GetSnapshotAsync());

    [AllowAnonymous]
    [HttpGet("situation/{participantId:guid}")]
    public async Task<ActionResult<SituationResponse>> GetSituation(Guid partyId, Guid participantId)
        => Ok(new SituationResponse(await Places(partyId).GetSituationAsync(participantId)));

    [HttpPost("move")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Body>> Move(Guid partyId, [FromBody] MoveBodyRequest request)
    {
        var who = await actors.ActAsync(User, partyId, request.ParticipantId);
        var body = await Places(partyId).MoveAsync(who, request.PlaceId, request.SpotId);
        return body is null ? Conflict("Can't go there (private, full, or unknown).") : Ok(body);
    }

    [HttpPost("join")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Body>> Join(Guid partyId, [FromBody] JoinBodyRequest request)
    {
        var who = await actors.ActAsync(User, partyId, request.ParticipantId);
        var body = await Places(partyId).JoinAsync(who, request.OtherId);
        return body is null ? Conflict("Can't get to them right now.") : Ok(body);
    }

    [HttpPost("furni")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FurniActionResponse>> ActOnFurni(Guid partyId, [FromBody] FurniActionRequest request)
    {
        var who = await actors.ActAsync(User, partyId, request.ParticipantId);
        if (!request.Care && await Doing.DoAsync(grains, partyId, who, null, request.PlaceId, request.FurniId, null, request.Text, DateTimeOffset.UtcNow) is { } did)
            return Ok(new FurniActionResponse(did.Summary));
        var done = await Places(partyId).ActOnFurniAsync(who, request.PlaceId, request.FurniId, request.Care, request.Text);
        return done is null ? Conflict("Not here, or nothing to do with it right now.") : Ok(new FurniActionResponse(done));
    }

    [HttpPost("voice")]
    public async Task<ActionResult> SetVoice(Guid partyId, [FromBody] VoiceRequest request)
    {
        if (request.Volume is not (Volumes.Whisper or Volumes.Talk or Volumes.Shout)) return BadRequest("whisper, talk or shout");
        var who = await actors.ActAsync(User, partyId, request.ParticipantId);
        await Places(partyId).SetVoiceAsync(who, request.Volume, request.To);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpGet("overheard/{participantId:guid}")]
    public async Task<ActionResult<List<Overheard>>> GetOverheard(Guid partyId, Guid participantId)
        => Ok(await Places(partyId).GetOverheardAsync(participantId));

    [HttpPost("knock")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Knock(Guid partyId, [FromBody] KnockRequest request)
    {
        var who = await actors.ActAsync(User, partyId, request.ParticipantId);
        return await Places(partyId).KnockAsync(who, request.HomeId) ? NoContent() : Conflict("Nobody's home (or they're asleep).");
    }

    [HttpPost("let-in")]
    public async Task<ActionResult> LetIn(Guid partyId, [FromBody] LetInRequest request)
    {
        var who = await actors.ActAsync(User, partyId, request.OwnerId);
        return await Places(partyId).LetInAsync(who, request.VisitorId) ? NoContent() : Conflict("No home to let them into.");
    }

    [HttpPost("decorate")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FurniActionResponse>> Decorate(Guid partyId, [FromBody] DecorateRequest request)
    {
        var who = await actors.ActAsync(User, partyId, request.ParticipantId);
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 200) return BadRequest("Describe it in under 200 characters.");
        var done = await Places(partyId).DecorateAsync(who, request.Text);
        return done is null ? Conflict("Only at home, and at most two new things a day.") : Ok(new FurniActionResponse(done));
    }

    [HttpPost("idle")]
    public async Task<ActionResult> SetIdle(Guid partyId, [FromBody] IdleRequest request)
    {
        var who = await actors.ActAsync(User, partyId, request.ParticipantId);
        await Places(partyId).SetIdleAsync(who, request.Idle);
        return NoContent();
    }

    [HttpPut("appearance")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Appearance>> SetAppearance(Guid partyId, [FromBody] AppearanceRequest request)
    {
        var who = await actors.ActAsync(User, partyId, request.ParticipantId);
        return await Places(partyId).SetAppearanceAsync(who, request.Picked) is { } look ? Ok(look) : NotFound();
    }

    [AllowAnonymous]
    [HttpGet("appearance/options")]
    public ActionResult<AppearanceField[]> GetAppearanceOptions() => Ok(AppearanceRules.Fields);

    [HttpPost("restyle")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RestyleResult>> Restyle(Guid partyId, [FromBody] TextRequest request)
    {
        var who = await actors.ActAsync(User, partyId, request.ParticipantId);
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 300) return BadRequest("Say what to change, under 300 characters.");
        return await Places(partyId).RestyleAsync(who, request.Text) is { } r ? Ok(r) : Conflict("Couldn't restyle right now.");
    }

    [HttpPost("pose")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FurniActionResponse>> Pose(Guid partyId, [FromBody] PoseRequest request)
    {
        var who = await actors.ActAsync(User, partyId, request.ParticipantId);
        var done = await Places(partyId).SetPoseAsync(who, request.Pose, request.With);
        return done is null ? Conflict("That pose isn't possible here.") : Ok(new FurniActionResponse(done));
    }

    [HttpPost("things")]
    public async Task<ActionResult<FurniView>> AddThing(Guid partyId, [FromBody] ThingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 200) return BadRequest("Describe it in under 200 characters.");
        return await Places(partyId).AddThingAsync(request.PlaceId, await actors.ActAsync(User, partyId, request.OwnerId), request.Text) is { } f ? Ok(f) : NotFound();
    }

    [HttpPost("things/arrange")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Arrange(Guid partyId, [FromBody] ArrangeRequest request)
    {
        var who = await actors.ActAsync(User, partyId, request.ParticipantId);
        return await Places(partyId).ArrangeAsync(who, request.PlaceId, request.FurniId, request.X, request.Z, request.Rot)
            ? NoContent() : Conflict("Not yours to move.");
    }

    [HttpPost("things/remove")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> RemoveThing(Guid partyId, [FromBody] ThingRefRequest request)
    {
        var who = await actors.ActAsync(User, partyId, request.ParticipantId);
        return await Places(partyId).RemoveThingAsync(who, request.PlaceId, request.FurniId) ? NoContent() : Conflict("Not yours to take down.");
    }

    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpPost("things/remodel")]
    public async Task<ActionResult> Remodel(Guid partyId, [FromBody] ThingRefRequest request)
    {
        await Places(partyId).RemodelAsync(request.PlaceId, request.FurniId);
        return Accepted();
    }

    [HttpPost("style")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> SetStyle(Guid partyId, [FromBody] PlaceStyleRequest request)
    {
        var who = await actors.ActAsync(User, partyId, request.ParticipantId);
        return await Places(partyId).SetPlaceStyleAsync(who, request.PlaceId, request.Wall, request.Floor) ? NoContent() : Conflict("Not yours to restyle.");
    }

    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpPost("create")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Place>> CreatePlace(Guid partyId, [FromBody] CreatePlaceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 400) return BadRequest("Describe it in under 400 characters.");
        return await PlaceMaking.CreatePlaceAsync(grains, partyId, request.MadeBy, request.Text, request.Outdoor) is { } p
            ? Ok(p) : Conflict("Couldn't design that one (or there are too many made Places).");
    }

    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpPost("{placeId:guid}/redesign")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Place>> RedesignHome(Guid partyId, Guid placeId, [FromBody] RedesignHomeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 1500) return BadRequest("Describe it in under 1500 characters.");
        return await PlaceMaking.RedesignAsync(grains, partyId, placeId, request.Text, request.Name, request.Outdoor) is { } p
            ? Ok(p) : Conflict("A Locale, or couldn't design that one.");
    }

    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpPut("{placeId:guid}/access")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Place>> SetAccess(Guid partyId, Guid placeId, [FromBody] PlaceAccessRequest request)
    {
        if (!PlaceAccess.IsKnown(request.Access)) return BadRequest($"Access is one of: {string.Join(", ", PlaceAccess.All)}.");
        return await Places(partyId).SetAccessAsync(placeId, request.Access, request.Allowed ?? new List<Guid>()) is { } place ? Ok(place) : NotFound();
    }

    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpDelete("{placeId:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RemovePlace(Guid partyId, Guid placeId)
        => await Places(partyId).RemovePlaceAsync(placeId) ? NoContent() : NotFound();

    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpPost("redress")]
    public async Task<ActionResult> Redress(Guid partyId, [FromQuery] Guid? participantId)
    {
        await Places(partyId).RedressAsync(participantId);
        return Accepted();
    }

    private IPlacesGrain Places(Guid partyId) => grains.GetGrain<IPlacesGrain>(partyId);
}

public sealed record MoveBodyRequest(Guid PlaceId, string? SpotId, Guid? ParticipantId);

public sealed record JoinBodyRequest(Guid OtherId, Guid? ParticipantId);
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
public sealed record RedesignHomeRequest(string Text, string? Name, bool Outdoor);
public sealed record IdleRequest(bool Idle, Guid? ParticipantId);
public sealed record DecorateRequest(string Text, Guid? ParticipantId);
public sealed record KnockRequest(Guid HomeId, Guid? ParticipantId);
public sealed record PlaceAccessRequest(string Access, List<Guid>? Allowed);
public sealed record LetInRequest(Guid VisitorId, Guid? OwnerId);
public sealed record VoiceRequest(string Volume, Guid? To, Guid? ParticipantId);
