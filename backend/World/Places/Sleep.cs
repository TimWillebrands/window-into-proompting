namespace PartyTown.World.Places;

/// <summary>When a Body sleeps (spec phase 7). Local minutes since midnight; pure.</summary>
public static class Sleep
{
    private const int Day = 1440;

    private static readonly string[] GetUpOrder = { "kitchen", "sofa", "desk" };

    /// <summary>Where waking up takes the body: the kitchen, else a free spot that isn't the bed; null when there's none.</summary>
    public static string? GetUpSpot(Place home, Func<string, int> occupied)
    {
        var free = home.Spots.Where(s => s.Id != "bed" && occupied(s.Id) < s.Capacity).ToList();
        return GetUpOrder.FirstOrDefault(id => free.Any(s => s.Id == id)) ?? free.FirstOrDefault()?.Id;
    }

    /// <summary>Bed and wake minute: the day's first beat wakes them when known, else a stable chronotype from the id.</summary>
    public static (int Bed, int Wake) Window(Guid id, int? firstBeat = null, int? lastBeat = null)
    {
        var bytes = id.ToByteArray();
        // A day written just after midnight can open with a small-hours beat; nobody's day starts before 05:00.
        var wake = Math.Clamp(firstBeat ?? 390 + bytes[0] * 180 / 255, EarliestWake, LatestWake); // 06:30–09:30 by chronotype
        var bed = 1350 + bytes[1] * 180 / 255;                       // 22:30–01:30 (past midnight = > 1440)
        if (lastBeat is { } last) bed = Math.Min(Math.Max(bed - 60, Later(last, wake) + 30), LatestBed);
        return (bed % Day, wake);
    }

    public const int EarliestWake = 5 * 60;
    public const int LatestWake = 11 * 60;
    /// <summary>02:30, as minutes past the previous midnight: a late last beat never leaves no night at all.</summary>
    public const int LatestBed = Day + 150;

    /// <summary>Whether <paramref name="minute"/> falls between bedtime and waking (wrapping midnight).</summary>
    public static bool IsNight(int minute, int bed, int wake)
        => bed > wake ? minute >= bed || minute < wake : minute >= bed && minute < wake;

    /// <summary>A minute before waking counts as late evening of the same day.</summary>
    private static int Later(int minute, int wake) => minute < wake ? minute + Day : minute;
}
