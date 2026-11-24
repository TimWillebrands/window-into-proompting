using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PartyTown.Services.Render;
using PartyTown.World.Places;

namespace PartyTown.Controllers;

[ApiController]
[Route("render")]
public sealed class RenderController(PlaceRenderer renderer) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("drafts/{key}")]
    [ProducesResponseType<Place>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<Place> GetDraft(string key)
        => renderer.Draft(key) is { } draft ? draft : NotFound();
}
