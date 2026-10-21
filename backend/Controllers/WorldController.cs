using PartyTown.Services.Memory;
using Microsoft.AspNetCore.Mvc;
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
public sealed class WorldController(IGrainFactory grains, IEnumerable<IWorldApp> apps) : ControllerBase
{
    private BoardApp BoardApp => apps.OfType<BoardApp>().Single();
    private CalendarApp CalendarApp => apps.OfType<CalendarApp>().Single();

    private const int MaxPostLength = 4000;

    [HttpGet]
    public async Task<ActionResult<WorldStatus>> GetStatus(Guid partyId)
        => Ok(await Clock(partyId).GetStatusAsync());

    [HttpPut]
    public async Task<ActionResult<WorldStatus>> Update(Guid partyId, [FromBody] UpdateWorldRequest request)
        => Ok(await Clock(partyId).UpdateAsync(request.Running, request.TickSeconds, request.MaxActionsPerHour, request.AmbientMinutes));

    /// <summary>Give one Persona (or a weighted pick) a Life turn right now.</summary>
    [HttpPost("tick")]
    public async Task<ActionResult<TickResponse>> Tick(Guid partyId, [FromBody] TickRequest request)
        => Ok(new TickResponse(await Clock(partyId).TickNowAsync(request.PersonaId, request.Nudge)));

    [HttpGet("activities")]
    public async Task<ActionResult<List<WorldActivity>>> GetActivities(Guid partyId, [FromQuery] int limit = 100, [FromQuery] Guid? personaId = null)
        => Ok(await Clock(partyId).GetActivitiesAsync(limit, personaId));

    /// <summary>Who lives here and what they're up to.</summary>
    [HttpGet("lives")]
    public async Task<ActionResult<List<LifeSummary>>> GetLives(Guid partyId)
    {
        var cast = await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync();
        var status = await Clock(partyId).GetStatusAsync();
        var activities = await Clock(partyId).GetActivitiesAsync(300, null);
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
    [HttpPost("happenings")]
    public async Task<ActionResult<WorldActivity>> Announce(Guid partyId, [FromBody] AnnounceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 600)
            return BadRequest("Text is required (≤ 600 chars).");
        return Ok(await Clock(partyId).AnnounceAsync(request.Text.Trim()));
    }

    /// <summary>Invite a library Persona to live in this World (LLM-driven Participant).</summary>
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
    [HttpDelete("residents/{personaId:guid}")]
    public async Task<ActionResult> Evict(Guid partyId, Guid personaId)
    {
        var party = grains.GetGrain<IPartyGrain>(partyId);
        var info = await party.GetParty();
        if (info.Participants.All(p => p.Id != personaId || p.Driver != DriverKind.LLM)) return NotFound();
        if (await party.RemoveParticipantAsync(personaId) is not { } leaving) return NotFound();
        await Clock(partyId).RecordAsync(new WorldActivity
        {
            PersonaId = personaId, PersonaName = leaving.Name, App = "world", Action = "leave", Summary = "moved out of the world",
        });
        return NoContent();
    }

    [HttpPost("player")]
    public async Task<ActionResult<PlayerResponse>> EnsurePlayer(Guid partyId, [FromBody] EnsurePlayerRequest request)
        => Ok(new PlayerResponse(await Clock(partyId).EnsurePlayerAsync(request.Name)));

    // ─── Board ────────────────────────────────────────────────────────────────

    [HttpGet("board")]
    public async Task<ActionResult<List<BoardThread>>> GetBoard(Guid partyId)
        => Ok(await Board(partyId).GetThreadsAsync());

    [HttpGet("board/{threadId:guid}")]
    public async Task<ActionResult<BoardThread>> GetThread(Guid partyId, Guid threadId)
        => await Board(partyId).GetThreadAsync(threadId) is { } t ? Ok(t) : NotFound();

