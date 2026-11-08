using System.ComponentModel;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Auth;
using PartyTown.World;
using PartyTown.World.Apps;
using PartyTown.World.Places;
using PartyTown.World.Work.Cdu;

namespace PartyTown.Mcp;

/// <summary>Living in the World as a Player (ADR 0046): walk, talk, DM, the Board, and the CDU as spectators see it.</summary>
[McpServerToolType]
public sealed class WorldTools(IGrainFactory grains, Actors actors, IOptions<McpOptions> mcp, IEnumerable<IWorldApp> apps, Cytheurgy cdu)
{
    private const string Whom = "Participant id; only admins may name someone else. Default: your own Player.";
    private Guid World => mcp.Value.World;
    private IPlacesGrain Places => grains.GetGrain<IPlacesGrain>(World);
    private IPartyGrain Party => grains.GetGrain<IPartyGrain>(World);
    private BoardApp Board => apps.OfType<BoardApp>().Single();

    [McpServerTool(Name = "go"), Description("Walk to a Place (id from list_places, optional spot) or over to someone. Walking takes a while; get_situation says when you're there.")]
    public Task<string> GoAsync(
        ClaimsPrincipal user,
        [Description("Place id from list_places.")] Guid? placeId = null,
        [Description("Spot id in that Place, from get_place.")] string? spotId = null,
        [Description("Walk over to this Participant instead.")] Guid? toParticipantId = null,
        [Description(Whom)] Guid? participantId = null) => Guard(async () =>
    {
        var who = await actors.ActAsync(user, World, participantId);
        await Places.SetIdleAsync(who, false);
        var body = toParticipantId is { } other ? await Places.JoinAsync(who, other)
            : placeId is { } place ? await Places.MoveAsync(who, place, spotId)
            : throw new McpException("Give placeId or toParticipantId.");
        if (body is null) throw new McpException("Can't go there (private, full, closed, or unknown). A Home needs a knock.");
        var dest = (await Places.GetSnapshotAsync()).Places.FirstOrDefault(p => p.Id == (body.DestPlaceId ?? body.PlaceId));
        return body.ArriveAt is { } at && at > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ? $"On the way to {dest?.Name}, there in about {Math.Max(1, (at - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000)}s."
            : $"At {dest?.Name}.";
    });

    [McpServerTool(Name = "knock"), Description("Knock on someone's Home or a closed workplace (Place id). Fails when nobody's in to answer.")]
    public Task<string> KnockAsync(ClaimsPrincipal user, [Description("Place id of the Home or workplace.")] Guid placeId, [Description(Whom)] Guid? participantId = null) => Guard(async () =>
    {
        var who = await actors.ActAsync(user, World, participantId);
        return await Places.KnockAsync(who, placeId) ? "Knocked. Wait for someone to let you in." : throw new McpException("Nobody's in to answer (or they're asleep).");
    });

    [McpServerTool(Name = "say"), Description("Say something out loud where you are. Everyone at your spot hears a talk; a shout carries across the Place; a whisper needs someone at your spot.")]
    public Task<string> SayAsync(
        ClaimsPrincipal user,
        [Description("What you say, as you'd say it.")] string text,
        [Description("talk, whisper or shout.")] string volume = Volumes.Talk,
        [Description("Who it's meant for (a whisper needs one).")] Guid? toParticipantId = null,
        [Description(Whom)] Guid? participantId = null) => Guard(async () =>
    {
        if (string.IsNullOrWhiteSpace(text)) throw new McpException("Say something.");
        if (volume is not (Volumes.Whisper or Volumes.Talk or Volumes.Shout)) throw new McpException("volume is talk, whisper or shout.");
        var who = await actors.ActAsync(user, World, participantId);
        var (room, place) = await RoomOfAsync(who);
        await Places.SetIdleAsync(who, false);
        await Places.SetVoiceAsync(who, volume, toParticipantId);
        await grains.GetGrain<IChatGroupGrain>(room).SendNewMessageAsync(await actors.SpeakAsync(user, room, who), text.Trim());
        return $"Said at {place}. listen to hear what comes back.";
    });

