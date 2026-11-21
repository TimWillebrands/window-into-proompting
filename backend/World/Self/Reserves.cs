using System.Globalization;

namespace PartyTown.World.Self;

public enum Hunger
{
    None,
    Peckish,
    Hungry,
    VeryHungry,
    Famished,
}

public enum Tiredness
{
    Fresh,
    Fine,
    Flagging,
    Spent,
}

[GenerateSerializer, Alias("PartyTown.World.Self.Reserves")]
public readonly record struct Reserves(
    [property: Id(0)] double Fed,
    [property: Id(1)] double Rested,
    [property: Id(2)] long AteAt,
    [property: Id(3)] long UpSince,
    [property: Id(4)] bool Perked,
    [property: Id(5)] bool Felt,
    [property: Id(6)] bool Napped)
{
    public static readonly Reserves Unfelt = new(1, 1, 0, 0, false, false, false);

    public Hunger Hunger => Felt ? Bio.HungerOf(Fed) : Hunger.None;

    public Tiredness Tiredness => Felt ? Bio.TirednessOf(Rested) : Tiredness.Fresh;
}

public static class Bio
{
    public const string Food = "hunger";
    public const string Rest = "tiredness";

    public const double HungerPerHour = 0.11;
    public const double TiringPerHour = 0.036;
    public const double SleepPerHour = 0.14;
    public const double Meal = 1.0;
    public const double LightMeal = 0.8;
    public const double Snack = 0.35;
    public const double Nap = 0.3;
    public const double Perk = 0.15;
    public const double AwayFed = 0.6;
    public const double WakesAtMost = 0.44;
    public const double WakesAtLeast = 0.3;
    public const double Walk = 0.01;
    public const double Tram = 0.02;
    public const int Narrowed = 6;
    public const int Seated = 5;
    public const int SiestaBefore = 17;

    public static readonly TimeSpan Lived = TimeSpan.FromMinutes(75);
    public static readonly TimeSpan FeltFor = TimeSpan.FromHours(2);
    public static readonly TimeSpan PerkLasts = TimeSpan.FromHours(2);
    public static readonly TimeSpan ADay = TimeSpan.FromHours(26);
    public static readonly TimeSpan JustUp = TimeSpan.FromHours(3);
    public static readonly TimeSpan MealTakes = TimeSpan.FromMinutes(12);
    public static readonly TimeSpan NapTakes = TimeSpan.FromMinutes(30);

    public static Hunger HungerOf(double fed) => fed switch
    {
        < 0.15 => Hunger.Famished,
        < 0.3 => Hunger.VeryHungry,
        < 0.45 => Hunger.Hungry,
        < 0.6 => Hunger.Peckish,
        _ => Hunger.None,
    };

    public static Tiredness TirednessOf(double rested) => rested switch
    {
        < 0.15 => Tiredness.Spent,
        < 0.4 => Tiredness.Flagging,
        < 0.7 => Tiredness.Fine,
        _ => Tiredness.Fresh,
    };

    public static double Cap(string? energy) => energy switch
    {
        "low" => 0.58,
        "high" => 1,
        _ => 0.8,
    };

    public static Reserves Now(SelfState s, DateTimeOffset now)
    {
        if (s.LivedAt == 0) return Reserves.Unfelt;
        var ms = now.ToUnixTimeMilliseconds();
        var perked = s.PerkUntil > ms && TirednessOf(s.Rested) != Tiredness.Spent;
        return new Reserves(s.Fed, Math.Min(1, s.Rested + (perked ? Perk : 0)), s.AteAt, s.UpSince, perked,
            ms - s.LivedAt < FeltFor.TotalMilliseconds, s.NappedAt >= s.UpSince && s.NappedAt != 0);
    }

    public static Reserves Settle(SelfState s, DateTimeOffset now, bool away)
    {
        var ms = now.ToUnixTimeMilliseconds();
        if (s.LivedAt == 0 || ms - s.LivedAt > ADay.TotalMilliseconds) Begin(s, now);
        else
        {
            Advance(s, ms);
            if (ms - s.UpSince > ADay.TotalMilliseconds) Recover(s, 8, now);
        }
        s.LivedAt = ms;
        if (away && s.Fed < AwayFed)
        {
            s.Fed = AwayFed;
            s.AteAt = ms;
        }
        return Now(s, now);
    }

    public static double Eat(SelfState s, double amount, DateTimeOffset now)
    {
        var ms = now.ToUnixTimeMilliseconds();
        if (s.BodyAt == 0) Begin(s, now);
        else Advance(s, ms);
        var before = s.Fed;
        s.Fed = Math.Min(1, s.Fed + Math.Max(0, amount));
        s.AteAt = ms;
        return before;
    }

    public static void Rise(SelfState s, double sleptHours, DateTimeOffset now)
    {
        if (s.LivedAt == 0) return;
        Recover(s, sleptHours, now);
        s.Fed = Math.Clamp(s.Fed, WakesAtLeast, WakesAtMost);
    }

    private static void Recover(SelfState s, double sleptHours, DateTimeOffset now)
    {
        var ms = now.ToUnixTimeMilliseconds();
        s.Rested = Math.Min(1, s.Rested + Math.Max(0, sleptHours) * SleepPerHour);
        if (s.DayKey == now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) s.Rested = Math.Min(s.Rested, Cap(s.Energy));
        s.UpSince = ms;
        s.BodyAt = ms;
        s.PerkUntil = 0;
    }

