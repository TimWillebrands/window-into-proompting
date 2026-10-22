using System.Globalization;
using System.Text.RegularExpressions;
using PartyTown.World.Economy;

namespace PartyTown.World.Work;

/// <summary>A Participant's job in one Party (ADR 0026): what, where, when. Party-scoped, never on the Persona.</summary>
[GenerateSerializer, Alias("PartyTown.World.Work.Occupation")]
public sealed class Occupation
{
    [Id(0)] public string Title { get; set; } = string.Empty;
    /// <summary>Plain name of the workplace ("the Café", "the office in Zuid").</summary>
    [Id(1)] public string Workplace { get; set; } = string.Empty;
    /// <summary>Set when the workplace is a Place in this Party: the job happens onscreen.</summary>
    [Id(2)] public Guid? PlaceId { get; set; }
    /// <summary>Offscreen workplace as a Places Locale (ADR 0027), once resolved.</summary>
    [Id(3)] public Guid? LocaleId { get; set; }
    /// <summary>Workdays as a bitmask over <see cref="DayOfWeek"/> (bit 0 = Sunday).</summary>
    [Id(4)] public int Days { get; set; } = Shifts.Weekdays;
    /// <summary>Local minutes since midnight; a shift never wraps past midnight.</summary>
    [Id(5)] public int Start { get; set; } = 9 * 60;
    [Id(6)] public int End { get; set; } = 17 * 60;

    public bool Onscreen => PlaceId is not null;
}

/// <summary>One day's shift as it actually went.</summary>
[GenerateSerializer, Alias("PartyTown.World.Work.ShiftRecord")]
public sealed class ShiftRecord
{
    [Id(0)] public string Day { get; set; } = string.Empty;
    [Id(1)] public ShiftStatus Status { get; set; }
    [Id(2)] public long? StartedAt { get; set; }
    [Id(3)] public long? EndedAt { get; set; }
    [Id(4)] public int LateMinutes { get; set; }
    [Id(5)] public int Paid { get; set; }
}

[GenerateSerializer]
public enum ShiftStatus
{
    On = 0,
    Done = 1,
    LeftEarly = 2,
    Missed = 3,
}

/// <summary>What the clock should do about someone's job this pulse.</summary>
[GenerateSerializer]
public enum ShiftStep
{
    None,
    Start,
    End,
    Miss,
}

/// <summary>Pure shift rules (ADR 0026): when work happens, what it pays, how it reads in a prompt.</summary>
public static class Shifts
{
    public const int Weekdays = 0b0111110;
    public const int EveryDay = 0b1111111;
    /// <summary>Past this, turning up counts as late.</summary>
    public const int GraceMinutes = 10;

    private static readonly string[] Short = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

    public static string DayKey(DateTimeOffset local) => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static bool WorksOn(Occupation job, DayOfWeek day) => (job.Days & (1 << (int)day)) != 0;

    public static int Minute(DateTimeOffset local) => local.Hour * 60 + local.Minute;

    public static bool During(Occupation job, DateTimeOffset local)
        => WorksOn(job, local.DayOfWeek) && Minute(local) >= job.Start && Minute(local) < job.End;

    /// <summary>The one transition due now, given today's record (null = not started today).</summary>
    public static ShiftStep Step(Occupation job, ShiftRecord? record, DateTimeOffset local, bool asleep)
    {
        var today = DayKey(local);
        if (record is { Status: ShiftStatus.On } on)
            return on.Day != today || Minute(local) >= job.End ? ShiftStep.End : ShiftStep.None;
        if (record?.Day == today || !WorksOn(job, local.DayOfWeek)) return ShiftStep.None;
        if (Minute(local) >= job.End) return ShiftStep.Miss;
        if (Minute(local) >= job.Start && !asleep) return ShiftStep.Start;
        return ShiftStep.None;
    }

    /// <summary>Pull taken off everything but work while on shift: other things wait, they're not forbidden.</summary>
    public static int Damping(string appId, bool onShift) => onShift && appId != WorkApp.AppId ? 1 : 0;

    /// <summary>Hourly rate so five 8-hour shifts pay what a week of flat daily pay did (ADR 0024).</summary>
    public static double HourlyRate(string? means) => Money.DailyPay(means) * 7 / 40.0;

    public static int Pay(string? means, long startedAt, long endedAt)
        => (int)Math.Round(HourlyRate(means) * Math.Max(0, endedAt - startedAt) / 3_600_000.0);

    public static string Clock(int minute) => $"{minute / 60:00}:{minute % 60:00}";

    public static string DaysText(int days)
    {
        if (days == EveryDay) return "every day";
        if (days == Weekdays) return "Mon–Fri";
        if (days == 0b1000001) return "weekends";
        return string.Join(", ", Enumerable.Range(0, 7).Select(d => (d + 1) % 7).Where(d => (days & (1 << d)) != 0).Select(d => Short[d]));
    }