    [McpServerTool(Name = "listen", ReadOnly = true), Description("The last things said where they are, oldest first, and talk they noticed but couldn't make out.")]
    public Task<string> ListenAsync(ClaimsPrincipal user, [Description("How many lines, up to 40.")] int count = 15, [Description(Whom)] Guid? participantId = null) => Guard(async () =>
    {
        var who = participantId ?? await actors.PlayerAsync(user, World);
        var (room, place) = await RoomOfAsync(who);
        var lines = await grains.GetGrain<IChatGroupGrain>(room).GetRecentMessagesAsync(Math.Clamp(count, 1, 40));
        var names = await NamesAsync();
        var sb = new StringBuilder($"At {place}:\n");
        foreach (var m in lines) sb.AppendLine(Line(m, names));
        var overheard = await Places.GetOverheardAsync(who);
        if (overheard.Count > 0) sb.AppendLine($"Overheard: {string.Join("; ", overheard.TakeLast(5).Select(o => $"{o.SpeakerName} {o.Tone} by the {o.SpotId}"))}");
        return sb.ToString();
    });

    [McpServerTool(Name = "send_dm"), Description("Send a Messenger DM to someone; Personas answer in their own time. read_dms shows the thread.")]
    public Task<string> SendDmAsync(ClaimsPrincipal user, [Description("Who to write to, from list_participants.")] Guid toParticipantId, [Description("The message.")] string text, [Description(Whom)] Guid? participantId = null) => Guard(async () =>
    {
        if (string.IsNullOrWhiteSpace(text)) throw new McpException("Write something.");
        var who = await actors.ActAsync(user, World, participantId);
        if (who == toParticipantId) throw new McpException("Not to yourself.");
        if ((await Party.GetParty()).Participants.All(p => p.Id != toParticipantId)) throw new McpException("No such Participant.");
        var chat = await Party.GetOrCreateDirectChatAsync(who, toParticipantId);
        await grains.GetGrain<IChatGroupGrain>(chat.Id).SendNewMessageAsync(await actors.SpeakAsync(user, chat.Id, who), text.Trim());
        return "Sent.";
    });

    [McpServerTool(Name = "read_dms", ReadOnly = true), Description("Their DM threads, or with withParticipantId the last messages of that one thread.")]
    public Task<string> ReadDmsAsync(ClaimsPrincipal user, [Description("The other person; default lists threads.")] Guid? withParticipantId = null, [Description("How many lines, up to 40.")] int count = 15, [Description(Whom)] Guid? participantId = null) => Guard(async () =>
    {
        var who = await actors.ActAsync(user, World, participantId);
        var names = await NamesAsync();
        var dms = (await Party.GetChatGroups()).Where(g => g.Kind == ChatGroupKinds.Direct && g.MemberIds?.Contains(who) == true).ToList();
        if (withParticipantId is { } other)
        {
            var dm = dms.FirstOrDefault(d => d.MemberIds!.Contains(other)) ?? throw new McpException("No thread with them yet; send_dm starts one.");
            var lines = await grains.GetGrain<IChatGroupGrain>(dm.Id).GetRecentMessagesAsync(Math.Clamp(count, 1, 40));
            return string.Join("\n", lines.Select(m => Line(m, names)));
        }
        var sb = new StringBuilder();
        foreach (var dm in dms)
        {
            var with = dm.MemberIds!.First(id => id != who);
            var last = (await grains.GetGrain<IChatGroupGrain>(dm.Id).GetRecentMessagesAsync(1)).LastOrDefault();
            sb.AppendLine($"{names.GetValueOrDefault(with, "?")} ({with}): {(last is null ? "(empty)" : Line(last, names))}");
        }
        return sb.Length == 0 ? "No DMs yet." : sb.ToString();
    });

    [McpServerTool(Name = "read_board", ReadOnly = true), Description("The Board: threads newest first, or with threadId one thread with its replies.")]
    public Task<string> ReadBoardAsync([Description("Thread id; default lists threads.")] Guid? threadId = null, [Description("How many threads, up to 30.")] int count = 10) => Guard(async () =>
    {
        var board = grains.GetGrain<IBoardGrain>(World);
        if (threadId is { } id)
        {
            var t = await board.GetThreadAsync(id) ?? throw new McpException("No such thread.");
            var sb = new StringBuilder($"{t.AuthorName}: {t.Title}\n{t.Body}\n");
            foreach (var r in t.Replies) sb.AppendLine($"  {r.AuthorName}: {r.Body}");
            return sb.ToString();
        }
        var threads = (await board.GetThreadsAsync()).OrderByDescending(t => t.LastActivityAt).Take(Math.Clamp(count, 1, 30));
        return string.Join("\n", threads.Select(t => $"[{t.Id}] {t.AuthorName}: {t.Title} ({t.Replies.Count} replies)"));
    });

