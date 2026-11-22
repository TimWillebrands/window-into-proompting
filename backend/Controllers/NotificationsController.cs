using Microsoft.AspNetCore.Mvc;
using PartyTown.Services.Auth;
using PartyTown.World.Notifications;

namespace PartyTown.Controllers;

[ApiController]
[Route("worlds/{partyId:guid}/notifications")]
public sealed class NotificationsController(PlayerInbox inbox, Actors actors) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<NotificationFeed>> Get(Guid partyId)
        => Ok(await inbox.ForAsync(partyId, await actors.PlayerAsync(User, partyId), DateTimeOffset.UtcNow));

    [HttpPost("read")]
    [AllowWhenStopped]
    public async Task<ActionResult<NotificationFeed>> MarkRead(Guid partyId, [FromBody] MarkNotificationsReadRequest request)
        => Ok(await inbox.MarkReadAsync(partyId, await actors.PlayerAsync(User, partyId), request.Ids, request.All, DateTimeOffset.UtcNow));

    [HttpPost("dismiss")]
    [AllowWhenStopped]
    public async Task<ActionResult<NotificationFeed>> Dismiss(Guid partyId, [FromBody] DismissNotificationsRequest request)
        => Ok(await inbox.DismissAsync(partyId, await actors.PlayerAsync(User, partyId), request.Ids, request.All, DateTimeOffset.UtcNow));
}

public sealed record DismissNotificationsRequest(List<string>? Ids, bool All = false);

public sealed record MarkNotificationsReadRequest(List<string>? Ids, bool All = false);
