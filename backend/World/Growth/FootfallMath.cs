namespace PartyTown.World.Growth;

/// <summary>One bit of pressure on a Locale (ADR 0027): an anchor, a Day stretch spent there, or a mention in talk.</summary>
[GenerateSerializer, Alias("PartyTown.World.Growth.FootfallSignal")]
public sealed class FootfallSignal
{
    [Id(0)] public Guid LocaleId { get; set; }
    [Id(1)] public Guid ParticipantId { get; set; }
    /// <summary><see cref="FootfallSources"/>.</summary>
    [Id(2)] public string Source { get; set; } = FootfallSources.Talk;
    [Id(3)] public double Weight { get; set; }
    [Id(4)] public long At { get; set; }
    /// <summary>Local day, "yyyy-MM-dd".</summary>
    [Id(5)] public string Day { get; set; } = string.Empty;
}

public static class FootfallSources
{
    public const string Anchor = "anchor";
    public const string Day = "day";
    public const string Talk = "talk";

    public static double WeightOf(string source) => source switch
    {
        Anchor => 0.5,
        Day => 0.5,
        _ => 0.2,
    };
}

/// <summary>Where a Locale stands against the three gates; <see cref="Missing"/> null means it would open.</summary>
[GenerateSerializer, Alias("PartyTown.World.Growth.FootfallReading")]
public sealed record FootfallReading(
    [property: Id(0)] Guid LocaleId,
    [property: Id(1)] double Footfall,
    [property: Id(2)] int People,
    [property: Id(3)] int Days,
    [property: Id(4)] string? Missing,
    [property: Id(5)] List<Guid> Contributors);

/// <summary>Pure footfall rules (ADR 0027): decay, the per-person cap, and the three gates. Nothing ticks.</summary>
public static class FootfallMath
{
    public static readonly TimeSpan HalfLife = TimeSpan.FromDays(3);
    public static readonly TimeSpan Kept = TimeSpan.FromDays(15);
    public const double PerParticipantCap = 1.5;
    public const double OpenAt = 3.0;
    public const int MinPeople = 2;
    public const int MinDays = 2;
    private const double Faded = 0.05;

    public static double Decayed(FootfallSignal s, long now)
        => s.Weight * Math.Pow(0.5, Math.Max(0, now - s.At) / HalfLife.TotalMilliseconds);

    public static List<FootfallReading> Read(IEnumerable<FootfallSignal> signals, long now)
        => signals.GroupBy(s => s.LocaleId).Select(g => ReadOne(g.Key, g.ToList(), now))
            .OrderByDescending(r => r.Footfall).ToList();

    private static FootfallReading ReadOne(Guid localeId, List<FootfallSignal> signals, long now)
    {
        var live = signals.Where(s => Decayed(s, now) > Faded).ToList();
        var perPerson = live.GroupBy(s => s.ParticipantId)
            .ToDictionary(g => g.Key, g => Math.Min(PerParticipantCap, g.Sum(s => Decayed(s, now))));
        var footfall = Math.Round(perPerson.Values.Sum(), 2);
        var people = perPerson.Count;
        var days = live.Select(s => s.Day).Distinct().Count();
        var missing = people < MinPeople ? $"needs {MinPeople - people} more person{(MinPeople - people == 1 ? "" : "s")}"
            : days < MinDays ? $"needs {MinDays - days} more day{(MinDays - days == 1 ? "" : "s")}"
            : footfall < OpenAt ? $"needs {Math.Round(OpenAt - footfall, 1)} more footfall"
            : null;
        return new FootfallReading(localeId, footfall, people, days, missing,
            perPerson.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList());
    }

    /// <summary>One signal per (Locale, person, source, day): saying it twice in a day isn't twice the pull.</summary>
    public static bool Duplicate(IEnumerable<FootfallSignal> ledger, FootfallSignal s)
        => ledger.Any(x => x.LocaleId == s.LocaleId && x.ParticipantId == s.ParticipantId && x.Source == s.Source && x.Day == s.Day);
}