    [McpServerTool(Name = "post_board"), Description("Post on the Board, or reply to a thread with threadId. Everyone reads it.")]
    public Task<string> PostBoardAsync(ClaimsPrincipal user, [Description("The post.")] string text, [Description("Reply to this thread.")] Guid? threadId = null, [Description(Whom)] Guid? participantId = null) => Guard(async () =>
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length > BoardApp.MaxLength) throw new McpException($"Write something, at most {BoardApp.MaxLength} characters.");
        var who = await actors.ActAsync(user, World, participantId);
        var names = await NamesAsync();
        var name = names.GetValueOrDefault(who, "You");
        var cast = await Party.GetCastAsync();
        ViewsOf views = ids => ids.Select(i => cast.FirstOrDefault(c => c.Id == i) is { } c
            ? new ParticipantView(c.Id, c.Name, c.DefaultDriver) : new ParticipantView(i, "someone", DriverKind.User)).ToList();
        var (thread, outcome) = threadId is { } id
            ? await Board.ReplyAsync(World, id, who, name, text.Trim(), views) ?? throw new McpException("No such thread.")
            : await Board.PostAsync(World, who, name, text.Trim(), views);
        await grains.GetGrain<IWorldClockGrain>(World).RecordAsync(new WorldActivity
        {
            PersonaId = who, PersonaName = name, App = Board.Id, Action = outcome.Action, Summary = outcome.Summary, TargetId = outcome.TargetId,
        });
        return $"Posted in [{thread.Id}] {thread.Title}.";
    });

    [McpServerTool(Name = "get_cdu", ReadOnly = true), Description("The CDU as spectators see it: up or dark, its allowance, trunk, PR, and who said what to it lately.")]
    public Task<string> GetCduAsync([Description("How many exchanges, up to 20.")] int count = 6) => Guard(async () =>
    {
        var status = await cdu.StatusAsync(CancellationToken.None, fresh: true);
        var state = await grains.GetGrain<ICduGrain>(World).GetAsync();
        var sb = new StringBuilder();
        sb.AppendLine(status is null ? "Dark: not answering." : $"Up, {(status.Awake ? $"awake (hearing {status.Hearing})" : "asleep")}, model {status.Model}.");
        if (status?.Allowance is { } a) sb.AppendLine($"Spent ${a.Usd:0.00} of ${a.CapUsd:0.00} today.");
        if (status?.Trunk?.Ahead is { Count: > 0 } ahead) sb.AppendLine($"Trunk: {string.Join("; ", ahead.Select(c => $"{c.Subject} ({c.Author})"))}");
        if (status?.Pr is { } pr) sb.AppendLine($"Agora: #{pr.Number} {pr.Title} [{pr.State}, checks {pr.Checks}] {pr.Url}");
        sb.AppendLine($"Staff on shift: {(state.OnShift ? "yes" : "no")}.");
        foreach (var e in state.Exchanges.AsEnumerable().Reverse().Take(Math.Clamp(count, 1, 20)))
            sb.AppendLine($"- {e.SpeakerName} ({e.Kind}, {e.Status}): {WorldStream.Clip(e.Text, 300)}\n  CDU: {WorldStream.Clip(e.Reply ?? "…", 400)}");
        return sb.ToString();
    });

    private async Task<(Guid Room, string Place)> RoomOfAsync(Guid who)
    {
        var snapshot = await Places.GetSnapshotAsync();
        var body = snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == who) ?? throw new McpException("They have no Body yet.");
        if (body.DestPlaceId is not null) throw new McpException("Still walking; wait till they arrive.");
        var place = snapshot.Places.FirstOrDefault(p => p.Id == body.PlaceId);
        return place?.TalkRoomId is { } room ? (room, place.Name) : throw new McpException("Nowhere to talk here.");
    }

    private async Task<Dictionary<Guid, string>> NamesAsync()
        => (await Party.GetParty()).Participants.ToDictionary(p => p.Id, p => p.Name);

    private static string Line(ChatMessage m, Dictionary<Guid, string> names)
    {
        var at = m.SendAt is { } t ? DateTimeOffset.FromUnixTimeMilliseconds(t).ToString("ddd HH:mm") : "";
        var voice = m.Volume is { } v && v != Volumes.Talk ? $" ({v})" : "";
        return $"[{at}] {names.GetValueOrDefault(m.SenderId, m.SenderType)}{voice}: {m.Content}";
    }

    private static async Task<T> Guard<T>(Func<Task<T>> run)
    {
        try
        {
            return await run();
        }
        catch (Exception ex) when (ex is ForbiddenActorException or NoPlayerException or OnAutoException)
        {
            throw new McpException(ex.Message);
        }
    }
}
