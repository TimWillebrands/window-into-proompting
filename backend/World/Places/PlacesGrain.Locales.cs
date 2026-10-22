using System.Globalization;

namespace PartyTown.World.Places;

/// <summary>Locales (ADR 0027): named offscreen Places a Body is away at; never drawn, no talk. Plus visit days (ADR 0026 roles).</summary>
public sealed partial class PlacesGrain
{
    public const int MaxLocales = 80;
    private const string Elsewhere = "elsewhere";
    private static readonly TimeSpan VisitsKept = TimeSpan.FromDays(7);
    private static readonly HashSet<string> GenericWork = new(StringComparer.OrdinalIgnoreCase)
    {
        "work", "my work", "their work", "at work", "the office", "office", "the job", "job", "my job", "workplace", "the workplace",
    };

    public async Task<Guid> LocaleAsync(string name, Guid? forParticipant)
    {
        await SyncCastAsync();
        var wanted = LocaleName(name, forParticipant);
        if (FindByName(wanted) is { } known) return known.Id;
        if (state.State.Places.Count(p => p.Kind == PlaceKinds.Locale) >= MaxLocales)
            wanted = Elsewhere;
        if (FindByName(wanted) is { } fallback) return fallback.Id;
        var locale = PlaceSeeds.Locale(wanted);
        state.State.Places.Add(locale);
        await SaveAsync();
        return locale.Id;
    }

    public async Task<Body?> GoAwayAsync(Guid participantId, Guid localeId, string? activity)
    {
        await SyncCastAsync();
        await SettleAsync();
        if (!state.State.Bodies.TryGetValue(participantId, out var body) || Find(localeId) is not { } place) return null;
        if (place.Kind != PlaceKinds.Locale)
        {
            // Drawn workplace: just go there, behind the counter if it has one.
            var there = await MoveAsync(participantId, place.Id, place.Spots.FirstOrDefault(s => s.Staff)?.Id);
            return there is null || activity is null ? there : await SetActivityAsync(participantId, activity, null);
        }
        var from = body.DestPlaceId ?? body.PlaceId;
        if (Find(from) is { Kind: not PlaceKinds.Locale }) body.CameFrom = from;
        var moved = await WalkAsync(body, place, place.Spots[0].Id);
        if (moved is null) return null;
        moved.AwayAs = string.IsNullOrWhiteSpace(activity) ? "away" : WorldStream.Clip(activity.Trim(), 40);
        moved.Activity = null;
        moved.ActivityUntil = null;
        await SaveAsync();
        await PublishAsync(moved);
        return moved;
    }

    public async Task<Body?> ReturnAsync(Guid participantId)
    {
        await SettleAsync();
        if (!state.State.Bodies.TryGetValue(participantId, out var body)) return null;
        if (Find(body.DestPlaceId ?? body.PlaceId) is not { Kind: PlaceKinds.Locale }) return body;
        var back = body.CameFrom is { } c && Find(c) is { Kind: not PlaceKinds.Locale } p && MayEnter(p, participantId)
            ? p
            : state.State.Places.FirstOrDefault(p => p.Kind == PlaceKinds.Home && p.OwnerId == participantId);
        return back is null ? null : await MoveAsync(participantId, back.Id, null);
    }

    public async Task<Body?> SetActivityAsync(Guid participantId, string? activity, TimeSpan? lasts)
    {
        if (!state.State.Bodies.TryGetValue(participantId, out var body)) return null;
        body.Activity = string.IsNullOrWhiteSpace(activity) ? null : WorldStream.Clip(activity.Trim(), 60);
        body.ActivityUntil = body.Activity is null || lasts is null ? null : Now() + (long)lasts.Value.TotalMilliseconds;
        await SaveAsync();
        await PublishAsync(body);
        return body;
    }

