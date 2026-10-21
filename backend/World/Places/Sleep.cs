namespace PartyTown.World.Places;

/// <summary>When a Body sleeps (spec phase 7). Local minutes since midnight; pure.</summary>
public static class Sleep
{
    private const int Day = 1440;

    /// <summary>Bed and wake minute: the day's first beat wakes them when known, else a stable chronotype from the id.</summary>
    public static (int Bed, int Wake) Window(Guid id, int? firstBeat = null, int? lastBeat = null)
    {
        var bytes = id.ToByteArray();
        var wake = firstBeat ?? 390 + bytes[0] * 180 / 255;          // 06:30–09:30
        var bed = 1350 + bytes[1] * 180 / 255;                       // 22:30–01:30 (past midnight = > 1440)
        if (lastBeat is { } last) bed = Math.Max(bed - 60, Later(last, wake) + 30);
        return (bed % Day, wake);
    }

    /// <summary>Whether <paramref name="minute"/> falls between bedtime and waking (wrapping midnight).</summary>
    public static bool IsNight(int minute, int bed, int wake)
        => bed > wake ? minute >= bed || minute < wake : minute >= bed && minute < wake;

    /// <summary>A minute before waking counts as late evening of the same day.</summary>
    private static int Later(int minute, int wake) => minute < wake ? minute + Day : minute;
}
