using System.Globalization;
using System.Text.RegularExpressions;

namespace PartyTown.World.Self;

/// <summary>Where the Day takes them (ADR 0027): stretches away from home, resolved to Locales after the Day is written.</summary>
public sealed partial class PersonaSelfGrain
{
    private const string WhereaboutsSpec = "one line per stretch of the day spent away from home, \"HH:MM-HH:MM | place | district\" (e.g. \"09:00-17:30 | the office in Zuid | Zuid\"). Reuse a name from \"Places you go\" when it's the same place; otherwise a plain, specific name. Leave out time at home and errands under an hour.";

    public Task<List<Whereabout>> GetWhereaboutsAsync()
        => Task.FromResult(state.State.DayKey == DateTimeOffset.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            ? state.State.Whereabouts.ToList()
            : new List<Whereabout>());

    /// <summary>"Places you go: …" for the Day prompt, so names converge on shared places.</summary>
    private async Task<string> PlacesYouGoAsync(Guid partyId, Guid personaId)
    {
        try
        {
            var places = GrainFactory.GetGrain<Places.IPlacesGrain>(partyId);
            var known = await places.GetKnownPlacesAsync(personaId);
            var districts = await places.GetDistrictNamesAsync();
            return (known.Count == 0 ? string.Empty : $"\nPlaces you go: {string.Join("; ", known)}")
                + $"\nParts of town (reuse these names for the district): {string.Join(", ", districts)}";
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Known places unavailable for the Day prompt");
            return string.Empty;
        }
    }

    /// <summary>Stretches that aren't errands: today's Occupation shift (ADR 0026).</summary>
    private async Task<List<(int Start, int End)>> FixedWindowsAsync(Guid partyId, Guid personaId)
    {
        // Today's shift (ADR 0026) is work, never an errand; called detached, so awaiting Occupation can't cycle.
        try
        {
            var job = (await GrainFactory.GetGrain<Work.IOccupationGrain>(WorldKeys.Life(partyId, personaId)).GetAsync()).Job;
            return job is not null && Work.Shifts.WorksOn(job, DateTimeOffset.Now.DayOfWeek)
                ? new List<(int Start, int End)> { (job.Start, job.End) }
                : new List<(int Start, int End)>();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Shift window lookup failed for {Persona}", personaId);
            return new List<(int Start, int End)>();
        }
    }

    /// <summary>Detached: resolve each stretch to a Locale (in its District) once the Day is written.</summary>
    private async Task ApplyWhereaboutsAsync(Guid partyId, Guid personaId)
    {
        try
        {
            var fixedWindows = await FixedWindowsAsync(partyId, personaId);
            state.State.Whereabouts.RemoveAll(w => fixedWindows.Any(f => w.Start < f.End && f.Start < w.End));
            var places = GrainFactory.GetGrain<Places.IPlacesGrain>(partyId);
            var growth = GrainFactory.GetGrain<Growth.IGrowthGrain>(partyId);
            foreach (var w in state.State.Whereabouts.Where(w => w.LocaleId is null))
            {
                w.LocaleId = await places.LocaleInAsync(w.Place, personaId, w.District);
                await growth.NoteAsync(w.LocaleId.Value, personaId, Growth.FootfallSources.Day);
            }
            await state.WriteStateAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Resolving whereabouts failed for {Persona}", personaId);
        }
    }
}

/// <summary>A stretch of the Day spent somewhere other than home.</summary>
[GenerateSerializer, Alias("PartyTown.World.Self.Whereabout")]
public sealed class Whereabout
{
    /// <summary>Local minutes since midnight.</summary>
    [Id(0)] public int Start { get; set; }
    [Id(1)] public int End { get; set; }
    [Id(2)] public string Place { get; set; } = string.Empty;
    [Id(3)] public string? District { get; set; }
    /// <summary>Resolved Locale (or drawn Place) id; null until resolved.</summary>
    [Id(4)] public Guid? LocaleId { get; set; }
}

public static partial class SelfMath
{
    [GeneratedRegex(@"^\s*[-*•]?\s*(\d{1,2})[:.h](\d{2})\s*(?:-|–|—|to)\s*(\d{1,2})[:.h](\d{2})\s*\|\s*([^|]+?)\s*(?:\|\s*([^|]*?)\s*)?$")]
    private static partial Regex WhereaboutLine();

    private static readonly HashSet<string> AtHome = new(StringComparer.OrdinalIgnoreCase) { "home", "at home", "my place", "my flat", "my apartment" };

    /// <summary>"09:00-17:30 | the office in Zuid | Zuid" lines; home, wraps past midnight and nonsense are dropped.</summary>
    public static List<Whereabout> ParseWhereabouts(string? raw)
        => (raw ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => WhereaboutLine().Match(l))
            .Where(m => m.Success)
            .Select(m => (Start: Minute(m.Groups[1].Value, m.Groups[2].Value), End: Minute(m.Groups[3].Value, m.Groups[4].Value),
                Place: Unprefixed(m.Groups[5].Value), District: m.Groups[6].Success ? Unprefixed(m.Groups[6].Value) : string.Empty))
            .Where(w => w.Start >= 0 && w.End > w.Start && w.Place.Length > 0 && !AtHome.Contains(w.Place))
            .Select(w => new Whereabout
            {
                Start = w.Start, End = w.End, Place = WorldStream.Clip(w.Place, 60),
                District = w.District.Length == 0 ? null : WorldStream.Clip(w.District, 40),
            })
            .Take(6)
            .ToList();

    private static int Minute(string h, string m)
        => int.TryParse(h, out var hour) && int.TryParse(m, out var min) && hour < 24 && min < 60 ? hour * 60 + min : -1;

    private static string Unprefixed(string s)
    {
        var t = s.Trim().Trim('"');
        return t.StartsWith("new:", StringComparison.OrdinalIgnoreCase) ? t[4..].Trim() : t;
    }
}
