using PartyTown.Services.Memory;
using System.Text;
using PartyTown.Grains.Generation;
using PartyTown.Model;

namespace PartyTown.World.Apps;

/// <summary>Board: asynchronous, public, threaded. Written in voice; every post is a captured moment.</summary>
public sealed class BoardApp(IGrainFactory grains, IWorldLlm llm, WorldMemory memory, ILogger<BoardApp> logger) : IWorldApp
{
    private const string PostShape = """{"title": "<short catchy title>", "body": "<the post>"}""";
    private const string ReplyShape = """{"body": "<your reply>"}""";

    public string Id => "board";

    public async Task<AppPerception?> PerceiveAsync(LifeContext ctx, CancellationToken ct)
    {
        var threads = (await grains.GetGrain<IBoardGrain>(ctx.PartyId).GetThreadsAsync()).Take(6).ToList();
        var affordances = new List<Affordance> { new(Id, "Write a new post on the Board", 1, null, "post") };
        if (threads.Count == 0)
            return new AppPerception(Id, "Board (public forum)", "The Board is empty — nobody has posted yet.", affordances);

        var sb = new StringBuilder();
        foreach (var t in threads)
        {
            var lastAuthor = t.Replies.LastOrDefault()?.AuthorId ?? t.AuthorId;
            var youIn = t.AuthorId == ctx.Self.Id || t.Replies.Any(r => r.AuthorId == ctx.Self.Id);
            var answeredYou = youIn && lastAuthor != ctx.Self.Id;
            var author = t.AuthorId == ctx.Self.Id ? "you" : t.AuthorName;
            sb.AppendLine($"- \"{t.Title}\" by {author} ({t.Replies.Count} replies, last {WorldStream.Ago(t.LastActivityAt, ctx.Now)}){(answeredYou ? " — someone answered you" : "")}");
            sb.AppendLine($"    {WorldStream.Clip(t.Body, 160)}");
            if (t.Replies.LastOrDefault() is { } r)
                sb.AppendLine($"    latest reply, {(r.AuthorId == ctx.Self.Id ? "you" : r.AuthorName)}: {WorldStream.Clip(r.Body, 120)}");

            if (lastAuthor == ctx.Self.Id) continue;
            var priority = 1 + (answeredYou ? 2 : 0) + (ctx.Mentions(t.Body) || t.Replies.Any(x => ctx.Mentions(x.Body)) ? 1 : 0);
            affordances.Add(new Affordance(Id, $"Reply to \"{WorldStream.Clip(t.Title, 60)}\" by {author}", priority, t.Id, "reply"));
        }

        return new AppPerception(Id, "Board (public forum)", sb.ToString().TrimEnd(), affordances);
    }

    public async Task<ActOutcome?> ActAsync(LifeContext ctx, Affordance affordance, string intent, string? thought, CancellationToken ct)
    {
        var board = grains.GetGrain<IBoardGrain>(ctx.PartyId);
        var system = $"""
{WorldLlm.Identity(ctx.Self)}

You are writing on the Board — the public forum of the small online world you live in, read by everyone here. Write as yourself: your voice, your opinions, your quirks. Forum register: casual, 1-3 short paragraphs, no hashtags, no sign-off, no meta commentary.
Output ONLY JSON.
""";

        if (affordance.Verb == "post")
        {
            var titles = (await board.GetThreadsAsync()).Take(10).Select(t => $"- {t.Title}");
            var user = $"""
What you want to post about: {intent}
{(thought is null ? "" : $"What's on your mind: {thought}")}

Recent post titles (don't duplicate them):
{string.Join("\n", titles)}

Output: {PostShape}
""";
            var json = await llm.CompleteJsonAsync(JobComplexity.CharacterVoice, system, user, ct, 0.9, ["title", "body"]);
            var title = json?["title"]?.GetValue<string>();
            var body = json?["body"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body)) return null;

            var thread = await board.CreateThreadAsync(ctx.Self.Id, ctx.Self.Name, title, body);
            CaptureDetached(ctx, thread);
            return new ActOutcome("post", $"posted \"{thread.Title}\"", thread.Id);
        }

        if (affordance.TargetId is not { } threadId || await board.GetThreadAsync(threadId) is not { } target) return null;
        var replyUser = $"""
The thread you're replying to:
{Render(target, ctx)}

What you want to say: {intent}
{(thought is null ? "" : $"How it hits you: {thought}")}

Output: {ReplyShape}
""";
        var replyJson = await llm.CompleteJsonAsync(JobComplexity.CharacterVoice, system, replyUser, ct, 0.9, ["body"]);
        var replyBody = replyJson?["body"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(replyBody)) return null;

