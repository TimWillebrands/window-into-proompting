using Microsoft.AspNetCore.Authorization;
using PartyTown.Services.Memory;
using PostHog;
using Microsoft.AspNetCore.Mvc;
using PartyTown.Services.Analytics;
using PartyTown.Services.Auth;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.World;
using PartyTown.World.Apps;

namespace PartyTown.Controllers;

/// <summary>
/// The World surface of a Party (ADR 0018): clock + activity log, the Player, and the
/// non-Room Apps (Board, Messenger, Diary). Rooms keep their routes on <see cref="PartyController"/>.
/// </summary>
[ApiController]
[Route("worlds/{partyId:guid}")]
public sealed class WorldController(IGrainFactory grains, IEnumerable<IWorldApp> apps, Actors actors, PlayerRemoval removal, PlayerAuto auto, IPostHogClient posthog) : ControllerBase
{
    private BoardApp BoardApp => apps.OfType<BoardApp>().Single();
    private CalendarApp CalendarApp => apps.OfType<CalendarApp>().Single();

    [AllowAnonymous]

    [HttpGet]
    public async Task<ActionResult<WorldStatus>> GetStatus(Guid partyId)
        => Ok(await Clock(partyId).GetStatusAsync());

    [Authorize(Policy = ClerkAuth.AdminPolicy)]

    [HttpPut]
    public async Task<ActionResult<WorldStatus>> Update(Guid partyId, [FromBody] UpdateWorldRequest request)
        => Ok(await Clock(partyId).UpdateAsync(request.Running, request.TickSeconds, request.MaxActionsPerHour, request.AmbientMinutes));

