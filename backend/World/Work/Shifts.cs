using System.Globalization;
using System.Text.RegularExpressions;
using PartyTown.World.Economy;

namespace PartyTown.World.Work;

[GenerateSerializer, Alias("PartyTown.World.Work.Occupation")]
public sealed class Occupation
{
    [Id(0)] public string Title { get; set; } = string.Empty;
    [Id(1)] public string Workplace { get; set; } = string.Empty;
    [Id(2)] public Guid? PlaceId { get; set; }
    [Id(3)] public Guid? LocaleId { get; set; }
    [Id(4)] public int Days { get; set; } = Shifts.Weekdays;
    [Id(5)] public int Start { get; set; } = 9 * 60;
    [Id(6)] public int End { get; set; } = 17 * 60;
    [Id(7)] public Guid? CompanyId { get; set; }
    [Id(8)] public Practices.Practice? Practice { get; set; }
    [Id(9)] public Places.WorkClothes? Clothes { get; set; }

    public bool Onscreen => PlaceId is not null;
}

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

[GenerateSerializer]
public enum ShiftStep
{
    None,
    Start,
    End,
    Miss,
}

public static class Shifts
{
    public const int Weekdays = 0b0111110;
    public const int EveryDay = 0b1111111;
    public const int GraceMinutes = 10;

    private static readonly string[] Short = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

    public static string DayKey(DateTimeOffset local) => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static bool WorksOn(Occupation job, DayOfWeek day) => (job.Days & (1 << (int)day)) != 0;

    public static int Minute(DateTimeOffset local) => local.Hour * 60 + local.Minute;

    public static bool During(Occupation job, DateTimeOffset local)
        => WorksOn(job, local.DayOfWeek) && Minute(local) >= job.Start && Minute(local) < job.End;

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

    public static int Damping(string appId, bool onShift) => onShift && !IsWork(appId) ? 1 : 0;

    public static int Damping(Apps.Affordance a, bool onShift) => a.Meets is null ? Damping(a.AppId, onShift) : 0;

    public static bool IsWork(string appId) => appId is WorkApp.AppId or Companies.DeskApp.AppId or Practices.PracticeApp.AppId or Cdu.CduScreen.AppId;

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

    public static string Describe(Occupation job)
        => $"{job.Title} at {job.Workplace} ({DaysText(job.Days)} {Clock(job.Start)}–{Clock(job.End)})";

    public static string? FixedToday(Occupation? job, DateTimeOffset local)
        => job is null || !WorksOn(job, local.DayOfWeek) ? null
            : $"work as {job.Title} at {job.Workplace} {Clock(job.Start)}–{Clock(job.End)}";

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

    public static int ParseDays(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Weekdays;
        var t = text.Trim().ToLowerInvariant();
        if (t.Contains("every") || t.Contains("daily") || t.Contains("7")) return EveryDay;
        if (t.Contains("weekend")) return 0b1000001;
        if (t.Contains("weekday")) return Weekdays;
        var found = Regex.Matches(t, "sun|mon|tue|wed|thu|fri|sat").Select(m => Array.FindIndex(Short, s => s.Equals(m.Value, StringComparison.OrdinalIgnoreCase))).ToList();
        if (found.Count == 0) return Weekdays;
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

public static class WorkRules
{
    public static void Carry(Occupation next, Occupation? previous)
    {
        if (next.CompanyId is not null) next.Practice = null;
        else if (next.Practice is not null) next.CompanyId = null;
        else (next.CompanyId, next.Practice) = (previous?.CompanyId, previous?.Practice);
    }

    public static void CarryClothes(Occupation next, Occupation? previous)
    {
        if (next.Clothes is null && Places.WorkClothesRules.Fits(previous?.Clothes, next.Title)) next.Clothes = previous!.Clothes;
        if (next.Clothes is { } c && !Places.WorkClothesRules.Fits(c, next.Title)) next.Clothes = null;
    }

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
