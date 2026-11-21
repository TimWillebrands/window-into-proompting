namespace PartyTown.World.Places;

public static class Sleep
{
    private const int Day = 1440;

    private static readonly string[] GetUpOrder = { "kitchen", "sofa", "desk" };

    public static string? GetUpSpot(Place home, Func<string, int> occupied)
    {
        var free = home.Spots.Where(s => s.Id != "bed" && occupied(s.Id) < s.Capacity).ToList();
        return GetUpOrder.FirstOrDefault(id => free.Any(s => s.Id == id)) ?? free.FirstOrDefault()?.Id;
    }

    public static (int Bed, int Wake) Window(Guid id, int? firstBeat = null, int? lastBeat = null)
    {
        var bytes = id.ToByteArray();
        var wake = Math.Clamp(firstBeat ?? 390 + bytes[0] * 180 / 255, EarliestWake, LatestWake);
        var bed = 1350 + bytes[1] * 180 / 255;
        if (lastBeat is { } last) bed = Math.Min(Math.Max(bed - 60, Later(last, wake) + 30), LatestBed);
        return (bed % Day, wake);
    }

    public const int EarliestWake = 5 * 60;
    public const int LatestWake = 11 * 60;
    public const int LatestBed = Day + 150;

    public static bool IsNight(int minute, int bed, int wake)
        => bed > wake ? minute >= bed || minute < wake : minute >= bed && minute < wake;

    public const int TurnInPast = 15;
    public static readonly TimeSpan TurnInAtMost = TimeSpan.FromHours(6);

    public static long TurnInUntil(long now, int minute, int bed, int wake)
    {
        var untilBed = IsNight(minute, bed, wake) ? 0 : (bed - minute + Day) % Day;
        return now + (long)Math.Min(TimeSpan.FromMinutes(untilBed + TurnInPast).TotalMilliseconds, TurnInAtMost.TotalMilliseconds);
    }

    public static bool Evening(int minute, int bed, int wake)
        => !IsNight(minute, bed, wake) && (bed - minute + Day) % Day < (minute - wake + Day) % Day;

    public static bool TurnedIn(long? until, long now, int minute, int bed, int wake)
        => until is { } u && (u > now || (now - u < TurnInAtMost.TotalMilliseconds && Evening(minute, bed, wake)));

    private static int Later(int minute, int wake) => minute < wake ? minute + Day : minute;
}
