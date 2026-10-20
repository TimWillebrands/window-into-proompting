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
public sealed class WorldController(IGrainFactory grains, WorldMemory memory) : ControllerBase
{
    private const int MaxPostLength = 4000;

    [HttpGet]
    public async Task<ActionResult<WorldStatus>> GetStatus(Guid partyId)
        => Ok(await Clock(partyId).GetStatusAsync());

    [HttpPut]
    public async Task<ActionResult<WorldStatus>> Update(Guid partyId, [FromBody] UpdateWorldRequest request)
        => Ok(await Clock(partyId).UpdateAsync(request.Running, request.TickSeconds, request.MaxActionsPerHour));

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
        var thread = await Board(partyId).CreateThreadAsync(authorId, authorName, request.Title, request.Body);
        await Clock(partyId).RecordAsync(new WorldActivity
        {
            PersonaId = authorId, PersonaName = authorName, App = "board", Action = "post",
            Summary = $"posted \"{thread.Title}\"", TargetId = thread.Id,
        });
        return Ok(thread);
    }

    [HttpPost("board/{threadId:guid}/replies")]
    public async Task<ActionResult<BoardThread>> Reply(Guid partyId, Guid threadId, [FromBody] CreateBoardReplyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > MaxPostLength)
            return BadRequest("Body is required (≤ 4000 chars).");
        var (authorId, authorName) = await AuthorAsync(partyId, request.AuthorId);
        var thread = await Board(partyId).ReplyAsync(threadId, authorId, authorName, request.Body);
        if (thread is null) return NotFound();
        await Clock(partyId).RecordAsync(new WorldActivity
        {
            PersonaId = authorId, PersonaName = authorName, App = "board", Action = "reply",
            Summary = $"replied to \"{thread.Title}\"", TargetId = thread.Id,
        });

        // The thread's Personas will remember what the human said to them.
        var cast = await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync();
        var context = BoardApp.BoardMessages(thread);
        var present = context.Select(m => m.SenderId).Distinct()
            .Select(id => cast.FirstOrDefault(c => c.Id == id) is { } c
                ? new ParticipantView(c.Id, c.Name, c.DefaultDriver)
                : new ParticipantView(id, "someone", DriverKind.User))
            .ToList();
        _ = memory.CaptureAsync(partyId, thread.Id, context[^1].MessageId, context, present, CancellationToken.None);
        return Ok(thread);
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

    // ─── Diary ────────────────────────────────────────────────────────────────

    [HttpGet("diary/{personaId:guid}")]
    public async Task<ActionResult<List<DiaryEntry>>> GetDiary(Guid partyId, Guid personaId)
        => Ok(await grains.GetGrain<IDiaryGrain>(WorldKeys.Life(partyId, personaId)).GetEntriesAsync());

    private IWorldClockGrain Clock(Guid partyId) => grains.GetGrain<IWorldClockGrain>(partyId);
    private IBoardGrain Board(Guid partyId) => grains.GetGrain<IBoardGrain>(partyId);

    private async Task<(Guid Id, string Name)> AuthorAsync(Guid partyId, Guid? authorId)
    {
        var id = authorId ?? await Clock(partyId).EnsurePlayerAsync(null);
        var info = await grains.GetGrain<IPartyGrain>(partyId).GetParty();
        return (id, info.Participants.FirstOrDefault(p => p.Id == id)?.Name ?? "You");
    }
}

public sealed record UpdateWorldRequest(bool? Running, int? TickSeconds, int? MaxActionsPerHour);
public sealed record TickRequest(Guid? PersonaId, string? Nudge);
public sealed record TickResponse(bool Started);
public sealed record EnsurePlayerRequest(string? Name);
public sealed record AnnounceRequest(string Text);
public sealed record PlayerResponse(Guid PlayerId);
public sealed record CreateBoardPostRequest(string Title, string Body, Guid? AuthorId);
public sealed record CreateBoardReplyRequest(string Body, Guid? AuthorId);
public sealed record OpenDirectChatRequest(Guid WithPersonaId, Guid? FromPersonaId);
public sealed record DirectChatSummary(Guid ChatGroupId, Guid WithId, string WithName, string? LastMessage, Guid? LastSenderId, long LastAt);
public sealed record LifeSummary(Guid PersonaId, string Name, DriverKind Driver, double Chattiness, bool Busy, bool IsPlayer, WorldActivity? LastActivity);
