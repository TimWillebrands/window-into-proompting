namespace PartyTown.World.Reflex;

public readonly record struct Bond(double Exchanges, double Warmth, double? HoursApart)
{
    public static readonly Bond None = new(0, 0, null);
}

public readonly record struct Landscape(double Impulsivity, double Chattiness, bool Human);

public static class ReflexMath
{
    public const double MaxLoad = 0.95;
    public const double NoticeFloor = 0.12;
    public const double LoadGain = 0.6;
    public const double NoticeNoise = 0.05;
    public const double NameBreakthrough = 0.5;
    public const double HabitHalfLifeMinutes = 5;
    public const double SurpriseHours = 6;
    public const double BreakpointLoad = 0.6;
    public const double ConflictMargin = 0.08;

    public static readonly TimeSpan OwedWithin = TimeSpan.FromMinutes(12);
    public static readonly TimeSpan ForgottenAfter = TimeSpan.FromHours(6);
    public static readonly TimeSpan BreakpointCheck = TimeSpan.FromMinutes(3);
    public const int MaxTries = 3;
    public static readonly TimeSpan ExchangeHolds = TimeSpan.FromMinutes(3);

    public static bool Late(Percept p, long now) => p.Deadline is { } deadline && now > deadline;

    public static double Load(double availability, bool onShift, bool away, bool occupied, bool talkingElsewhere)
    {
        var free = Math.Clamp(availability, 0, 1)
            * (onShift ? 0.4 : 1) * (away ? 0.7 : 1) * (occupied ? 0.65 : 1) * (talkingElsewhere ? 0.5 : 1);
        return Math.Min(MaxLoad, 1 - free);
    }

    public static bool Occupies(string? activity, bool onDevice, PerceptKind? channel)
        => !string.IsNullOrEmpty(activity) && !(onDevice && channel is null or PerceptKind.Direct or PerceptKind.Line);

    public static double Closeness(double exchanges) => 1 - Math.Exp(-Math.Max(0, exchanges) / 20);

    public static double Surprise(double? hoursApart)
        => hoursApart is not { } h ? 1 : 1 - Math.Pow(0.5, Math.Max(0, h) / SurpriseHours);

    public static double Habit(double seen) => 1 / (1 + 0.6 * Math.Max(0, seen));

    public static bool Addressed(Percept p) => p.Named || p.FromHuman || p.Kind is PerceptKind.Direct or PerceptKind.Knock;

    public static double Recover(double seen, double minutes) => seen * Math.Pow(0.5, Math.Max(0, minutes) / HabitHalfLifeMinutes);

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

    public static bool Noticed(double strength, double load, double unit)
        => strength + (unit * 2 - 1) * NoticeNoise >= Threshold(load);

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

    public static Dictionary<string, double> Shape(IReadOnlyDictionary<string, double> weighed, Landscape l)
    {
        var shaped = weighed.ToDictionary(kv => kv.Key, kv => Math.Max(kv.Value, 1e-6) * Pull(kv.Key, l));
        var sum = shaped.Values.Sum();
        return sum <= 0 ? weighed.ToDictionary(kv => kv.Key, kv => kv.Value) : shaped.ToDictionary(kv => kv.Key, kv => kv.Value / sum);
    }

    public static bool Torn(IReadOnlyDictionary<string, double> shaped)
    {
        if (shaped.Count < 2) return false;
        var top = shaped.Values.OrderByDescending(v => v).Take(2).ToList();
        return top[0] - top[1] < ConflictMargin;
    }

    public static double Lognormal(double median, double sigma, double u1, double u2)
        => median * Math.Exp(sigma * Math.Sqrt(-2 * Math.Log(Math.Max(u1, 1e-9))) * Math.Cos(2 * Math.PI * u2));

    public static TimeSpan ReplyLag(PerceptKind kind, double load, double u1, double u2)
    {
        var (median, max) = kind == PerceptKind.Speech ? (0.6, 2.0) : (2.5 + 5 * load, 12.0);
        return TimeSpan.FromSeconds(Math.Clamp(Lognormal(median, 0.5, u1, u2), 0.2, max));
    }

    public static TimeSpan PutOff(double load, bool owed, double u1, double u2)
    {
        var minutes = Lognormal(2 + 18 * Math.Clamp(load, 0, 1), 0.8, u1, u2);
        var max = owed ? OwedWithin.TotalMinutes * 0.75 : 45;
        return TimeSpan.FromMinutes(Math.Clamp(minutes, 0.75, max));
    }
}

public enum IntentionStep { Wait, Surface, Postpone, Forget }

public static class Intentions
{
    public static IntentionStep Step(Intention i, long now, double load)
    {
        if (i.OwedBy is { } owed && now >= owed) return IntentionStep.Surface;
        if (now < i.DueAt) return IntentionStep.Wait;
        if (i.OwedBy is null && now >= i.ForgetAt) return IntentionStep.Forget;
        return load > ReflexMath.BreakpointLoad ? IntentionStep.Postpone : IntentionStep.Surface;
    }

    public static void Renew(Intention i, string gist, long now)
    {
        if (string.IsNullOrWhiteSpace(i.Gist)) i.Gist = gist;
        else if (!string.IsNullOrWhiteSpace(gist) && gist != i.Gist) i.Latest = gist;
        i.DueAt = Math.Max(now, now + (i.DueAt - now) / 2);
    }

    public static string Said(Intention i)
        => i.Latest is { } latest ? $"\"{i.Gist}\", then later \"{latest}\"" : $"\"{i.Gist}\"";

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

    public static string Render(IReadOnlyList<Intention> held)
        => held.Count == 0
            ? string.Empty
            : "# Still on your mind\n" + string.Join("\n", held.Take(3).Select(i => $"- You meant to get back to {i.FromName} (they said: {Said(i)}).")) + "\n";
}