    /// <summary>Give one Persona (or a weighted pick) a Life turn right now.</summary>
    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpPost("tick")]
    public async Task<ActionResult<TickResponse>> Tick(Guid partyId, [FromBody] TickRequest request)
        => Ok(new TickResponse(await Clock(partyId).TickNowAsync(request.PersonaId, request.Nudge)));

    [AllowAnonymous]

    [HttpGet("activities")]
    public async Task<ActionResult<List<WorldActivity>>> GetActivities(Guid partyId, [FromQuery] int limit = 100, [FromQuery] Guid? personaId = null)
        => Ok(await VisibleAsync(partyId, await Clock(partyId).GetActivitiesAsync(limit, personaId)));

    /// <summary>Who lives here and what they're up to.</summary>
    [AllowAnonymous]
    [HttpGet("lives")]
    public async Task<ActionResult<List<LifeSummary>>> GetLives(Guid partyId)
    {
        var cast = await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync();
        var status = await Clock(partyId).GetStatusAsync();
        var activities = await VisibleAsync(partyId, await Clock(partyId).GetActivitiesAsync(300, null));
        return Ok(cast
            .Where(c => c.Id != Narrator.PersonaId)
            .Select(c =>
            {
                var last = activities.FirstOrDefault(a => a.PersonaId == c.Id);
                return new LifeSummary(c.Id, c.Name, c.DefaultDriver, c.Chattiness,
                    status.BusyPersonaIds.Contains(c.Id), c.Id == status.PlayerId, last);
            })
            .ToList());
    }

    /// <summary>Announce a Happening every Persona will perceive and remember.</summary>
    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpPost("happenings")]
    public async Task<ActionResult<WorldActivity>> Announce(Guid partyId, [FromBody] AnnounceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 600)
            return BadRequest("Text is required (≤ 600 chars).");
        return Ok(await Clock(partyId).AnnounceAsync(request.Text.Trim()));
    }

    /// <summary>Invite a library Persona to live in this World (LLM-driven Participant).</summary>
    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpPost("residents")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LifeSummary>> Invite(Guid partyId, [FromBody] InviteResidentRequest request)
    {
        var persona = await grains.GetGrain<IPersonaGrain>(request.PersonaId).GetPersona();
        if (string.IsNullOrEmpty(persona.Name) || request.PersonaId == Narrator.PersonaId) return NotFound();
        var added = await grains.GetGrain<IPartyGrain>(partyId).AddParticipantsAsync(new List<PartyParticipant>
        {
            new() { Id = request.PersonaId, Name = persona.Name, Driver = DriverKind.LLM },
        });
        if (added.Count > 0)
        {
            await Clock(partyId).RecordAsync(new WorldActivity
            {
                PersonaId = request.PersonaId, PersonaName = persona.Name, App = "world", Action = "arrive",
                Summary = "moved into the world",
            });
        }
        return Ok(new LifeSummary(persona.Id, persona.Name, DriverKind.LLM, persona.Chattiness, false, false, null));
    }

    /// <summary>A Persona moves out: their Participant (and Party-scoped memories' owner) leaves the cast.</summary>
    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpDelete("residents/{personaId:guid}")]
    public async Task<ActionResult> Evict(Guid partyId, Guid personaId)
    {
        // A Player on auto looks like a Persona here, but leaving takes what's theirs and frees their Clerk id.
        if (await Clock(partyId).IsClaimedAsync(personaId))
            return await removal.RemoveAsync(partyId, personaId) ? NoContent() : NotFound();
        var party = grains.GetGrain<IPartyGrain>(partyId);
        var info = await party.GetParty();
        if (info.Participants.All(p => p.Id != personaId || p.Driver != DriverKind.LLM)) return NotFound();
        if (await party.RemoveParticipantAsync(personaId) is not { } leaving) return NotFound();
        await grains.GetGrain<PartyTown.World.Relations.IRelationsGrain>(partyId).LeaveAsync(personaId);
        await Clock(partyId).RecordAsync(new WorldActivity
        {
            PersonaId = personaId, PersonaName = leaving.Name, App = "world", Action = "leave", Summary = "moved out of the world",
        });
        return NoContent();
    }

    /// <summary>The caller's character here; PlayerId is null until they've made one.</summary>
    [HttpGet("player")]
    public async Task<ActionResult<PlayerResponse>> GetPlayer(Guid partyId)
    {
        var id = await actors.FindPlayerAsync(User, partyId);
        var info = await grains.GetGrain<IPartyGrain>(partyId).GetParty();
        var link = info.Participants.FirstOrDefault(p => p.Id == id);
        var aura = id is { } self ? (await grains.GetGrain<IPersonaGrain>(self).GetPersona()).Aura : null;
        return Ok(new PlayerResponse(id, link?.Name, Actors.IsAdmin(User), link?.Driver == DriverKind.LLM, id is { } own && link?.Driver == DriverKind.LLM ? await auto.MissionAsync(partyId, own) : null, aura));
    }

    /// <summary>How the caller's character comes across to whoever stands right next to them; empty clears it.</summary>
    [HttpPut("player/aura")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PlayerResponse>> SetAura(Guid partyId, [FromBody] SetAuraRequest request)
    {
        if (request.Aura?.Trim().Length > Persona.MaxAura) return BadRequest($"An aura is up to {Persona.MaxAura} characters.");
        var id = await actors.PlayerAsync(User, partyId);
        await grains.GetGrain<IPersonaGrain>(id).SetAura(request.Aura);
        return await GetPlayer(partyId);
    }

    /// <summary>Hand the caller's character to the agent, or take it back, optionally with a mission for them to go after.</summary>
    [HttpPut("player/auto")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PlayerResponse>> SetAuto(Guid partyId, [FromBody] SetAutoRequest request)
    {
        var id = await actors.PlayerAsync(User, partyId);
        await auto.SetAsync(partyId, id, request.On, request.Mission);
        posthog.Track(HttpContext, "player_auto_set", new Dictionary<string, object> { ["party_id"] = partyId, ["player_id"] = id, ["on"] = request.On });
        return await GetPlayer(partyId);
    }

    /// <summary>Make the caller's character. 409 when the name is taken.</summary>
    [HttpPost("player")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PlayerResponse>> CreatePlayer(Guid partyId, [FromBody] CreatePlayerRequest request)
    {
        if (AccessRules.CleanPlayerName(request.Name) is not { } name) return BadRequest($"A name, up to {AccessRules.MaxPlayerName} characters.");
        if (await Clock(partyId).ClaimPlayerAsync(User.ClerkUserId()!, name) is not { } id)
            return Conflict("Someone here already goes by that name.");
        posthog.Track(HttpContext, "player_created", new Dictionary<string, object> { ["party_id"] = partyId, ["player_id"] = id });
        return Ok(new PlayerResponse(id, name, Actors.IsAdmin(User)));
    }

    /// <summary>Remove a human Player and what's theirs. Personas keep their memories.</summary>
    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpDelete("players/{playerId:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RemovePlayer(Guid partyId, Guid playerId)
        => await removal.RemoveAsync(partyId, playerId) ? NoContent() : NotFound();

    // ─── Board ────────────────────────────────────────────────────────────────

    [AllowAnonymous]

    [HttpGet("board")]
    public async Task<ActionResult<List<BoardThread>>> GetBoard(Guid partyId)
        => Ok(await Board(partyId).GetThreadsAsync());

    [AllowAnonymous]

    [HttpGet("board/{threadId:guid}")]
    public async Task<ActionResult<BoardThread>> GetThread(Guid partyId, Guid threadId)
        => await Board(partyId).GetThreadAsync(threadId) is { } t ? Ok(t) : NotFound();

    [HttpPost("board")]
    public async Task<ActionResult<BoardThread>> CreatePost(Guid partyId, [FromBody] CreateBoardPostRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length > BoardApp.MaxLength)
            return BadRequest($"Body is required (≤ {BoardApp.MaxLength} chars).");
        var (authorId, authorName) = await AuthorAsync(partyId, request.AuthorId);
        var (thread, outcome) = await BoardApp.PostAsync(partyId, authorId, authorName, request.Body.Trim(), await ViewsOfAsync(partyId));
        await RecordAsync(partyId, authorId, authorName, BoardApp.Id, outcome);
        posthog.Track(HttpContext, "board_post_created", new Dictionary<string, object>
        {
            ["party_id"] = partyId, ["thread_id"] = thread.Id, ["length"] = request.Body.Trim().Length,
        });
        return Ok(thread);
    }

    [HttpPost("board/{threadId:guid}/replies")]
    public async Task<ActionResult<BoardThread>> Reply(Guid partyId, Guid threadId, [FromBody] CreateBoardReplyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length > BoardApp.MaxLength)
            return BadRequest($"Body is required (≤ {BoardApp.MaxLength} chars).");
        var (authorId, authorName) = await AuthorAsync(partyId, request.AuthorId);
        if (await BoardApp.ReplyAsync(partyId, threadId, authorId, authorName, request.Body.Trim(), await ViewsOfAsync(partyId)) is not { } done)
            return NotFound();
        await RecordAsync(partyId, authorId, authorName, BoardApp.Id, done.Outcome);
        posthog.Track(HttpContext, "board_reply_created", new Dictionary<string, object>
        {
            ["party_id"] = partyId, ["thread_id"] = threadId, ["length"] = request.Body.Trim().Length,
        });
        return Ok(done.Thread);
    }

    /// <summary>Give (or take back) Respect! on a post, or on one of its replies when <c>ReplyId</c> is set.</summary>
    [HttpPost("board/{threadId:guid}/respect")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BoardThread>> Respect(Guid partyId, Guid threadId, [FromBody] BoardRespectRequest request)
    {
        var (giverId, giverName) = await AuthorAsync(partyId, request.AuthorId);
        if (await BoardApp.RespectAsync(partyId, threadId, request.ReplyId, giverId, giverName, request.Given) is not { } t) return NotFound();
        posthog.Track(HttpContext, "board_respect_changed", new Dictionary<string, object>
        {
            ["party_id"] = partyId, ["thread_id"] = threadId, ["on_reply"] = request.ReplyId is not null, ["given"] = request.Given,
        });
        return Ok(t);
    }

    // ─── Messenger ────────────────────────────────────────────────────────────

    /// <summary>DM threads of one Participant (defaults to the Player), newest first.</summary>
    [HttpGet("messenger")]
    public async Task<ActionResult<List<DirectChatSummary>>> GetDirectChats(Guid partyId, [FromQuery] Guid? personaId = null)
    {
        var viewer = await actors.ViewerAsync(User, partyId);
        if ((personaId ?? viewer.PlayerId) is not { } who) return Ok(new List<DirectChatSummary>());
        var humans = await actors.HumansAsync(partyId);
        var party = grains.GetGrain<IPartyGrain>(partyId);
        var info = await party.GetParty();
        var names = info.Participants.ToDictionary(p => p.Id, p => p.Name);

        var result = new List<DirectChatSummary>();
        foreach (var dm in (await party.GetChatGroups()).Where(g => g.Kind == ChatGroupKinds.Direct && g.MemberIds?.Contains(who) == true))
        {
            if (!AccessRules.CanSeeRoom(viewer, AccessRules.PrivateMembers(dm, humans))) continue;
            var other = dm.MemberIds!.First(id => id != who);
            var last = (await grains.GetGrain<IChatGroupGrain>(dm.Id).GetRecentMessagesAsync(1)).LastOrDefault();
            result.Add(new DirectChatSummary(dm.Id, other, names.GetValueOrDefault(other, "someone"),
                last?.Content, last?.SenderId, last?.SendAt ?? dm.CreatedAt));
        }
        return Ok(result.OrderByDescending(r => r.LastAt).ToList());
    }

    [HttpPost("messenger")]
    public async Task<ActionResult<ChatGroupInfo>> OpenDirectChat(Guid partyId, [FromBody] OpenDirectChatRequest request)
    {
        var from = await actors.ActAsync(User, partyId, request.FromPersonaId);
        if (from == request.WithPersonaId) return BadRequest("Cannot DM yourself.");
        var info = await grains.GetGrain<IPartyGrain>(partyId).GetParty();
        if (info.Participants.All(p => p.Id != request.WithPersonaId)) return NotFound();
        var chat = await grains.GetGrain<IPartyGrain>(partyId).GetOrCreateDirectChatAsync(from, request.WithPersonaId);
        posthog.Track(HttpContext, "direct_chat_opened", new Dictionary<string, object> { ["party_id"] = partyId, ["room_id"] = chat.Id });
        return Ok(chat);
    }

    // ─── Calendar ─────────────────────────────────────────────────────────────

    [AllowAnonymous]

    [HttpGet("gatherings")]
    public async Task<ActionResult<List<Gathering>>> GetGatherings(Guid partyId)
        => Ok(await grains.GetGrain<ICalendarGrain>(partyId).GetGatheringsAsync());

    /// <summary>A human plans a Gathering in a Room; personas see it in their Calendar perception.</summary>
    [HttpPost("gatherings")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Gathering>> PlanGathering(Guid partyId, [FromBody] PlanGatheringRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.StartsAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            return BadRequest("Title and a future start time are required.");
        var (hostId, hostName) = await AuthorAsync(partyId, request.HostId);
        Gathering g;
        ActOutcome outcome;
        if (request.PlaceId is { } placeId)
        {
            var place = (await grains.GetGrain<World.Places.IPlacesGrain>(partyId).GetSnapshotAsync()).Places
                .FirstOrDefault(p => p.Id == placeId && p.Kind == World.Places.PlaceKinds.Public && !p.Private);
            if (place is null) return BadRequest("Unknown public place.");
            (g, outcome) = await CalendarApp.PlanAtPlaceAsync(partyId, hostId, hostName, request.Title.Trim(), place, request.StartsAt, DateTimeOffset.UtcNow);
        }
        else
        {
            var room = (await grains.GetGrain<IPartyGrain>(partyId).GetChatGroups())
                .FirstOrDefault(r => r.Id == request.RoomId && r.Kind == ChatGroupKinds.Room);
            if (room is null) return BadRequest("Unknown room.");
            (g, outcome) = await CalendarApp.PlanAsync(partyId, hostId, hostName, request.Title.Trim(), room, request.StartsAt, DateTimeOffset.UtcNow);
        }
        await RecordAsync(partyId, hostId, hostName, CalendarApp.Id, outcome);
        posthog.Track(HttpContext, "gathering_planned", new Dictionary<string, object>
        {
            ["party_id"] = partyId, ["gathering_id"] = g.Id, ["at_place"] = request.PlaceId is not null,
        });
        return Ok(g);
    }

    /// <summary>Author: call a Gathering off.</summary>
    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpDelete("gatherings/{gatheringId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> CancelGathering(Guid partyId, Guid gatheringId)
        => await grains.GetGrain<ICalendarGrain>(partyId).CancelAsync(gatheringId) ? NoContent() : NotFound();

    [HttpPost("gatherings/{gatheringId:guid}/rsvp")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Gathering>> Rsvp(Guid partyId, Guid gatheringId, [FromBody] RsvpRequest request)
    {
        var (id, name) = await AuthorAsync(partyId, request.PersonaId);
        if (await CalendarApp.RsvpAsync(partyId, gatheringId, id, request.Going) is not { } done) return NotFound();
        await RecordAsync(partyId, id, name, CalendarApp.Id, done.Outcome);
        posthog.Track(HttpContext, "gathering_rsvped", new Dictionary<string, object>
        {
            ["party_id"] = partyId, ["gathering_id"] = gatheringId, ["going"] = request.Going,
        });
        return Ok(done.Gathering);
    }

    // ─── Diary ────────────────────────────────────────────────────────────────

    [HttpGet("diary/{personaId:guid}")]
    public async Task<ActionResult<List<DiaryEntry>>> GetDiary(Guid partyId, Guid personaId)
        => Ok(await grains.GetGrain<IDiaryGrain>(WorldKeys.Life(partyId, personaId)).GetEntriesAsync());

    private IWorldClockGrain Clock(Guid partyId) => grains.GetGrain<IWorldClockGrain>(partyId);
    private IBoardGrain Board(Guid partyId) => grains.GetGrain<IBoardGrain>(partyId);

    /// <summary>The Player's act lands in the activity log exactly like a Life turn's.</summary>
    private Task RecordAsync(Guid partyId, Guid actorId, string actorName, string app, ActOutcome outcome)
        => Clock(partyId).RecordAsync(new WorldActivity
        {
            PersonaId = actorId, PersonaName = actorName, App = app, Action = outcome.Action,
            Summary = outcome.Summary, TargetId = outcome.TargetId, WithPersonaId = outcome.WithPersonaId,
        });

    /// <summary>Capture views from the Party cast; unknown authors are someone human.</summary>
    private async Task<ViewsOf> ViewsOfAsync(Guid partyId)
    {
        var cast = await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync();
        return ids => ids
            .Select(id => cast.FirstOrDefault(c => c.Id == id) is { } c
                ? new ParticipantView(c.Id, c.Name, c.DefaultDriver)
                : new ParticipantView(id, "someone", DriverKind.User))
            .ToList();
    }

    private async Task<List<WorldActivity>> VisibleAsync(Guid partyId, IEnumerable<WorldActivity> activities)
    {
        var viewer = await actors.ViewerAsync(User, partyId);
        var humans = await actors.HumansAsync(partyId);
        return activities.Where(a => AccessRules.CanSeeActivity(viewer, a, humans)).ToList();
    }

    private async Task<(Guid Id, string Name)> AuthorAsync(Guid partyId, Guid? authorId)
    {
        var id = await actors.ActAsync(User, partyId, authorId);
        var info = await grains.GetGrain<IPartyGrain>(partyId).GetParty();
        return (id, info.Participants.FirstOrDefault(p => p.Id == id)?.Name ?? "You");
    }
}

public sealed record UpdateWorldRequest(bool? Running, int? TickSeconds, int? MaxActionsPerHour, int? AmbientMinutes = null);
public sealed record TickRequest(Guid? PersonaId, string? Nudge);
public sealed record TickResponse(bool Started);
public sealed record AnnounceRequest(string Text);
public sealed record InviteResidentRequest(Guid PersonaId);
public sealed record PlanGatheringRequest(string Title, Guid RoomId, long StartsAt, Guid? HostId, Guid? PlaceId = null);
public sealed record RsvpRequest(Guid? PersonaId, bool Going = true);
public sealed record PlayerResponse(Guid? PlayerId, string? Name, bool IsAdmin, bool Auto = false, string? Mission = null, string? Aura = null);

public sealed record SetAuraRequest(string? Aura);
public sealed record CreatePlayerRequest(string Name);
public sealed record SetAutoRequest(bool On, string? Mission);
public sealed record CreateBoardPostRequest(string Body, Guid? AuthorId);
public sealed record BoardRespectRequest(Guid? ReplyId, bool Given = true, Guid? AuthorId = null);
public sealed record CreateBoardReplyRequest(string Body, Guid? AuthorId);
public sealed record OpenDirectChatRequest(Guid WithPersonaId, Guid? FromPersonaId);
public sealed record DirectChatSummary(Guid ChatGroupId, Guid WithId, string WithName, string? LastMessage, Guid? LastSenderId, long LastAt);
public sealed record LifeSummary(Guid PersonaId, string Name, DriverKind Driver, double Chattiness, bool Busy, bool IsPlayer, WorldActivity? LastActivity);