    /// <summary>"barista at the Café (Mon–Fri 09:00–17:00)".</summary>
    public static string Describe(Occupation job)
        => $"{job.Title} at {job.Workplace} ({DaysText(job.Days)} {Clock(job.Start)}–{Clock(job.End)})";

    /// <summary>Today's shift as a fixed beat for the Day generator; null on a day off.</summary>
    public static string? FixedToday(Occupation? job, DateTimeOffset local)
        => job is null || !WorksOn(job, local.DayOfWeek) ? null
            : $"work as {job.Title} at {job.Workplace} {Clock(job.Start)}–{Clock(job.End)}";

    /// <summary>Work as one plain fact for "Right now"; null when work isn't near.</summary>
    public static string? Line(Occupation? job, ShiftRecord? today, DateTimeOffset local, bool atWork)
    {
        if (job is null) return null;
        var minute = Minute(local);
        var line = today switch
        {
            { Status: ShiftStatus.On } when atWork => $"You're at work ({job.Title}, {job.Workplace}) until {Clock(job.End)}.",
            { Status: ShiftStatus.On } => $"You're meant to be at work at {job.Workplace} until {Clock(job.End)}, and you're not there.",
            { Status: ShiftStatus.Done } when today.Day == DayKey(local) => $"Done with work for today (worked {Clock(job.Start)}–{Clock(job.End)}).",
            { Status: ShiftStatus.LeftEarly } when today.Day == DayKey(local) => "You left work early today.",
            { Status: ShiftStatus.Missed } when today.Day == DayKey(local) => $"You missed work at {job.Workplace} today.",
            _ when !WorksOn(job, local.DayOfWeek) => $"Day off from work ({job.Title}, {job.Workplace}).",
            _ when minute < job.Start && job.Start - minute <= 120 => $"Work at {job.Workplace} starts at {Clock(job.Start)} (in {job.Start - minute} min).",
            _ when minute >= job.Start && minute < job.End => $"You should be at work at {job.Workplace} since {Clock(job.Start)}.",
            _ => null,
        };
        return line;
    }

    /// <summary>"Mon-Fri", "weekdays", "Tue, Thu, Sat", "every day" → bitmask; weekdays when unreadable.</summary>
    public static int ParseDays(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Weekdays;
        var t = text.Trim().ToLowerInvariant();
        if (t.Contains("every") || t.Contains("daily") || t.Contains("7")) return EveryDay;
        if (t.Contains("weekend")) return 0b1000001;
        if (t.Contains("weekday")) return Weekdays;
        var found = Regex.Matches(t, "sun|mon|tue|wed|thu|fri|sat").Select(m => Array.FindIndex(Short, s => s.Equals(m.Value, StringComparison.OrdinalIgnoreCase))).ToList();
        if (found.Count == 0) return Weekdays;
        // "mon-fri" or "mon–sat" is a range; anything else is a list.
        if (found.Count == 2 && Regex.IsMatch(t, "[a-z]{3}\\w*\\s*[-–to]+\\s*[a-z]{3}"))
        {
            var mask = 0;
            for (var d = found[0]; ; d = (d + 1) % 7)
            {
                mask |= 1 << d;
                if (d == found[1]) break;
            }
            return mask;
        }
        return found.Aggregate(0, (m, d) => m | (1 << d));
    }

    /// <summary>"09:00-17:00", "9-17", "7am to 3pm" → minutes; null when unreadable or wrapping midnight.</summary>
    public static (int Start, int End)? ParseHours(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var times = Regex.Matches(text.ToLowerInvariant(), @"(\d{1,2})(?:[:.h](\d{2}))?\s*(am|pm)?")
            .Select(m =>
            {
                var h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) % 24;
                var min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
                if (m.Groups[3].Value == "pm" && h < 12) h += 12;
                if (m.Groups[3].Value == "am" && h == 12) h = 0;
                return h * 60 + Math.Min(min, 59);
            })
            .ToList();
        if (times.Count < 2 || times[1] <= times[0] || times[1] - times[0] > 14 * 60) return null;
        return (times[0], times[1]);
    }
}

/// <summary>Pure: keeping a job's workplace in step with Places.</summary>
public static class WorkRules
{
    /// <summary>Public → onscreen, Locale → offscreen (an Opened place Closed again); the name follows renames.</summary>
    public static void Sync(Occupation job, Places.Place place)
    {
        if (place.Kind == Places.PlaceKinds.Public)
        {
            job.PlaceId = place.Id;
        }
        else if (place.Kind == Places.PlaceKinds.Locale)
        {
            job.LocaleId ??= place.Id;
            job.PlaceId = null;
        }
        job.Workplace = place.Name;
    }
}
