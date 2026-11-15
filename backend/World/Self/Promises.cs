using System.Globalization;
using PartyTown.Model;
using PartyTown.Services.SystemOne;
using PartyTown.World.Desires;

namespace PartyTown.World.Self;

public static class PromiseRules
{
    public const double Made = 0.6;
    public const double Settled = 0.6;
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
    public static readonly TimeSpan SameMeetup = TimeSpan.FromDays(1);

    public static readonly TimeSpan SoonSettles = TimeSpan.FromHours(4);
    public static readonly TimeSpan TomorrowSettles = TimeSpan.FromHours(14);
    public static readonly TimeSpan SetDaySettles = TimeSpan.FromDays(2);

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
        if (d.Coming) d.SettlesAt = null;
    }

    public static long? SettlesAt(Desire d)
        => d.Coming ? null : d.SettlesAt ?? (d.DueAt is { } due ? due + (long)SoonSettles.TotalMilliseconds : null);

    public static bool OffStageDue(Desire d, long now)
        => DesireRules.IsPromise(d) && d.Status == DesireStatus.Active && d.TargetId is not null && !DesireRules.Lapsed(d, now)
            && SettlesAt(d) is { } at && now >= at;

    public static Desire? Open(IEnumerable<Desire> desires, Guid holder, bool coming, long now)
        => desires.FirstOrDefault(d => DesireRules.IsPromise(d) && d.Status == DesireStatus.Active && d.TargetId == holder && d.Coming == coming && !DesireRules.Lapsed(d, now));

    public static bool Restate(IEnumerable<Desire> desires, Desire incoming, DateTimeOffset now)
    {
        if (incoming.TargetId is not { } holder || Open(desires, holder, incoming.Coming, now.ToUnixTimeMilliseconds()) is not { } same) return false;
        same.Again++;
        same.TouchedAt = now.ToUnixTimeMilliseconds();
        return true;
    }

    public static bool LetDown(Desire d, long now) => DesireRules.IsPromise(d) && d.DueAt is not null && DesireRules.Lapsed(d, now);

    public static bool Arrived(Places.Body? me, Places.Body? them)
        => me is { IsWalking: false } && them is { IsWalking: false } && me.PlaceId == them.PlaceId;

    public static Guid? Addressee(IReadOnlyList<ChatMessage> recent, Guid speaker)
    {
        for (var i = recent.Count - 2; i >= 0; i--)
        {
            var m = recent[i];
            if (m.SenderId != speaker && m.SenderId != Narrator.PersonaId && !string.IsNullOrWhiteSpace(m.Content)) return m.SenderId;
        }
        return null;
    }

    public static IReadOnlyList<DateTime> Days(DateTimeOffset now) => Enumerable.Range(1, 7).Select(i => now.Date.AddDays(i)).ToList();

    private static string DayKey(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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

    public static string Top(SystemOneAnswers answers, string key, string fallback) => TopShare(answers, key, fallback).Key;

    public static (string Key, double Share) TopShare(SystemOneAnswers answers, string key, string fallback)
    {
        if (!answers.Choices.TryGetValue(key, out var dist) || dist.Count == 0) return (fallback, 0);
        var top = dist.MaxBy(kv => kv.Value);
        return (top.Key, top.Value);
    }

    public static (long StartsAt, string Where)? Meetup((string Key, double Share) day, (string Key, double Share) part, (string Key, double Share) where, DateTimeOffset now)
    {
        if (day.Share < Sure || where.Share < Sure || where.Key == Elsewhere) return null;
        if (!DateTime.TryParseExact(day.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
        var hour = (part.Share >= Sure ? part.Key : Evening) switch { Morning => 10, Afternoon => 14, _ => 19 };
        return (new DateTimeOffset(date.AddHours(hour), now.Offset).ToUnixTimeMilliseconds(), where.Key);
    }

    public static Places.Place? HomeOf(IEnumerable<Places.Place> places, Guid who)
        => places.FirstOrDefault(p => p.Kind == Places.PlaceKinds.Home && Places.Homes.LivesIn(p, who));

    public static Gathering? Already(IEnumerable<Gathering> gatherings, Guid a, Guid b, long startsAt)
        => gatherings.FirstOrDefault(g => g.Status == GatheringStatus.Planned && g.AttendeeIds.Contains(a) && g.AttendeeIds.Contains(b)
            && Math.Abs(g.StartsAt - startsAt) <= SameMeetup.TotalMilliseconds);

    public static string Title(string guest, string host) => $"{guest.Split(' ')[0]} over at {host.Split(' ')[0]}'s";

    public static IEnumerable<Work.Owed> Owed(IEnumerable<Desire> desires, Func<Guid, string> nameOf, DateTimeOffset now)
    {
        var ms = now.ToUnixTimeMilliseconds();
        foreach (var d in desires.Where(d => DesireRules.IsPromise(d) && d.Status == DesireStatus.Active && !DesireRules.Lapsed(d, ms) && d.TargetId is not null))
            yield return new Work.Owed($"You told {nameOf(d.TargetId!.Value)} {d.Text}, {WorldStream.Ago(d.Since, now)}{When(d, ms)}{Again(d)}; not done yet.",
                d.DueAt ?? d.Until ?? ms, d.TargetId);
    }

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
