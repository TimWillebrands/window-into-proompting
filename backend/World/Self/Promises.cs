using System.Globalization;
using PartyTown.Model;
using PartyTown.Services.SystemOne;
using PartyTown.World.Desires;

namespace PartyTown.World.Self;

/// <summary>Pure rules for promises (ADR 0043): a promise is the promiser's Desire, a set-day meet-up a Gathering.</summary>
public static class PromiseRules
{
    /// <summary>p(said they'd do something) at which it counts as a promise.</summary>
    public const double Made = 0.6;
    /// <summary>p(did it, or called it off) at which an open promise is settled.</summary>
    public const double Settled = 0.6;
    /// <summary>Choice share a set-day meet-up's day and Home need before it goes on the Calendar.</summary>
    public const double Sure = 0.5;

    public const string Soon = "soon";
    public const string Tomorrow = "tomorrow";
    public const string SetDay = "set";
    public const string Come = "come";
    public const string Do = "do";

    public const string Morning = "morning";
    public const string Afternoon = "afternoon";
    public const string Evening = "evening";
    public const string TheirHome = "speaker";
    public const string OtherHome = "other";
    public const string Elsewhere = "elsewhere";

    public static readonly TimeSpan SoonLasts = TimeSpan.FromHours(24);
    public static readonly TimeSpan TomorrowLasts = TimeSpan.FromHours(36);
    public static readonly TimeSpan SetDayLasts = TimeSpan.FromDays(8);
    /// <summary>A Gathering this close to the agreed start is the same plan.</summary>
    public static readonly TimeSpan SameMeetup = TimeSpan.FromDays(1);

    /// <summary>A "soon" promise to do something is settled off-screen this long after it was made (ADR 0055).</summary>
    public static readonly TimeSpan SoonSettles = TimeSpan.FromHours(4);
    /// <summary>A "tomorrow" one this long after their midnight: early afternoon.</summary>
    public static readonly TimeSpan TomorrowSettles = TimeSpan.FromHours(14);
    public static readonly TimeSpan SetDaySettles = TimeSpan.FromDays(2);

    /// <summary>The promiser's Desire; <paramref name="now"/> in their local offset, so "tomorrow" starts at their midnight.</summary>
    public static Desire Make(Guid holder, bool coming, string said, string? asked, string when, DateTimeOffset now)
    {
        var ms = now.ToUnixTimeMilliseconds();
        var text = asked is null ? $"\"{WorldStream.Clip(said, 140)}\"" : $"\"{WorldStream.Clip(said, 140)}\", answering \"{WorldStream.Clip(asked, 100)}\"";
        var d = new Desire { Kind = DesireKind.Being, Source = DesireSource.Promise, Text = text, TargetId = holder, Coming = coming, Since = ms, TouchedAt = ms };
        Schedule(d, when, now);
        return d;
    }

    private static void Schedule(Desire d, string when, DateTimeOffset now)
    {
        var ms = now.ToUnixTimeMilliseconds();
        switch (when)
        {
            case Tomorrow:
                var midnight = new DateTimeOffset(now.Date.AddDays(1), now.Offset).ToUnixTimeMilliseconds();
                d.DueAt = midnight;
                d.Until = midnight + (long)TomorrowLasts.TotalMilliseconds;
                d.SettlesAt = midnight + (long)TomorrowSettles.TotalMilliseconds;
                break;
            case SetDay:
                d.DueAt = null;
                d.Until = ms + (long)SetDayLasts.TotalMilliseconds;
                d.SettlesAt = ms + (long)SetDaySettles.TotalMilliseconds;
                break;
            default:
                d.DueAt = ms;
                d.Until = ms + (long)SoonLasts.TotalMilliseconds;
                d.SettlesAt = ms + (long)SoonSettles.TotalMilliseconds;
                break;
        }
        // Being there keeps a come-over; nobody settles it for them.
        if (d.Coming) d.SettlesAt = null;
    }

    /// <summary>When an open promise to do something gets settled off-screen; promises made before ADR 0055 settle a while after they came due.</summary>
    public static long? SettlesAt(Desire d)
        => d.Coming ? null : d.SettlesAt ?? (d.DueAt is { } due ? due + (long)SoonSettles.TotalMilliseconds : null);

    /// <summary>Open, not a come-over, and its time to be settled off-screen has come (ADR 0055).</summary>
    public static bool OffStageDue(Desire d, long now)
        => DesireRules.IsPromise(d) && d.Status == DesireStatus.Active && d.TargetId is not null && !DesireRules.Lapsed(d, now)
            && SettlesAt(d) is { } at && now >= at;

    /// <summary>The open promise to that person of that kind, if any.</summary>
    public static Desire? Open(IEnumerable<Desire> desires, Guid holder, bool coming, long now)
        => desires.FirstOrDefault(d => DesireRules.IsPromise(d) && d.Status == DesireStatus.Active && d.TargetId == holder && d.Coming == coming && !DesireRules.Lapsed(d, now));

    /// <summary>Saying it again restates the open one, keeping the first words and the first deadline (ADR 0055); true when it did.</summary>
    public static bool Restate(IEnumerable<Desire> desires, Desire incoming, DateTimeOffset now)
    {
        if (incoming.TargetId is not { } holder || Open(desires, holder, incoming.Coming, now.ToUnixTimeMilliseconds()) is not { } same) return false;
        same.Again++;
        same.TouchedAt = now.ToUnixTimeMilliseconds();
        return true;
    }

    /// <summary>A due promise left to lapse: whoever was owed it cools on the promiser.</summary>
    public static bool LetDown(Desire d, long now) => DesireRules.IsPromise(d) && d.DueAt is not null && DesireRules.Lapsed(d, now);

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

