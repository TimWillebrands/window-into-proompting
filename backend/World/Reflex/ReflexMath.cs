namespace PartyTown.World.Reflex;

/// <summary>What a Persona is to whoever just reached them.</summary>
/// <param name="HoursApart">Since they last talked; null if they never have.</param>
public readonly record struct Bond(double Exchanges, double Warmth, double? HoursApart)
{
    public static readonly Bond None = new(0, 0, null);
}

/// <summary>What System One can't read from the state: numeric traits, and the one norm that isn't its call.</summary>
public readonly record struct Landscape(double Impulsivity, double Chattiness, bool Human);

/// <summary>Reflex as pure functions (ADR 0031): attention filter, landscape, conflict, timing.</summary>
public static class ReflexMath
{
    /// <summary>Load a Persona can't go above: nobody is wholly unreachable while awake.</summary>
    public const double MaxLoad = 0.95;
    /// <summary>What it takes to notice anything with nothing else going on.</summary>
    public const double NoticeFloor = 0.12;
    /// <summary>How much load raises that bar (perceptual load theory).</summary>
    public const double LoadGain = 0.6;
    /// <summary>Signal-detection noise: near the bar, noticing is a coin flip.</summary>
    public const double NoticeNoise = 0.05;
    /// <summary>Own name adds this much (the cocktail party effect).</summary>
    public const double NameBreakthrough = 0.5;
    /// <summary>Half-life of habituation to one source, in minutes.</summary>
    public const double HabitHalfLifeMinutes = 5;
    /// <summary>Half-life of how long someone's absence takes to feel like news, in hours.</summary>
    public const double SurpriseHours = 6;
    /// <summary>Above this load, an intention waits for the next breakpoint.</summary>
    public const double BreakpointLoad = 0.6;
    /// <summary>Top two options closer than this: System One is torn.</summary>
    public const double ConflictMargin = 0.08;

    public static readonly TimeSpan OwedWithin = TimeSpan.FromMinutes(12);
    public static readonly TimeSpan ForgottenAfter = TimeSpan.FromHours(6);
    public static readonly TimeSpan BreakpointCheck = TimeSpan.FromMinutes(3);
    /// <summary>Owed answers are retried this often before giving up on a broken model.</summary>
    public const int MaxTries = 3;
    /// <summary>A pause this long ends an exchange: the next line is a fresh Percept.</summary>
    public static readonly TimeSpan ExchangeHolds = TimeSpan.FromMinutes(3);

    /// <summary>Weighed past the caller's deadline: the caller already replied, so the verdict mustn't act.</summary>
    public static bool Late(Percept p, long now) => p.Deadline is { } deadline && now > deadline;

    /// <summary>Independent demands combine like noisy-or: each eats into what's left free.</summary>
    public static double Load(double availability, bool onShift, bool away, bool occupied, bool talkingElsewhere)
    {
        var free = Math.Clamp(availability, 0, 1)
            * (onShift ? 0.4 : 1) * (away ? 0.7 : 1) * (occupied ? 0.65 : 1) * (talkingElsewhere ? 0.5 : 1);
        return Math.Min(MaxLoad, 1 - free);
    }

    /// <summary>Attention is per channel: a phone in hand costs the room around you, not the next message.</summary>
    public static bool Occupies(string? activity, bool onDevice, PerceptKind? channel)
        => !string.IsNullOrEmpty(activity) && !(onDevice && channel is null or PerceptKind.Direct or PerceptKind.Line);

    /// <summary>How well they know someone, 0..1, saturating.</summary>
    public static double Closeness(double exchanges) => 1 - Math.Exp(-Math.Max(0, exchanges) / 20);

    /// <summary>Prediction error of seeing someone: nothing for a colleague just talked to, full for a stranger.</summary>
    public static double Surprise(double? hoursApart)
        => hoursApart is not { } h ? 1 : 1 - Math.Pow(0.5, Math.Max(0, h) / SurpriseHours);

    /// <summary>Orienting fades with repetition from one source.</summary>
    public static double Habit(double seen) => 1 / (1 + 0.6 * Math.Max(0, seen));

    /// <summary>Habituation spares what carries signal: being addressed doesn't wear off, it nags.</summary>
    public static bool Addressed(Percept p) => p.Named || p.FromHuman || p.Kind is PerceptKind.Direct or PerceptKind.Knock;

    /// <summary>Repetition count after <paramref name="minutes"/> without it.</summary>
    public static double Recover(double seen, double minutes) => seen * Math.Pow(0.5, Math.Max(0, minutes) / HabitHalfLifeMinutes);

    /// <summary>How loud a Percept arrives, before load.</summary>
    public static double Strength(Percept p, Bond bond, double seen)
    {
        var raw = p.Kind switch
        {
            PerceptKind.Direct => 0.7,
            PerceptKind.Line => 0.2,
            PerceptKind.Speech => 0.5,
            PerceptKind.Arrival => (p.Near ? 0.5 : 0.2) * (0.25 + 0.75 * Surprise(bond.HoursApart)),
            PerceptKind.Overheard => p.Near ? 0.45 : 0.3,
            PerceptKind.Knock => 0.8,
            _ => 0.2,
        };
        var personal = 0.2 * Closeness(bond.Exchanges) + (p.FromHuman ? 0.15 : 0) + (p.Named ? NameBreakthrough : 0);
        var habit = Addressed(p) ? Math.Max(0.6, Habit(seen)) : Habit(seen);
        return Math.Clamp((raw + personal) * habit, 0, 1);
    }

    public static double Threshold(double load) => NoticeFloor + LoadGain * Math.Clamp(load, 0, 1);