        var updated = await board.ReplyAsync(threadId, ctx.Self.Id, ctx.Self.Name, replyBody);
        if (updated is null) return null;
        CaptureDetached(ctx, updated);
        return new ActOutcome("reply", $"replied to \"{updated.Title}\": \"{WorldStream.Clip(replyBody, 140)}\"", threadId);
    }

    private static string Render(BoardThread t, LifeContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"\"{t.Title}\" by {(t.AuthorId == ctx.Self.Id ? "you" : t.AuthorName)}:");
        sb.AppendLine(t.Body);
        foreach (var r in t.Replies.TakeLast(8))
            sb.AppendLine($"— {(r.AuthorId == ctx.Self.Id ? "you" : r.AuthorName)}: {r.Body}");
        return sb.ToString().TrimEnd();
    }

    /// <summary>The post just written becomes an Event recollected by everyone in the thread.</summary>
    private void CaptureDetached(LifeContext ctx, BoardThread thread)
    {
        var moment = MomentOf(ctx.PartyId, thread, ids => ids.Select(ctx.ViewOf).ToList());
        _ = Task.Run(() => memory.CaptureAsync(moment, CancellationToken.None));
    }

    /// <summary>A Board thread as a Moment marked at its latest post; <paramref name="viewsOf"/> resolves its authors.</summary>
    public static Moment MomentOf(Guid partyId, BoardThread thread, Func<IEnumerable<Guid>, List<ParticipantView>> viewsOf)
    {
        var lines = new List<MomentLine> { new(thread.AuthorId, $"[Board post \"{thread.Title}\"] {thread.Body}", thread.CreatedAt) };
        lines.AddRange(thread.Replies.Select(r => new MomentLine(r.AuthorId, $"[Board reply] {r.Body}", r.CreatedAt)));
        return new Moment(partyId, MomentSources.Board, thread.Id, lines, lines.Count - 1,
            viewsOf(lines.Select(l => l.SenderId).Distinct()));
    }
}

/// <summary>Diary: private and reflective. Writing is rest — Consolidation runs alongside.</summary>
public sealed class DiaryApp(IGrainFactory grains, IWorldLlm llm, WorldMemory memory) : IWorldApp
{
    private const string EntryShape = """{"mood": "<one or two words>", "entry": "<the diary entry>"}""";

    public string Id => "diary";

    public async Task<AppPerception?> PerceiveAsync(LifeContext ctx, CancellationToken ct)
    {
        var entries = await grains.GetGrain<IDiaryGrain>(WorldKeys.Life(ctx.PartyId, ctx.Self.Id)).GetEntriesAsync();
        var last = entries.FirstOrDefault();
        var sinceLast = last is null ? ctx.RecentOwn.Count : ctx.RecentOwn.Count(a => a.Ts > last.CreatedAt);
        var block = last is null
            ? "You haven't written in your diary yet."
            : $"Your last diary entry ({WorldStream.Ago(last.CreatedAt, ctx.Now)}): {WorldStream.Clip(last.Body, 140)}";
        var priority = sinceLast >= 4 ? 2 : 0;
        return new AppPerception(Id, "Your diary (private)", block,
            new[] { new Affordance(Id, "Write in your diary and take some time to reflect", priority, null, "write") });
    }

    public async Task<ActOutcome?> ActAsync(LifeContext ctx, Affordance affordance, string intent, string? thought, CancellationToken ct)
    {
        var (memories, stances) = await memory.RecallAsync(
            ctx.PartyId, ctx.Self.Id, ctx.Cast.Select(c => c.Id).ToList(), intent, ct);
        var activity = string.Join("\n", ctx.RecentOwn.Take(6).Select(a => $"- {a.Action} {a.Summary}"));
        var presence = await grains.GetGrain<Self.IPersonaSelfGrain>(WorldKeys.Life(ctx.PartyId, ctx.Self.Id)).GetPresenceAsync(withDay: true);
        var system = $"""
{WorldLlm.Identity(ctx.Self)}

You are writing in your private diary. Nobody else reads it. Be honest with yourself — what happened today (the ordinary parts too), how you feel about the people around you, what you want. Your own voice, plain words, first person, 80-180 words.
Output ONLY JSON: {EntryShape}
""";
        var user = $"""
What you want to write about: {intent}

{presence}

What you've been doing lately:
{(activity.Length == 0 ? "(not much)" : activity)}

Moments on your mind:
{(memories.Count == 0 ? "(nothing in particular)" : string.Join("\n", memories.Take(6).Select(m => $"- {m.Snippet}")))}

Where you stand:
{(stances.Count == 0 ? "(no strong feelings yet)" : string.Join("\n", stances.Select(s => $"- {s}")))}
""";
        var json = await llm.CompleteJsonAsync(JobComplexity.CharacterVoice, system, user, ct, 0.9, ["mood", "entry"]);
        var body = json?["entry"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(body)) return null;
        var mood = json?["mood"]?.GetValue<string>();

        await grains.GetGrain<IDiaryGrain>(WorldKeys.Life(ctx.PartyId, ctx.Self.Id)).AddEntryAsync(body, mood);
        if (mood is not null) await grains.GetGrain<Self.IPersonaSelfGrain>(WorldKeys.Life(ctx.PartyId, ctx.Self.Id)).SetMoodAsync(mood);
        var rest = await memory.RestAsync(ctx.PartyId, ctx.Self, ctx.Cast, ct);
        var formed = rest is { StancesAppended: > 0 } ? $", and {rest.StancesAppended} feeling(s) settled" : "";
        return new ActOutcome("diary", $"wrote in their diary{(mood is null ? "" : $" ({mood})")}{formed}", null);
    }
}
