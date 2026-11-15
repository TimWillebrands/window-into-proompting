using PartyTown.Services.SystemOne;

namespace PartyTown.World.Desires;

public static class Lack
{
    public const string Action = "lack";
    public const string Key = "lack";
    public const double Spreads = 2.5;
    public const double Surprise = 0.06;
    public const int Settled = 6;
    private const double Rate = 0.2;
    public static readonly TimeSpan Every = TimeSpan.FromHours(6);
    public static readonly TimeSpan AskAgain = TimeSpan.FromHours(1);
    public const int Shown = 5;
    public const string Shape = """{"want": "<what you wanted to do, first person, one plain concrete line; or none>"}""";

    public static SystemOneQuestion Question(string name)
        => new NoulQuestion($"What {name} actually wants to do right now is none of these options, and this world gives them no way to do it.");

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

    public static double Jump(Baseline usual) => Math.Max(Surprise, Spreads * usual.Spread);

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

    public static string? Read(string? want)
    {
        want = want?.Trim().Trim('"');
        return string.IsNullOrWhiteSpace(want) || want.Length < 8 || want.StartsWith("none", StringComparison.OrdinalIgnoreCase) ? null : WorldStream.Clip(want, 160);
    }

    public static IReadOnlyList<(string Who, string What)> Of(IEnumerable<WorldActivity> day)
        => day.Where(a => a.Action == Action).OrderByDescending(a => a.Ts).DistinctBy(a => a.PersonaId).Take(Shown)
            .Select(a => (a.PersonaName, a.Summary)).ToList();
}