    /// <param name="unit">Uniform in [0, 1).</param>
    public static bool Noticed(double strength, double load, double unit)
        => strength + (unit * 2 - 1) * NoticeNoise >= Threshold(load);

    /// <summary>Traits lean on an option, neutral at 0.5; load, ties and mood are in the state, so leaning on them counts them twice.</summary>
    public static double Pull(string option, Landscape l)
    {
        var bold = 0.7 + 0.6 * l.Impulsivity;
        var talky = 0.5 + l.Chattiness;
        return option switch
        {
            ReflexOptions.Now or ReflexOptions.GoOver => bold,
            ReflexOptions.Soon => 2 - bold,
            ReflexOptions.Leave => l.Human ? 0 : 1,
            ReflexOptions.Speak or ReflexOptions.Greet or ReflexOptions.CallOut => talky,
            ReflexOptions.Listen or ReflexOptions.Nod or ReflexOptions.CarryOn => 2 - talky,
            _ => 1,
        };
    }

    /// <summary>System One's distribution reshaped by the landscape, summing to 1.</summary>
    public static Dictionary<string, double> Shape(IReadOnlyDictionary<string, double> weighed, Landscape l)
    {
        // The floor keeps every allowed option alive, so a ruled-out one can't win by being the only mass.
        var shaped = weighed.ToDictionary(kv => kv.Key, kv => Math.Max(kv.Value, 1e-6) * Pull(kv.Key, l));
        var sum = shaped.Values.Sum();
        return sum <= 0 ? weighed.ToDictionary(kv => kv.Key, kv => kv.Value) : shaped.ToDictionary(kv => kv.Key, kv => kv.Value / sum);
    }

    /// <summary>Conflict monitoring: no clear winner is the cue for System Two.</summary>
    public static bool Torn(IReadOnlyDictionary<string, double> shaped)
    {
        if (shaped.Count < 2) return false;
        var top = shaped.Values.OrderByDescending(v => v).Take(2).ToList();
        return top[0] - top[1] < ConflictMargin;
    }

    /// <summary>Heavy-tailed, as human response times are; <paramref name="u1"/>, <paramref name="u2"/> uniform in (0, 1).</summary>
    public static double Lognormal(double median, double sigma, double u1, double u2)
        => median * Math.Exp(sigma * Math.Sqrt(-2 * Math.Log(Math.Max(u1, 1e-9))) * Math.Cos(2 * Math.PI * u2));

    /// <summary>Time to read and turn to it: near nothing face to face, a few seconds on a screen.</summary>
    public static TimeSpan ReplyLag(PerceptKind kind, double load, double u1, double u2)
    {
        var (median, max) = kind == PerceptKind.Speech ? (0.6, 2.0) : (2.5 + 5 * load, 12.0);
        return TimeSpan.FromSeconds(Math.Clamp(Lognormal(median, 0.5, u1, u2), 0.2, max));
    }

    /// <summary>How long something gets put off: scales with load, never past what a human is owed.</summary>
    public static TimeSpan PutOff(double load, bool owed, double u1, double u2)
    {
        var minutes = Lognormal(2 + 18 * Math.Clamp(load, 0, 1), 0.8, u1, u2);
        var max = owed ? OwedWithin.TotalMinutes * 0.75 : 45;
        return TimeSpan.FromMinutes(Math.Clamp(minutes, 0.75, max));
    }
}

/// <summary>What happens to a held Intention on a look at it.</summary>
public enum IntentionStep { Wait, Surface, Postpone, Forget }

/// <summary>Prospective memory as pure rules (ADR 0031).</summary>
public static class Intentions
{
    /// <summary>Owed ones always happen; the rest wait for a breakpoint or slip away.</summary>
    public static IntentionStep Step(Intention i, long now, double load)
    {
        if (i.OwedBy is { } owed && now >= owed) return IntentionStep.Surface;
        if (now < i.DueAt) return IntentionStep.Wait;
        if (i.OwedBy is null && now >= i.ForgetAt) return IntentionStep.Forget;
        return load > ReflexMath.BreakpointLoad ? IntentionStep.Postpone : IntentionStep.Surface;
    }

    /// <summary>A follow-up in a held chat brings it to mind sooner; both what they first said and their latest stay in view.</summary>
    public static void Renew(Intention i, string gist, long now)
    {
        if (string.IsNullOrWhiteSpace(i.Gist)) i.Gist = gist;
        else if (!string.IsNullOrWhiteSpace(gist) && gist != i.Gist) i.Latest = gist;
        i.DueAt = Math.Max(now, now + (i.DueAt - now) / 2);
    }

    /// <summary>"\"first\"" or "\"first\", then later \"latest\"": the model sees what's owed and decides.</summary>
    public static string Said(Intention i)
        => i.Latest is { } latest ? $"\"{i.Gist}\", then later \"{latest}\"" : $"\"{i.Gist}\"";

    /// <summary>When the next look at these is due, or null.</summary>
    public static long? NextLook(IEnumerable<Intention> held)
    {
        long? next = null;
        foreach (var i in held)
        {
            var at = i.OwedBy is { } owed ? Math.Min(i.DueAt, owed) : i.DueAt;
            if (next is null || at < next) next = at;
        }
        return next;
    }

    /// <summary>The line in their mind; unfinished things stay present (Zeigarnik).</summary>
    public static string Render(IReadOnlyList<Intention> held)
        => held.Count == 0
            ? string.Empty
            : "# Still on your mind\n" + string.Join("\n", held.Take(3).Select(i => $"- You meant to get back to {i.FromName} (they said: {Said(i)}).")) + "\n";
}
