using PartyTown.Model;
using PartyTown.Services.SystemOne;

namespace PartyTown.World.Self;

/// <summary>Something one of two people said they'd do for the other (ADR 0043); kept on both their Ties.</summary>
[GenerateSerializer, Alias("PartyTown.World.Self.Promise")]
public sealed class Promise
{
    /// <summary>This persona made it; false means it was made to them.</summary>
    [Id(0)] public bool Mine { get; set; }
    /// <summary>To come over to where the other is; being there keeps it.</summary>
    [Id(1)] public bool Coming { get; set; }
    /// <summary>The line that made it.</summary>
    [Id(2)] public string Said { get; set; } = string.Empty;
    /// <summary>What the other had just said, if anything: often the real content ("what was my father working on?").</summary>
    [Id(3)] public string? Asked { get; set; }
    [Id(4)] public long MadeAt { get; set; }
    /// <summary>Last time it was said again.</summary>
    [Id(5)] public long LastAt { get; set; }
    /// <summary>Pressing from then; null for a set later day, which is known but doesn't press.</summary>
    [Id(6)] public long? DueFrom { get; set; }
    [Id(7)] public long LapseAt { get; set; }
    /// <summary>Times it was said again since: a restated plan, or a put-off.</summary>
    [Id(8)] public int Again { get; set; }
}

/// <summary>A promise with who it's between, as one persona holds it.</summary>
[GenerateSerializer, Alias("PartyTown.World.Self.PromiseView")]
public sealed record PromiseView([property: Id(0)] Guid OtherId, [property: Id(1)] Promise Promise);

/// <summary>Pure rules for promises: when they press, when they lapse, how they read (ADR 0043).</summary>
public static class PromiseRules
{
    /// <summary>p(said they'd do something) at which it counts as a promise.</summary>
    public const double Made = 0.6;
    /// <summary>p(did it, or called it off) at which an open promise is settled.</summary>
    public const double Settled = 0.6;

    public const string Soon = "soon";
    public const string Tomorrow = "tomorrow";
    public const string SetDay = "set";
    public const string Come = "come";
    public const string Do = "do";

    public static readonly TimeSpan SoonLasts = TimeSpan.FromHours(24);
    public static readonly TimeSpan TomorrowLasts = TimeSpan.FromHours(36);
    public static readonly TimeSpan SetDayLasts = TimeSpan.FromDays(8);

    /// <summary>A fresh promise; <paramref name="now"/> in the persona's local offset, so "tomorrow" starts at their midnight.</summary>
    public static Promise Make(bool mine, bool coming, string said, string? asked, string when, DateTimeOffset now)
    {
        var ms = now.ToUnixTimeMilliseconds();
        var p = new Promise { Mine = mine, Coming = coming, Said = WorldStream.Clip(said, 200), Asked = asked is null ? null : WorldStream.Clip(asked, 160), MadeAt = ms, LastAt = ms };
        Schedule(p, when, now);
        return p;
    }

    private static void Schedule(Promise p, string when, DateTimeOffset now)
    {
        var ms = now.ToUnixTimeMilliseconds();
        switch (when)
        {
            case Tomorrow:
                var midnight = new DateTimeOffset(now.Date.AddDays(1), now.Offset).ToUnixTimeMilliseconds();
                p.DueFrom = midnight;
                p.LapseAt = midnight + (long)TomorrowLasts.TotalMilliseconds;
                break;
            case SetDay:
                p.DueFrom = null;
                p.LapseAt = ms + (long)SetDayLasts.TotalMilliseconds;
                break;
            default:
                p.DueFrom = ms;
                p.LapseAt = ms + (long)SoonLasts.TotalMilliseconds;
                break;
        }
    }

    /// <summary>One open promise per direction and kind between two people: saying it again restates it, keeping the first words.</summary>
    /// <returns>True when it restated one already open.</returns>
    public static bool Merge(List<Promise> open, Promise incoming, string when, DateTimeOffset now)
    {
        var same = open.FirstOrDefault(p => p.Mine == incoming.Mine && p.Coming == incoming.Coming && !Lapsed(p, now.ToUnixTimeMilliseconds()));
        if (same is null)
        {
            open.RemoveAll(p => p.Mine == incoming.Mine && p.Coming == incoming.Coming);
            open.Add(incoming);
            return false;
        }
        same.Again++;
        same.LastAt = now.ToUnixTimeMilliseconds();
        Schedule(same, when, now);
        return true;
    }

    public static bool Lapsed(Promise p, long now) => now >= p.LapseAt;

    /// <summary>Theirs to keep, and due now: it pulls on what they do next.</summary>
    public static bool Pressing(Promise p, long now) => p.Mine && p.DueFrom is { } due && now >= due && !Lapsed(p, now);

    /// <summary>A come-over is kept by being there.</summary>
    public static bool Arrived(Places.Body? me, Places.Body? them)
        => me is { IsWalking: false } && them is { IsWalking: false } && me.PlaceId == them.PlaceId;

