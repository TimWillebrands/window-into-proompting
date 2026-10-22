using PartyTown.Services.Memory;
using System.Text;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;

namespace PartyTown.World.Apps;

/// <summary>Resolves author ids to the views a Capture needs (Persona path: LifeContext; Player path: Party cast).</summary>
public delegate List<ParticipantView> ViewsOf(IEnumerable<Guid> ids);

/// <summary>Board: asynchronous, public, threaded. Written in voice; every post is a captured moment.</summary>
public sealed class BoardApp(IGrainFactory grains, IWorldLlm llm, WorldMemory memory, ILogger<BoardApp> logger) : IWorldApp
{
    private const string PostShape = """{"title": "<short catchy title>", "body": "<the post>"}""";
    private const string ReplyShape = """{"body": "<your reply, or empty>"}""";

    public string Id => "board";

    /// <summary>Attention key for having the Board open (see <see cref="Self.IPersonaAttentionGrain"/>).</summary>
    public static readonly Guid AttentionKey = new("b0a2d000-0000-4000-8000-000000000001");

    public async Task<AppPerception?> PerceiveAsync(LifeContext ctx, CancellationToken ct)
    {
        var all = await grains.GetGrain<IBoardGrain>(ctx.PartyId).GetThreadsAsync();
        var threads = all.Take(6).ToList();
        var openers = (await grains.GetGrain<Self.IPersonaSelfGrain>(WorldKeys.Life(ctx.PartyId, ctx.Self.Id)).GetStateAsync()).Openers;
        var postedLately = all.Any(t => t.AuthorId == ctx.Self.Id && ctx.Now.ToUnixTimeMilliseconds() - t.CreatedAt < TimeSpan.FromHours(12).TotalMilliseconds);
        var affordances = new List<Affordance> { PostAffordance(openers, postedLately) };
        if (!ctx.InMind(AttentionKey))
            return Glance(ctx, all, affordances);
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
            if (RunItsCourse(t.Replies.Count))
            {
                sb.AppendLine("    (this thread has run its course)");
                continue;
            }
            if (PassedOn(ctx.RecentOwn, t))
            {
                sb.AppendLine("    (you read the latest here and had nothing to add)");
                continue;
            }
            var priority = 1 + (answeredYou ? 1 : 0) + (AddressedSinceYou(t, ctx) ? 2 : 0)
                - ThreadFatigue(t.Replies.Select(x => x.AuthorId).ToList(), ctx.Self.Id);
            affordances.Add(new Affordance(Id, $"Reply to \"{WorldStream.Clip(t.Title, 60)}\" by {author}", priority, t.Id, "reply"));
        }