    [HttpPost("board")]
    public async Task<ActionResult<BoardThread>> CreatePost(Guid partyId, [FromBody] CreateBoardPostRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > MaxPostLength)
            return BadRequest("Title and body are required (body ≤ 4000 chars).");
        var (authorId, authorName) = await AuthorAsync(partyId, request.AuthorId);
        var (thread, outcome) = await BoardApp.PostAsync(partyId, authorId, authorName, request.Title, request.Body, await ViewsOfAsync(partyId));
        await RecordAsync(partyId, authorId, authorName, BoardApp.Id, outcome);
        return Ok(thread);
    }

    [HttpPost("board/{threadId:guid}/replies")]
    public async Task<ActionResult<BoardThread>> Reply(Guid partyId, Guid threadId, [FromBody] CreateBoardReplyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > MaxPostLength)
            return BadRequest("Body is required (≤ 4000 chars).");
        var (authorId, authorName) = await AuthorAsync(partyId, request.AuthorId);
        if (await BoardApp.ReplyAsync(partyId, threadId, authorId, authorName, request.Body, await ViewsOfAsync(partyId)) is not { } done)
            return NotFound();
        await RecordAsync(partyId, authorId, authorName, BoardApp.Id, done.Outcome);
        return Ok(done.Thread);
    }

    // ─── Messenger ────────────────────────────────────────────────────────────

    /// <summary>DM threads of one Participant (defaults to the Player), newest first.</summary>
    [HttpGet("messenger")]
    public async Task<ActionResult<List<DirectChatSummary>>> GetDirectChats(Guid partyId, [FromQuery] Guid? personaId = null)
    {
        var who = personaId ?? (await Clock(partyId).GetStatusAsync()).PlayerId;
        if (who is null) return Ok(new List<DirectChatSummary>());
        var party = grains.GetGrain<IPartyGrain>(partyId);
        var info = await party.GetParty();
        var names = info.Participants.ToDictionary(p => p.Id, p => p.Name);

        var result = new List<DirectChatSummary>();
        foreach (var dm in (await party.GetChatGroups()).Where(g => g.Kind == ChatGroupKinds.Direct && g.MemberIds?.Contains(who.Value) == true))
        {
            var other = dm.MemberIds!.First(id => id != who.Value);
            var last = (await grains.GetGrain<IChatGroupGrain>(dm.Id).GetRecentMessagesAsync(1)).LastOrDefault();
            result.Add(new DirectChatSummary(dm.Id, other, names.GetValueOrDefault(other, "someone"),
                last?.Content, last?.SenderId, last?.SendAt ?? dm.CreatedAt));
        }
        return Ok(result.OrderByDescending(r => r.LastAt).ToList());
    }

    [HttpPost("messenger")]
    public async Task<ActionResult<ChatGroupInfo>> OpenDirectChat(Guid partyId, [FromBody] OpenDirectChatRequest request)
    {
        var from = request.FromPersonaId ?? await Clock(partyId).EnsurePlayerAsync(null);
        if (from == request.WithPersonaId) return BadRequest("Cannot DM yourself.");
        var info = await grains.GetGrain<IPartyGrain>(partyId).GetParty();
        if (info.Participants.All(p => p.Id != request.WithPersonaId)) return NotFound();
        return Ok(await grains.GetGrain<IPartyGrain>(partyId).GetOrCreateDirectChatAsync(from, request.WithPersonaId));
    }

    // ─── Calendar ─────────────────────────────────────────────────────────────

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
                .FirstOrDefault(p => p.Id == placeId && p.Kind == World.Places.PlaceKinds.Public);
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
        return Ok(g);
    }

    [HttpPost("gatherings/{gatheringId:guid}/rsvp")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Gathering>> Rsvp(Guid partyId, Guid gatheringId, [FromBody] RsvpRequest request)
    {
        var (id, name) = await AuthorAsync(partyId, request.PersonaId);
        if (await CalendarApp.RsvpAsync(partyId, gatheringId, id, request.Going) is not { } done) return NotFound();
        await RecordAsync(partyId, id, name, CalendarApp.Id, done.Outcome);
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

    private async Task<(Guid Id, string Name)> AuthorAsync(Guid partyId, Guid? authorId)
    {
        var id = authorId ?? await Clock(partyId).EnsurePlayerAsync(null);
        var info = await grains.GetGrain<IPartyGrain>(partyId).GetParty();
        return (id, info.Participants.FirstOrDefault(p => p.Id == id)?.Name ?? "You");
    }
}

public sealed record UpdateWorldRequest(bool? Running, int? TickSeconds, int? MaxActionsPerHour, int? AmbientMinutes = null);
public sealed record TickRequest(Guid? PersonaId, string? Nudge);
public sealed record TickResponse(bool Started);
public sealed record EnsurePlayerRequest(string? Name);
public sealed record AnnounceRequest(string Text);
public sealed record InviteResidentRequest(Guid PersonaId);
public sealed record PlanGatheringRequest(string Title, Guid RoomId, long StartsAt, Guid? HostId, Guid? PlaceId = null);
public sealed record RsvpRequest(Guid? PersonaId, bool Going = true);
public sealed record PlayerResponse(Guid PlayerId);
public sealed record CreateBoardPostRequest(string Title, string Body, Guid? AuthorId);
public sealed record CreateBoardReplyRequest(string Body, Guid? AuthorId);
public sealed record OpenDirectChatRequest(Guid WithPersonaId, Guid? FromPersonaId);
public sealed record DirectChatSummary(Guid ChatGroupId, Guid WithId, string WithName, string? LastMessage, Guid? LastSenderId, long LastAt);
public sealed record LifeSummary(Guid PersonaId, string Name, DriverKind Driver, double Chattiness, bool Busy, bool IsPlayer, WorldActivity? LastActivity);