    /// <summary>Who a line in a Room answers: the last other speaker just before it, or null.</summary>
    public static Guid? Addressee(IReadOnlyList<ChatMessage> recent, Guid speaker)
    {
        for (var i = recent.Count - 2; i >= 0; i--)
        {
            var m = recent[i];
            if (m.SenderId != speaker && m.SenderId != Narrator.PersonaId && !string.IsNullOrWhiteSpace(m.Content)) return m.SenderId;
        }
        return null;
    }

    /// <summary>The question for System One: did the last line promise something, what, when, and does it settle one already open.</summary>
    public static Dictionary<string, SystemOneQuestion> Questions(string speaker, string other, IReadOnlyList<Promise> open)
    {
        var questions = new Dictionary<string, SystemOneQuestion>
        {
            ["promise"] = new NoulQuestion($"In their last message, {speaker} says they will do something for {other} later that isn't done yet: come over, answer or get back to them, tell them something, send, check or fix something, or meet up."),
            ["kind"] = new ChoiceQuestion($"What did {speaker} say they'd do for {other}?", new Dictionary<string, string?>
            {
                [Come] = $"come over to where {other} is, or meet up somewhere",
                [Do] = "something else: answer, get back to them, tell, send, check or fix something",
            }),
            ["when"] = new ChoiceQuestion($"When did {speaker} say they'd do it?", new Dictionary<string, string?>
            {
                [Soon] = "now, later today, or no time given",
                [Tomorrow] = "tomorrow",
                [SetDay] = "on a set later day or time, like a weekday or next week",
            }),
        };
        for (var i = 0; i < open.Count; i++)
            questions[$"kept{i}"] = new NoulQuestion($"In their last message, {speaker} actually does what they earlier told {other} they would ({Gist(open[i])}), or plainly tells {other} they won't after all.");
        return questions;
    }

    /// <summary>The likeliest option of a classifying choice: reading what was said, not choosing what to do.</summary>
    public static string Top(SystemOneAnswers answers, string key, string fallback)
        => answers.Choices.TryGetValue(key, out var dist) && dist.Count > 0 ? dist.MaxBy(kv => kv.Value).Key : fallback;

    public static string Gist(Promise p)
        => p.Asked is { } asked ? $"answering \"{WorldStream.Clip(asked, 100)}\": \"{WorldStream.Clip(p.Said, 120)}\"" : $"\"{WorldStream.Clip(p.Said, 140)}\"";

    /// <summary>Their own open promises as "# What you owe" lines: pressing first, then by when.</summary>
    public static IEnumerable<Work.Owed> Owed(IEnumerable<PromiseView> promises, Func<Guid, string> nameOf, DateTimeOffset now)
    {
        var ms = now.ToUnixTimeMilliseconds();
        foreach (var (other, p) in promises.Where(v => v.Promise.Mine && !Lapsed(v.Promise, ms)).Select(v => (v.OtherId, v.Promise)))
        {
            var after = p.Asked is { } asked ? $" (when they said \"{WorldStream.Clip(asked, 100)}\")" : string.Empty;
            yield return new Work.Owed(
                $"You told {nameOf(other)} \"{WorldStream.Clip(p.Said, 140)}\"{after}, {WorldStream.Ago(p.MadeAt, now)}{When(p, ms)}{Again(p)}; not done yet.",
                p.DueFrom ?? p.LapseAt, other);
        }
    }

    /// <summary>What someone told this persona they'd do, for their line in "Who these people are to you".</summary>
    public static IEnumerable<string> Heard(IEnumerable<Promise> promises, string first, DateTimeOffset now)
    {
        var ms = now.ToUnixTimeMilliseconds();
        return promises.Where(p => !p.Mine && !Lapsed(p, ms))
            .Select(p => $"{first} told you \"{WorldStream.Clip(p.Said, 120)}\" {WorldStream.Ago(p.MadeAt, now)}{Again(p)}");
    }

    /// <summary>Options aimed at whoever they owe, by person, their chat and (for a come-over) where they are now.</summary>
    public static Dictionary<Guid, string> Handles(IEnumerable<PromiseView> promises, Func<Guid, Guid?> chatWith, Func<Guid, Guid?> placeOf, long now)
    {
        var handles = new Dictionary<Guid, string>();
        foreach (var (other, p) in promises.Where(v => Pressing(v.Promise, now)).Select(v => (v.OtherId, v.Promise)))
        {
            var said = p.Said;
            handles.TryAdd(other, said);
            if (chatWith(other) is { } chat) handles.TryAdd(chat, said);
            if (p.Coming && placeOf(other) is { } place) handles.TryAdd(place, said);
        }
        return handles;
    }

    private static string When(Promise p, long now)
        => p.DueFrom is null ? " (for a set day later)" : p.DueFrom > now ? " (for tomorrow)" : string.Empty;

    private static string Again(Promise p) => p.Again switch
    {
        0 => string.Empty,
        1 => "; said again once since",
        _ => $"; said again {p.Again} times since",
    };
}