    /// <summary>The next seven days by date key, for a set-day meet-up.</summary>
    public static IReadOnlyList<DateTime> Days(DateTimeOffset now) => Enumerable.Range(1, 7).Select(i => now.Date.AddDays(i)).ToList();

    private static string DayKey(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>One System One read of the last line: a promise, what, when, a set-day meet-up's day and Home, and each open one kept or off.</summary>
    public static Dictionary<string, SystemOneQuestion> Questions(string speaker, string other, IReadOnlyList<Desire> open, DateTimeOffset now)
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
            ["day"] = new ChoiceQuestion($"If they agreed to meet on a set day, which day?",
                Days(now).ToDictionary(DayKey, d => (string?)d.ToString("dddd d MMMM", CultureInfo.InvariantCulture))),
            ["part"] = new ChoiceQuestion("And when in that day?", new Dictionary<string, string?>
            {
                [Morning] = "morning", [Afternoon] = "afternoon", [Evening] = "evening or for dinner",
            }),
            ["where"] = new ChoiceQuestion($"Where would they meet?", new Dictionary<string, string?>
            {
                [TheirHome] = $"at {speaker}'s home", [OtherHome] = $"at {other}'s home", [Elsewhere] = "somewhere else, or not said",
            }),
        };
        for (var i = 0; i < open.Count; i++)
        {
            questions[$"kept{i}"] = new NoulQuestion($"In their last message, {speaker} actually does what they earlier told {other} they would ({WorldStream.Clip(open[i].Text, 180)}).");
            questions[$"off{i}"] = new NoulQuestion($"In their last message, {speaker} plainly tells {other} they won't do what they earlier said they would ({WorldStream.Clip(open[i].Text, 180)}).");
        }
        return questions;
    }

    /// <summary>The likeliest option of a classifying choice: reading what was said, not choosing what to do.</summary>
    public static string Top(SystemOneAnswers answers, string key, string fallback) => TopShare(answers, key, fallback).Key;

    /// <summary>The likeliest option with its share; the fallback at 0 when unanswered.</summary>
    public static (string Key, double Share) TopShare(SystemOneAnswers answers, string key, string fallback)
    {
        if (!answers.Choices.TryGetValue(key, out var dist) || dist.Count == 0) return (fallback, 0);
        var top = dist.MaxBy(kv => kv.Value);
        return (top.Key, top.Value);
    }

    /// <summary>When and at whose Home a set-day meet-up is, or null when the read isn't sure enough to put it on the Calendar.</summary>
    public static (long StartsAt, string Where)? Meetup((string Key, double Share) day, (string Key, double Share) part, (string Key, double Share) where, DateTimeOffset now)
    {
        if (day.Share < Sure || where.Share < Sure || where.Key == Elsewhere) return null;
        if (!DateTime.TryParseExact(day.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
        var hour = (part.Share >= Sure ? part.Key : Evening) switch { Morning => 10, Afternoon => 14, _ => 19 };
        return (new DateTimeOffset(date.AddHours(hour), now.Offset).ToUnixTimeMilliseconds(), where.Key);
    }

    /// <summary>Someone's Home: one they own and still live in, or one they've moved into.</summary>
    public static Places.Place? HomeOf(IEnumerable<Places.Place> places, Guid who)
        => places.FirstOrDefault(p => p.Kind == Places.PlaceKinds.Home && Places.Homes.LivesIn(p, who));

    /// <summary>A planned Gathering both already go to, near enough that it's this plan.</summary>
    public static Gathering? Already(IEnumerable<Gathering> gatherings, Guid a, Guid b, long startsAt)
        => gatherings.FirstOrDefault(g => g.Status == GatheringStatus.Planned && g.AttendeeIds.Contains(a) && g.AttendeeIds.Contains(b)
            && Math.Abs(g.StartsAt - startsAt) <= SameMeetup.TotalMilliseconds);

    /// <summary>"Denise over at Anne's": the plan as the Calendar shows it.</summary>
    public static string Title(string guest, string host) => $"{guest.Split(' ')[0]} over at {host.Split(' ')[0]}'s";

    /// <summary>Their own open promises as "# What you owe" lines.</summary>
    public static IEnumerable<Work.Owed> Owed(IEnumerable<Desire> desires, Func<Guid, string> nameOf, DateTimeOffset now)
    {
        var ms = now.ToUnixTimeMilliseconds();
        foreach (var d in desires.Where(d => DesireRules.IsPromise(d) && d.Status == DesireStatus.Active && !DesireRules.Lapsed(d, ms) && d.TargetId is not null))
            yield return new Work.Owed($"You told {nameOf(d.TargetId!.Value)} {d.Text}, {WorldStream.Ago(d.Since, now)}{When(d, ms)}{Again(d)}; not done yet.",
                d.DueAt ?? d.Until ?? ms, d.TargetId);
    }

    /// <summary>What someone told this persona they'd do, read off the promiser's own Desires.</summary>
    public static IEnumerable<string> Heard(IEnumerable<Desire> theirs, Guid holder, string first, DateTimeOffset now)
    {
        var ms = now.ToUnixTimeMilliseconds();
        return theirs.Where(d => DesireRules.IsPromise(d) && d.Status == DesireStatus.Active && d.TargetId == holder && !DesireRules.Lapsed(d, ms))
            .Select(d => $"{first} told you {d.Text} {WorldStream.Ago(d.Since, now)}{Again(d)}");
    }

    private static string When(Desire d, long now)
        => d.DueAt is null ? " (for a set day later)" : d.DueAt > now ? " (for tomorrow)" : string.Empty;

    private static string Again(Desire d) => d.Again switch
    {
        0 => string.Empty,
        1 => "; said again once since",
        _ => $"; said again {d.Again} times since",
    };
}