    public Task<Dictionary<Guid, int>> GetVisitDaysAsync(Guid participantId)
    {
        NoteVisits();
        var since = DayKey(-(int)VisitsKept.TotalDays + 1);
        var days = state.State.Visits.GetValueOrDefault(participantId) ?? new List<string>();
        return Task.FromResult(days.Select(SplitVisit).Where(v => string.CompareOrdinal(v.Day, since) >= 0)
            .GroupBy(v => v.Place).ToDictionary(g => g.Key, g => g.Select(v => v.Day).Distinct().Count()));
    }

    /// <summary>Today's Place for every settled Body; drops days older than a week. True when anything changed.</summary>
    private bool NoteVisits()
    {
        var today = DayKey(0);
        var oldest = DayKey(-(int)VisitsKept.TotalDays);
        var changed = false;
        foreach (var body in state.State.Bodies.Values.Where(b => !b.IsWalking))
        {
            if (!state.State.Visits.TryGetValue(body.ParticipantId, out var days))
                state.State.Visits[body.ParticipantId] = days = new List<string>();
            var key = $"{body.PlaceId}:{today}";
            if (days.Contains(key)) continue;
            days.Add(key);
            days.RemoveAll(d => string.CompareOrdinal(SplitVisit(d).Day, oldest) < 0);
            changed = true;
        }
        return changed;
    }

    private static (Guid Place, string Day) SplitVisit(string visit)
    {
        var at = visit.IndexOf(':');
        return (Guid.TryParse(visit.AsSpan(0, Math.Max(0, at)), out var id) ? id : Guid.Empty, visit[(at + 1)..]);
    }

    private string DayKey(int offsetDays)
        => time.GetLocalNow().AddDays(offsetDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>"work" for someone becomes "Ana's work"; anything else is kept as said.</summary>
    private string LocaleName(string name, Guid? forParticipant)
    {
        var trimmed = WorldStream.Clip((name ?? string.Empty).Trim().TrimEnd('.'), 60);
        if (trimmed.Length == 0) return Elsewhere;
        if (GenericWork.Contains(trimmed) && forParticipant is { } who && state.State.Bodies.TryGetValue(who, out var body))
            return $"{body.Name.Split(' ')[0]}'s work";
        return trimmed;
    }

    /// <summary>Case-insensitive, "the" optional; Homes never match (they're named for their owner).</summary>
    private Place? FindByName(string name)
    {
        static string Key(string n) => (n.StartsWith("the ", StringComparison.OrdinalIgnoreCase) ? n[4..] : n).Trim().ToLowerInvariant();
        var key = Key(name);
        return state.State.Places.FirstOrDefault(p => p.Kind != PlaceKinds.Home && Key(p.Name) == key);
    }

    /// <summary>"You're at work at the office in Zuid", offscreen; generic workplaces don't repeat the name.</summary>
    private string AwaySituation(Body body, Place place)
    {
        var why = body.AwayAs ?? "away";
        var generic = place.Name.EndsWith("'s work", StringComparison.Ordinal) || place.Name == Elsewhere;
        var where = generic ? string.Empty : $" {place.At} {place.Name}";
        var doing = body.Activity is { Length: > 0 } a ? $", {Yours(a)}" : string.Empty;
        return $"You're {why}{where}{doing}. That's offscreen: nobody in the World sees you there, but your phone works.";
    }

    /// <summary>Seeded Places stored before ADR 0026 get their purpose and staff spots.</summary>
    private bool BackfillPurposes()
    {
        var seeds = PlaceSeeds.Public().ToDictionary(p => p.Name);
        var changed = false;
        foreach (var place in state.State.Places.Where(p => p.Purpose is null))
        {
            if (place.Kind == PlaceKinds.Home) place.Purpose = PlaceSeeds.Home(Guid.Empty, "x").Purpose;
            else if (seeds.TryGetValue(place.Name, out var seed))
            {
                place.Purpose = seed.Purpose;
                foreach (var spot in place.Spots)
                    spot.Staff = seed.Spots.FirstOrDefault(s => s.Id == spot.Id)?.Staff ?? false;
            }
            else continue;
            changed = true;
        }
        return changed;
    }
}