    public static void Morning(SelfState s, DateTimeOffset now)
    {
        if (s.LivedAt == 0 || now.ToUnixTimeMilliseconds() - s.UpSince > JustUp.TotalMilliseconds) return;
        s.Rested = Math.Min(s.Rested, Cap(s.Energy));
    }

    public static void Exert(SelfState s, double cost, DateTimeOffset now)
    {
        if (s.LivedAt == 0) return;
        Advance(s, now.ToUnixTimeMilliseconds());
        s.Rested = Math.Clamp(s.Rested - Math.Max(0, cost), 0, 1);
    }

    public static bool LieDown(SelfState s, DateTimeOffset now)
    {
        if (s.LivedAt == 0 || (s.NappedAt != 0 && s.NappedAt >= s.UpSince)) return false;
        var ms = now.ToUnixTimeMilliseconds();
        Advance(s, ms);
        s.Rested = Math.Min(1, s.Rested + Nap);
        s.NappedAt = ms;
        return true;
    }

    public static void PerkUp(SelfState s, DateTimeOffset now)
    {
        if (s.LivedAt == 0) return;
        s.PerkUntil = now.ToUnixTimeMilliseconds() + (long)PerkLasts.TotalMilliseconds;
    }

    public static bool Helps(Tiredness tiredness, DateTimeOffset local)
        => tiredness == Tiredness.Spent || (tiredness == Tiredness.Flagging && local.Hour is >= 6 and < SiestaBefore);

    public static string? Distress(Reserves r)
        => !r.Felt ? null
            : r.Hunger == Hunger.Famished ? Food
            : r.Tiredness == Tiredness.Spent ? Rest
            : null;

    public static int Seats(int usual, bool relief) => relief ? Math.Min(usual, Narrowed) : usual;

    public static int Pull(Hunger hunger) => hunger switch
    {
        Hunger.Famished => Seated,
        Hunger.VeryHungry => 3,
        Hunger.Hungry => 2,
        _ => 0,
    };

    public static int Pull(Tiredness tiredness) => tiredness switch
    {
        Tiredness.Spent => Seated,
        Tiredness.Flagging => 1,
        _ => 0,
    };

    public static double Relief(double before, double amount)
        => 0.5 + 0.3 * Math.Clamp(1 - before, 0, 1) * Math.Clamp(amount, 0, 1);

    public static string Energy(string dayEnergy, Reserves r)
    {
        if (!r.Felt) return dayEnergy;
        var live = r.Tiredness switch
        {
            Tiredness.Fresh => 2,
            Tiredness.Fine => 1,
            _ => 0,
        };
        var woke = dayEnergy switch
        {
            "high" => 2,
            "low" => 0,
            _ => 1,
        };
        return r.Tiredness == Tiredness.Spent ? "none left" : Math.Min(live, woke) switch
        {
            2 => "high",
            1 => "medium",
            _ => "low",
        };
    }

    public static List<string> Lines(Reserves r, DateTimeOffset now)
    {
        var lines = new List<string>();
        if (!r.Felt) return lines;
        var since = Since(r, now);
        switch (r.Hunger)
        {
            case Hunger.Famished:
                lines.Add($"You're famished: {since}. It's hard to keep your mind on anything else until you've eaten.");
                break;
            case Hunger.VeryHungry:
                lines.Add($"You're really hungry: {since}.");
                break;
            case Hunger.Hungry:
                lines.Add($"You're hungry: {since}.");
                break;
        }
        if (r.Tiredness == Tiredness.Spent)
            lines.Add($"You're dead on your feet{(r.UpSince == 0 ? string.Empty : $", up since {Clock(r.UpSince, now)}")}. You'd keep anything short; what you want is to lie down.");
        return lines;
    }

    public static string Since(Reserves r, DateTimeOffset now)
    {
        if (r.AteAt == 0) return "it's been a good while since you ate";
        if (r.UpSince != 0 && r.AteAt < r.UpSince) return "you haven't eaten yet today";
        return $"nothing since {Clock(r.AteAt, now)}";
    }

    private static string Clock(long at, DateTimeOffset now)
    {
        var then = DateTimeOffset.FromUnixTimeMilliseconds(at).ToOffset(now.Offset);
        return then.Date == now.Date ? then.ToString("HH:mm", CultureInfo.InvariantCulture) : "yesterday";
    }

    private static (double Fed, double Rested) Drained(SelfState s, long ms)
    {
        var lived = Math.Clamp(ms - s.BodyAt, 0, Lived.TotalMilliseconds) / 3_600_000.0;
        return (Math.Clamp(s.Fed - lived * HungerPerHour, 0, 1), Math.Clamp(s.Rested - lived * TiringPerHour, 0, 1));
    }

    private static void Advance(SelfState s, long ms)
    {
        (s.Fed, s.Rested) = Drained(s, ms);
        s.BodyAt = ms;
    }

    private static void Begin(SelfState s, DateTimeOffset now)
    {
        var woke = new DateTimeOffset(now.Year, now.Month, now.Day, 7, 30, 0, now.Offset);
        if (woke > now) woke = now;
        s.UpSince = woke.ToUnixTimeMilliseconds();
        s.Rested = Math.Clamp(Cap(s.Energy) - (now - woke).TotalHours * TiringPerHour, 0.3, 1);
        s.Fed = 0.7;
        s.AteAt = 0;
        s.NappedAt = 0;
        s.PerkUntil = 0;
        s.BodyAt = now.ToUnixTimeMilliseconds();
    }
}