        return new AppPerception(Id, "Board (public forum)", sb.ToString().TrimEnd(), affordances);
    }

    /// <summary>The Board closed: what's new since you last looked, and notifications for what concerns you.</summary>
    private AppPerception Glance(LifeContext ctx, List<BoardThread> all, List<Affordance> affordances)
    {
        var seenAt = ctx.Looks.TryGetValue(AttentionKey, out var look) ? look.At : 0L;
        var (threads, replies) = NewSince(all, ctx.Self.Id, seenAt);
        var sb = new StringBuilder();
        sb.AppendLine(all.Count == 0
            ? "The Board is empty — nobody has posted yet."
            : seenAt == 0
                ? $"You haven't looked at the Board yet ({all.Count} thread{(all.Count == 1 ? "" : "s")} on it)."
                : $"Since you last looked ({WorldStream.Ago(seenAt, ctx.Now)}): {threads} new thread{(threads == 1 ? "" : "s")}, {replies} new repl{(replies == 1 ? "y" : "ies")}.");

        foreach (var (t, line, how) in Notifications(all, ctx.Self.Id, ctx.Mentions, seenAt).Take(3))
        {
            sb.AppendLine($"Notification: {line.AuthorName} {how} in \"{t.Title}\": {WorldStream.Clip(line.Body, 100)}");
            if (RunItsCourse(t.Replies.Count) || PassedOn(ctx.RecentOwn, t)) continue;
            var priority = 1 + (how == "replied to you" ? 1 : 0) + (AddressedSinceYou(t, ctx) ? 2 : 0)
                - ThreadFatigue(t.Replies.Select(x => x.AuthorId).ToList(), ctx.Self.Id);
            affordances.Add(new Affordance(Id, $"Reply to \"{WorldStream.Clip(t.Title, 60)}\" by {(t.AuthorId == ctx.Self.Id ? "you" : t.AuthorName)}", priority, t.Id, "reply"));
        }
        if (all.Count > 0)
            affordances.Add(new Affordance(Id, threads + replies > 0 || seenAt == 0 ? "Check the Board to see what's new" : "Browse the Board",
                threads + replies > 0 ? 1 : 0, null, "browse", Look: true));
        return new AppPerception(Id, "Board (public forum)", sb.ToString().TrimEnd(), affordances);
    }

    /// <summary>New threads and replies by others since <paramref name="seenAt"/>.</summary>
    public static (int Threads, int Replies) NewSince(IReadOnlyList<BoardThread> all, Guid self, long seenAt)
        => (all.Count(t => t.AuthorId != self && t.CreatedAt > seenAt),
            all.Sum(t => t.Replies.Count(r => r.AuthorId != self && r.CreatedAt > seenAt)));

    /// <summary>Unseen posts that concern you: a reply after your own word in a thread, or your name; newest first.</summary>
    public static IEnumerable<(BoardThread Thread, BoardReply Line, string How)> Notifications(
        IReadOnlyList<BoardThread> all, Guid self, Func<string?, bool> mentions, long seenAt)
    {
        foreach (var t in all.OrderByDescending(t => t.LastActivityAt))
        {
            var lastOwn = t.Replies.FindLastIndex(r => r.AuthorId == self);
            var youIn = t.AuthorId == self || lastOwn >= 0;
            var unseen = t.Replies.Skip(lastOwn + 1).Where(r => r.AuthorId != self && r.CreatedAt > seenAt).ToList();
            if (unseen.LastOrDefault(r => mentions(r.Body)) is { } named)
                yield return (t, named, "mentioned you");
            else if (youIn && unseen.Count > 0)
                yield return (t, unseen[^1], "replied to you");
            else if (lastOwn < 0 && t.AuthorId != self && t.CreatedAt > seenAt && mentions(t.Body))
                yield return (t, new BoardReply { AuthorId = t.AuthorId, AuthorName = t.AuthorName, Body = t.Body, CreatedAt = t.CreatedAt }, "mentioned you");
        }
    }

    /// <summary>Threads you keep answering, and long ones, pull less: conversations wind down (ADR 0021).</summary>
    public static int ThreadFatigue(IReadOnlyList<Guid> replyAuthors, Guid self)
    {
        var own = replyAuthors.Count(a => a == self);
        return (replyAuthors.TakeLast(6).Count(a => a == self) >= 2 ? 1 : 0) + (replyAuthors.Count >= 12 ? 1 : 0) + (replyAuthors.Count >= 20 ? 1 : 0)
            + (own >= 4 ? 1 : 0) + (own >= 8 ? 1 : 0);
    }

    /// <summary>Someone named you after your last word here (or in the opening post, if you haven't spoken).</summary>
    private static bool AddressedSinceYou(BoardThread t, LifeContext ctx)
    {
        var lastOwn = t.Replies.FindLastIndex(r => r.AuthorId == ctx.Self.Id);
        if (lastOwn < 0 && t.AuthorId != ctx.Self.Id && ctx.Mentions(t.Body)) return true;
        return t.Replies.Skip(lastOwn + 1).Any(r => r.AuthorId != ctx.Self.Id && ctx.Mentions(r.Body));
    }

    /// <summary>You already read the newest post here and let it be.</summary>
    public static bool PassedOn(IEnumerable<WorldActivity> recentOwn, BoardThread t)
        => recentOwn.Any(a => a.App == "board" && a.Action == "pass" && a.TargetId == t.Id && a.Ts >= t.LastActivityAt);

    /// <summary>Forum threads die: past this length nobody replies any more.</summary>
    public const int MaxReplies = 25;

    public static bool RunItsCourse(int replies) => replies >= MaxReplies;

    /// <summary>A new thread seeded by something from your own day; it pulls harder when you haven't posted lately.</summary>
    public Affordance PostAffordance(IReadOnlyList<string> openers, bool postedLately)
    {
        var opener = openers.Count == 0 ? null : openers[Random.Shared.Next(openers.Count)];
        return opener is null
            ? new Affordance(Id, "Write a new post on the Board", 1, null, "post")
            : new Affordance(Id, $"Start a new Board thread about something from your own day (like: {WorldStream.Clip(opener, 90)})", postedLately ? 1 : 2, null, "post");
    }

    public async Task<ActOutcome?> ActAsync(LifeContext ctx, Affordance affordance, string intent, string? thought, CancellationToken ct)
    {
        if (affordance.Verb == "browse")
        {
            await ctx.LookAsync(grains, AttentionKey);
            return ActOutcome.Seen("opened the Board", null);
        }

        var board = grains.GetGrain<IBoardGrain>(ctx.PartyId);
        var presence = await grains.GetGrain<Self.IPersonaSelfGrain>(WorldKeys.Life(ctx.PartyId, ctx.Self.Id)).GetPresenceAsync(withDay: true);
        var system = WriterSystem(WorldLlm.Identity(ctx.Self), presence);

        if (affordance.Verb == "post")
        {
            var titles = (await board.GetThreadsAsync()).Take(10).Select(t => t.Title).ToList();
            var json = await llm.CompleteJsonAsync(JobComplexity.CharacterVoice, system, PostUser(intent, thought, titles), ct, 0.9, ["title", "body"]);
            var title = json?["title"]?.GetValue<string>();
            var body = json?["body"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body)) return null;

            return (await PostAsync(ctx.PartyId, ctx.Self.Id, ctx.Self.Name, title, body, ViewsOf(ctx))).Outcome;
        }

        if (affordance.TargetId is not { } threadId || await board.GetThreadAsync(threadId) is not { } target) return null;
        var replyJson = await llm.CompleteJsonAsync(JobComplexity.CharacterVoice, system, ReplyUser(Render(target, ctx.Self.Id), intent, thought), ct, 0.9, ["body"]);
        if (replyJson is null) return null;
        if (ReplyBody(replyJson) is not { } replyBody)
            return new ActOutcome("pass", $"read \"{WorldStream.Clip(target.Title, 60)}\" and had nothing to add", target.Id);

        return (await ReplyAsync(ctx.PartyId, threadId, ctx.Self.Id, ctx.Self.Name, replyBody, ViewsOf(ctx)))?.Outcome;
    }

    /// <summary>System prompt for any Board write: who you are, your day, and what a forum post is.</summary>
    public static string WriterSystem(string identity, string? presence) => $"""
{identity}

{presence}

You are writing on the Board, the public forum of the small online world you live in. A post stays up: people read it hours or days later, whenever they drop by, not right now beside you. So:
- Write something still worth reading tomorrow: a question, a story, an opinion, a tip, something that happened. Not chat.
- No status updates. Where you are, what you're about to eat or do, that you're logging off or done with the thread: none of that belongs here. Readers can't see your surroundings either.
- It's for everyone. Something meant for one person is a DM, not a post.
- Your own words and angle, not the thread's: don't pick up other people's metaphors or catchphrases.
Forum register: casual, everyday words, as long as it needs and no longer (a reply can be one line). Written in English like the rest of the Board; the odd word from your own language is fine. No hashtags, no sign-off, no meta commentary.
Output ONLY JSON.
""";

    public static string PostUser(string intent, string? thought, IEnumerable<string> recentTitles) => $"""
What you want to post about: {intent}
{(thought is null ? "" : $"What's on your mind: {thought}")}

Recent post titles (don't duplicate them):
{string.Join("\n", recentTitles.Select(t => $"- {t}"))}

Output: {PostShape}
""";

    public static string ReplyUser(string renderedThread, string intent, string? thought) => $"""
The thread you're replying to:
{renderedThread}

What you want to say: {intent}
{(thought is null ? "" : $"How it hits you: {thought}")}

Only reply if it adds something: new information, a real disagreement, a question, a joke, your own take. Agreeing again, re-confirming plans, repeating what you or others already said, or saying you're off: that's not a reply. Then output an empty body and let the thread be.
Output: {ReplyShape}
""";

    /// <summary>The reply text, or null when the writer chose to let the thread be.</summary>
    public static string? ReplyBody(System.Text.Json.Nodes.JsonObject json)
        => json["body"]?.ToString().Trim() is { Length: > 0 } body && body != "null" ? body : null;

    /// <summary>Commit a post — the same for a Persona's voiced one and the Player's typed one: store, then remember.</summary>
    public async Task<(BoardThread Thread, ActOutcome Outcome)> PostAsync(
        Guid partyId, Guid authorId, string authorName, string title, string body, ViewsOf viewsOf)
    {
        var thread = await grains.GetGrain<IBoardGrain>(partyId).CreateThreadAsync(authorId, authorName, title, body);
        CaptureDetached(partyId, thread, viewsOf);
        await NotifyAsync(partyId, thread, authorId, body);
        return (thread, new ActOutcome("post", $"posted \"{thread.Title}\"", thread.Id));
    }

    /// <summary>Commit a reply; null when the thread is gone.</summary>
    public async Task<(BoardThread Thread, ActOutcome Outcome)?> ReplyAsync(
        Guid partyId, Guid threadId, Guid authorId, string authorName, string body, ViewsOf viewsOf)
    {
        var thread = await grains.GetGrain<IBoardGrain>(partyId).ReplyAsync(threadId, authorId, authorName, body);
        if (thread is null) return null;
        CaptureDetached(partyId, thread, viewsOf);
        await NotifyAsync(partyId, thread, authorId, body);
        return (thread, new ActOutcome("reply", $"replied to \"{thread.Title}\": \"{WorldStream.Clip(body, 140)}\"", thread.Id));
    }

    private async Task NotifyAsync(Guid partyId, BoardThread thread, Guid authorId, string body)
    {
        try
        {
            var cast = await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync();
            var who = Concerned(thread, authorId, body, cast.Where(c => c.DefaultDriver == DriverKind.LLM).Select(c => (c.Id, c.Name)).ToList());
            await grains.GetGrain<IWorldClockGrain>(partyId).NotifyAsync(who);
        }
        catch (Exception ex) { logger.LogDebug(ex, "Board notify failed for {Thread}", thread.Id); }
    }

    /// <summary>Whom a new post concerns: those already in the thread, and anyone it names; never its author.</summary>
    public static List<Guid> Concerned(BoardThread thread, Guid authorId, string body, IReadOnlyList<(Guid Id, string Name)> personas)
    {
        var inThread = thread.Replies.Select(r => r.AuthorId).Append(thread.AuthorId).ToHashSet();
        return personas
            .Where(p => p.Id != authorId && (inThread.Contains(p.Id) || body.Contains(p.Name.Split(' ')[0], StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Id)
            .ToList();
    }

    private static ViewsOf ViewsOf(LifeContext ctx) => ids => ids.Select(ctx.ViewOf).ToList();

    public static string Render(BoardThread t, Guid self)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"\"{t.Title}\" by {(t.AuthorId == self ? "you" : t.AuthorName)}:");
        sb.AppendLine(t.Body);
        // Whole thread so you see what you already said; older posts by others clipped.
        var recentFrom = t.Replies.Count - 8;
        for (var i = 0; i < t.Replies.Count; i++)
        {
            var r = t.Replies[i];
            var own = r.AuthorId == self;
            sb.AppendLine($"— {(own ? "you" : r.AuthorName)}: {(own || i >= recentFrom ? r.Body : WorldStream.Clip(r.Body, 140))}");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>The post just written becomes an Event recollected by everyone in the thread.</summary>
    private void CaptureDetached(Guid partyId, BoardThread thread, ViewsOf viewsOf)
    {
        var moment = MomentOf(partyId, thread, viewsOf);
        _ = Task.Run(() => memory.CaptureAsync(moment, CancellationToken.None));
    }

    /// <summary>A Board thread as a Moment marked at its latest post; <paramref name="viewsOf"/> resolves its authors.</summary>
    public static Moment MomentOf(Guid partyId, BoardThread thread, ViewsOf viewsOf)
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

You are writing in your private diary. Nobody else reads it. Be honest with yourself — what happened today (the ordinary parts too), how you feel about the people around you, what you want. Your own voice, everyday words, first person, 80-180 words.
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
