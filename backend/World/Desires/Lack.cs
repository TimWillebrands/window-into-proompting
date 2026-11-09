using PartyTown.Services.SystemOne;

namespace PartyTown.World.Desires;

/// <summary>
/// Reaching past the arena (ADR 0047): what someone wanted to do that nothing here affords. Weighed in Initiative's own
/// System One call, voiced only on a hit, and read by Cytheurgists at their desk as felt need, not counted failure.
/// </summary>
public static class Lack
{
    public const string Action = "lack";
    public const string Key = "lack";
    public const double Threshold = 0.5;
    public static readonly TimeSpan Every = TimeSpan.FromHours(6);
    public const int Shown = 5;
    public const string Shape = """{"want": "<what you wanted to do, first person, one plain concrete line; or none>"}""";

    public static SystemOneQuestion Question(string name)
        => new NoulQuestion($"What {name} actually wants to do right now is none of these options, and this world gives them no way to do it.");

    /// <summary>Strong enough, and the last one long enough ago: a lack felt every turn is a mood, not a need.</summary>
    public static bool Felt(double p, long? lastAt, DateTimeOffset now)
        => p >= Threshold && (lastAt is not { } at || now.ToUnixTimeMilliseconds() - at >= Every.TotalMilliseconds);

    public static string Ask(string identity) => $"""
{identity}

Nothing on offer right now is what you want to do. Say plainly what you wanted to do instead, that this world gives you no way to: something concrete you'd do here, with these people, places and things, not a dream or a feeling about life.
If, looking again, one of the options was it after all, say none.
Output ONLY JSON: {Shape}
""";

    /// <summary>The voiced want, or null for "none" and empty.</summary>
    public static string? Read(string? want)
    {
        want = want?.Trim().Trim('"');
        return string.IsNullOrWhiteSpace(want) || want.Length < 8 || want.StartsWith("none", StringComparison.OrdinalIgnoreCase) ? null : WorldStream.Clip(want, 160);
    }

    /// <summary>The day's lacks, newest first, one per person.</summary>
    public static IReadOnlyList<(string Who, string What)> Of(IEnumerable<WorldActivity> day)
        => day.Where(a => a.Action == Action).OrderByDescending(a => a.Ts).DistinctBy(a => a.PersonaId).Take(Shown)
            .Select(a => (a.PersonaName, a.Summary)).ToList();
}
