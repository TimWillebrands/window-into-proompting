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
    /// <summary>How many of their own spreads above usual the pull must jump: every judge has its own flat prior and its own wobble.</summary>
    public const double Spreads = 2.5;
    /// <summary>A floor under the jump, so a judge that barely moves doesn't make a lack out of noise.</summary>
    public const double Surprise = 0.06;
    public const int Settled = 6;
    private const double Rate = 0.2;
    public static readonly TimeSpan Every = TimeSpan.FromHours(6);
    /// <summary>After a reach that turned out to be nothing, how soon the next may be asked about.</summary>
    public static readonly TimeSpan AskAgain = TimeSpan.FromHours(1);
    public const int Shown = 5;
    public const string Shape = """{"want": "<what you wanted to do, first person, one plain concrete line; or none>"}""";

    public static SystemOneQuestion Question(string name)
        => new NoulQuestion($"What {name} actually wants to do right now is none of these options, and this world gives them no way to do it.");

    /// <summary>What they usually feel under one judge: a running mean and spread of this noul, and how many turns it has seen.</summary>
    public readonly record struct Baseline(double Mean, int Seen, double Var = 0)
    {
        public double Spread => Math.Sqrt(Var);

        public Baseline After(double p)
        {
            if (Seen == 0) return new(p, 1);
            var d = p - Mean;
            return new(Mean + Rate * d, Seen + 1, (1 - Rate) * (Var + Rate * d * d));
        }
    }

    /// <summary>How far above usual counts for them: a few of their own spreads, never less than the floor.</summary>
    public static double Jump(Baseline usual) => Math.Max(Surprise, Spreads * usual.Spread);

    /// <summary>Felt when it jumps above their usual (a damper is an expectation), not within the cap, and not right after a reach that was nothing.</summary>
    public static bool Felt(double p, Baseline usual, long? lastAt, DateTimeOffset now, long? askedAt = null)
        => usual.Seen >= Settled && p - usual.Mean >= Jump(usual)
            && (lastAt is not { } at || now.ToUnixTimeMilliseconds() - at >= Every.TotalMilliseconds)
            && (askedAt is not { } asked || now.ToUnixTimeMilliseconds() - asked >= AskAgain.TotalMilliseconds);

    public static string Ask(string identity) => $"""
{identity}

Nothing on offer right now is what you want to do. Say plainly what you wanted to do instead, that this world gives you no way to: something concrete you'd do here, with these people, places and things, not a dream or a feeling about life.
If one of the options was it after all, or you could do it here another way (say it to someone, walk somewhere that exists, write it down), say none: a lack is what this world has no way for at all.
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
