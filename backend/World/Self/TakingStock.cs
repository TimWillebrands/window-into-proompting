using System.Text.RegularExpressions;
using PartyTown.Services.SystemOne;

namespace PartyTown.World.Self;

public static class Grip
{
    public const string Key = "grip";

    public static SystemOneQuestion Question(string name)
        => new NoulQuestion($"{name} keeps doing the same thing and it isn't getting them anywhere.");

    public static double FromLoss(double stuck) => 1 - Math.Clamp(stuck, 0, 1);
}

public static partial class TakingStock
{
    public const string Action = "stock";
    public const string Summary = "stopped to think things over";
    public const int MinLived = 3;
    public static readonly TimeSpan Window = TimeSpan.FromHours(3);
    public static readonly TimeSpan LongestWindow = TimeSpan.FromHours(12);
    public static readonly TimeSpan NotesFor = TimeSpan.FromHours(2);
    public static readonly TimeSpan UpshotFor = TimeSpan.FromHours(8);
    public static readonly TimeSpan Day = TimeSpan.FromHours(24);
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);
    public const int MaxDeeds = 15;
    public const int NotesMax = 1500;
    public const int UpshotMax = 240;
    public const int ThoughtMax = 300;
    public const int SentenceFrom = 20;
    public const double Temperature = 0.7;
    public const double Logged = 0.05;
    public const double SameAt = 0.6;
    private const string So = "So:";

    public sealed record Stock(string? Notes, string? Upshot)
    {
        public bool Nothing => Notes is null;
    }

    public static TimeSpan WindowAfter(int fruitless)
        => TimeSpan.FromMilliseconds(Math.Min(Window.TotalMilliseconds * Math.Pow(2, Math.Max(0, fruitless)), LongestWindow.TotalMilliseconds));

    public static double Ripe(TimeSpan since, int fruitless) => Math.Clamp(since / WindowAfter(fruitless), 0, 1);

    public static double Chance(double grip, double ripe)
    {
        var loss = 1 - Math.Clamp(grip, 0, 1);
        return loss * loss * ripe * ripe;
    }

    public const int Pressing = 3;

    public static bool May(bool lookedAlready, string? nudge, string? distress, double ripe, IEnumerable<Apps.Affordance> seated)
        => !lookedAlready && string.IsNullOrWhiteSpace(nudge) && distress is null && ripe > 0 && !seated.Any(a => a.Priority >= Pressing && a.Meets is null);

    public static bool Counts(WorldActivity a)
        => a.Action is not ("fizzle" or "error" or Desires.Lack.Action or OwnHappenings.Action or Action);

    public static long From(long stockAt, DateTimeOffset now)
        => stockAt > 0 ? stockAt : (now - Day).ToUnixTimeMilliseconds();

    public static List<WorldActivity> Since(IEnumerable<WorldActivity> own, long stockAt, DateTimeOffset now)
    {
        var from = From(stockAt, now);
        return own.Where(a => a.Ts > from && Counts(a)).OrderBy(a => a.Ts).ToList();
    }

    public static int Lived(IEnumerable<WorldActivity> deeds) => Apps.DiaryApp.Lived(deeds, null);

    public static TimeSpan SinceLast(IReadOnlyList<WorldActivity> deeds, long stockAt, DateTimeOffset now)
    {
        var at = stockAt > 0 ? stockAt : deeds.Count == 0 ? now.ToUnixTimeMilliseconds() : deeds.Min(a => a.Ts);
        return TimeSpan.FromMilliseconds(Math.Max(0, now.ToUnixTimeMilliseconds() - at));
    }

    public static double Ripeness(SelfState? self, IReadOnlyList<WorldActivity> deeds, DateTimeOffset now)
        => self is null || Lived(deeds) < MinLived ? 0 : Ripe(SinceLast(deeds, self.StockAt, now), self.StockFruitless);

    public static string Brief(string identity) => $"""
{identity}

You've stopped for a moment, and nobody is waiting on you. Before you do anything else, look back over what you've been doing lately and what came of it: all of it together, not one thing at a time. Think it through for yourself, in your own words. What did it add up to? What do you know now that you didn't before? Is the way you've been going about things getting you anywhere, and if it isn't, what would? These are notes to yourself and nobody else reads them. Keep them to a few short paragraphs. Most of the time, looking back shows things are going the way they should, and then there is nothing to change. End with one last line that starts with "So:". If the way you've been going about something isn't getting you anywhere, that line says in one plain sentence what you see now and what you'll stop doing: the kind of thing it is, not only the last case of it. Don't plan what comes instead and don't name a time: that comes when you get to it. Otherwise that last line is "So: nothing".
""";

    public static string Deeds(IEnumerable<WorldActivity> deeds, DateTimeOffset now)
    {
        var lines = deeds.Where(Counts).OrderBy(a => a.Ts).TakeLast(MaxDeeds).Select(a =>
        {
            var thought = SecondThoughts.Is(a) || string.IsNullOrWhiteSpace(a.Thought) ? string.Empty : $" (you were thinking: {WorldStream.Clip(a.Thought.Trim(), ThoughtMax)})";
            return $"- {WorldStream.Ago(a.Ts, now)}: {a.Action} {a.Summary}{thought}";
        }).ToList();
        return $"# What you've been doing, oldest first\n{(lines.Count == 0 ? "(nothing yet)" : string.Join("\n", lines))}";
    }

    public static string Offer(IEnumerable<string> options, string rest)
        => $"# What you could do from here\n{string.Join("\n", options.Append(rest).Select(o => $"- {o}"))}";

    public static string Material(string world, IEnumerable<WorldActivity> deeds, IEnumerable<string> options, string rest, DateTimeOffset now)
        => $"{world}\n{Deeds(deeds, now)}\n\n{Offer(options, rest)}";

    public static Stock Read(string? text)
    {
        var all = text?.Trim() ?? string.Empty;
        if (Plain(all) is "" or "none" or "nothing") return new Stock(null, null);
        var said = all.Split('\n').Select(l => l.Trim().TrimStart('*', '_', '>', '#', '-', ' ').Trim())
            .LastOrDefault(l => l.StartsWith(So, StringComparison.OrdinalIgnoreCase));
        var upshot = said?[So.Length..].Trim().Trim('*', '_', '"', ' ').Trim();
        if (string.IsNullOrEmpty(upshot)) upshot = null;
        if (upshot is not null && !Unchanged(upshot)) upshot = Sentence(upshot);
        if (upshot is not null && Unchanged(upshot)) return new Stock(null, null);
        return new Stock(Keep(all), upshot is null ? null : WorldStream.Clip(upshot, UpshotMax));
    }

    public static string Sentence(string upshot)
    {
        for (var i = SentenceFrom; i < upshot.Length - 1; i++)
            if (upshot[i] is '.' or '!' or '?' && char.IsWhiteSpace(upshot[i + 1])) return upshot[..(i + 1)];
        return upshot;
    }

    private static string Keep(string notes)
    {
        var t = notes.ReplaceLineEndings("\n").Trim();
        return t.Length <= NotesMax ? t : t[..NotesMax].TrimEnd() + "…";
    }

    private static string Plain(string s) => s.Trim().Trim('*', '_', '"', ' ').TrimEnd('.', '!').Trim().ToLowerInvariant();

    private static bool Unchanged(string upshot)
        => Plain(upshot) is "nothing" or "none" || Plain(upshot).StartsWith("nothing needs to change", StringComparison.Ordinal)
            || Plain(upshot).StartsWith("nothing has to change", StringComparison.Ordinal);

    public static void Record(SelfState s, string? notes, string? upshot, long now)
    {
        s.StockAt = now;
        if (notes is null)
        {
            s.StockFruitless++;
            return;
        }
        s.StockFruitless = 0;
        s.StockNotes = Keep(notes);
        s.Upshot = upshot is null ? null : WorldStream.Clip(upshot, UpshotMax);
        s.StockNotesAt = now;
    }

    public static string? Standing(SelfState? s, DateTimeOffset now)
        => s is null || string.IsNullOrWhiteSpace(s.Upshot) || now.ToUnixTimeMilliseconds() - s.StockNotesAt >= UpshotFor.TotalMilliseconds ? null : s.Upshot;

    public static string? Line(SelfState s, DateTimeOffset now)
        => Standing(s, now) is { } upshot
            ? $"What you came to when you last stopped to think it over ({WorldStream.Ago(s.StockNotesAt, now)}): {upshot}"
            : null;

    public static string? Notes(SelfState? s, IEnumerable<WorldActivity> own, DateTimeOffset now)
        => s is null || string.IsNullOrWhiteSpace(s.StockNotes) || now.ToUnixTimeMilliseconds() - s.StockNotesAt >= NotesFor.TotalMilliseconds
            || Lived(own.Where(a => a.Ts > s.StockNotesAt && Counts(a))) > 0
            ? null
            : $"# What you worked out when you stopped to think, {WorldStream.Ago(s.StockNotesAt, now)}\n{s.StockNotes.Trim()}";

    public static double Overlap(string a, string b)
    {
        var x = WordsOf(a);
        var y = WordsOf(b);
        if (x.Count == 0 || y.Count == 0) return 0;
        return (double)x.Intersect(y).Count() / Math.Min(x.Count, y.Count);
    }

    public static bool Same(string? standing, string? upshot)
        => standing is not null && upshot is not null && Overlap(standing, upshot) >= SameAt;

    public static Stock Against(Stock stock, SelfState? s, DateTimeOffset now)
        => Same(Standing(s, now), stock.Upshot) ? new Stock(null, null) : stock;

    private static HashSet<string> WordsOf(string s) => Word().Matches(s.ToLowerInvariant()).Select(m => m.Value).ToHashSet();

    [GeneratedRegex(@"[\p{L}\p{N}']+")]
    private static partial Regex Word();

    public static string Remembered(string upshot) => $"[Thinking it over, to yourself] {upshot}";
}
