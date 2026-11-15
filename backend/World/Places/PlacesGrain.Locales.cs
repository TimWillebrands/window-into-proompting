using System.Globalization;

namespace PartyTown.World.Places;

public sealed partial class PlacesGrain
{
    public const int MaxLocales = 80;
    private const string Somewhere = "somewhere";
    private const string WorkSuffix = "'s work";
    private static readonly TimeSpan VisitsKept = TimeSpan.FromDays(7);
    private static readonly HashSet<string> GenericWork = new(StringComparer.OrdinalIgnoreCase)
    {
        "work", "my work", "their work", "at work", "the office", "office", "the job", "job", "my job", "workplace", "the workplace",
    };

    public Task<Guid> LocaleAsync(string name, Guid? forParticipant) => MintAsync(name, forParticipant, null, match: true);

    public Task<Guid> NamedLocaleAsync(string name) => MintAsync(name, null, null, match: false);

    private async Task<Guid> MintAsync(string name, Guid? forParticipant, string? district, bool match)
    {
        await SyncCastAsync();
        var wanted = LocaleName(name, forParticipant);
        if (FindByName(wanted) is { } known) return known.Id;
        if (match && !Unnamed(wanted)
            && await SamePlaceAsync(wanted, district ?? Districts.FromName(wanted), Shortlist(wanted, state.State.Places, DistrictNames()),
                "Is that one of these places, under another name? Pick one only if it's clearly the same place.") is { } same)
        {
            AddName(same, wanted);
            await SaveAsync();
            return same.Id;
        }
        if (FindByName(wanted) is { } meanwhile) return meanwhile.Id;
        if (state.State.Places.Count(p => p.Kind == PlaceKinds.Locale) >= MaxLocales)
            wanted = Somewhere;
        if (FindByName(wanted) is { } fallback) return fallback.Id;
        var locale = PlaceSeeds.Locale(wanted);
        if (Districts.FromName(wanted) is { } said) locale.DistrictId = await DistrictAsync(said);
        state.State.Places.Add(locale);
        await SaveAsync();
        return locale.Id;
    }

    public async Task<Body?> GoAwayAsync(Guid participantId, Guid localeId, string? activity)
    {
        await SyncCastAsync();
        await SettleAsync();
        if (!state.State.Bodies.TryGetValue(participantId, out var body) || Find(localeId) is not { } found) return null;
        var place = Resolved(found);
        if (place.Kind != PlaceKinds.Locale)
        {
            var there = await MoveAsync(participantId, place.Id, place.Spots.FirstOrDefault(s => s.Staff)?.Id);
            return there is null || activity is null ? there : await SetActivityAsync(participantId, activity, null);
        }
        return await SendAwayAsync(body, place, activity, byDay: false);
    }

    private async Task<Body?> SendAwayAsync(Body body, Place locale, string? activity, bool byDay)
    {
        var from = body.DestPlaceId ?? body.PlaceId;
        if (Find(from) is { Kind: not PlaceKinds.Locale }) body.CameFrom = from;
        var moved = await WalkAsync(body, locale, locale.Spots[0].Id);
        if (moved is null) return null;
        moved.AwayAs = string.IsNullOrWhiteSpace(activity) ? "away" : WorldStream.Clip(activity.Trim(), 40);
        moved.AwayByDay = byDay;
        moved.Activity = null;
        moved.ActivityUntil = null;
        await SaveAsync();
        await PublishAsync(moved);
        return moved;
    }

    public async Task<Guid> LocaleInAsync(string name, Guid? forParticipant, string? district)
    {
        var id = await MintAsync(name, forParticipant, district, match: true);
        if (!string.IsNullOrWhiteSpace(district) && Find(id) is { Kind: PlaceKinds.Locale, DistrictId: null } locale)
        {
            locale.DistrictId = await DistrictAsync(district);
            await SaveAsync();
        }
        return id;
    }

    public Task<List<string>> GetKnownPlacesAsync(Guid participantId)
    {
        var body = state.State.Bodies.GetValueOrDefault(participantId);
        var district = Find(body?.PlaceId ?? Guid.Empty)?.DistrictId ?? Districts.Centre;
        var mine = (state.State.Visits.GetValueOrDefault(participantId) ?? new List<string>()).Select(v => SplitVisit(v).Place).ToHashSet();
        var visitors = state.State.Visits.SelectMany(kv => kv.Value.Select(v => (Who: kv.Key, SplitVisit(v).Place)))
            .GroupBy(v => v.Place).ToDictionary(g => g.Key, g => g.Select(v => v.Who).Distinct().Count());
        var locales = state.State.Places.Where(p => p is { Kind: PlaceKinds.Locale, SameAs: null } && !Unnamed(p.Name)).ToList();
        var known = state.State.Places.Where(p => p.Kind == PlaceKinds.Public && !p.Private && !p.Dormant)
            .OrderByDescending(p => p.DistrictId == district).ThenByDescending(p => visitors.GetValueOrDefault(p.Id))
            .Concat(locales.Where(p => mine.Contains(p.Id)).Take(3))
            .Concat(locales.OrderByDescending(p => visitors.GetValueOrDefault(p.Id)).Take(3))
            .DistinctBy(p => p.Id)
            .Select(p => p.DistrictId is { } d && d != district ? $"{p.Name} ({DistrictName(d)})" : p.Name)
            .ToList();
        return Task.FromResult(known);
    }

    public async Task<Body?> ReturnAsync(Guid participantId)
    {
        await SettleAsync();
        if (!state.State.Bodies.TryGetValue(participantId, out var body)) return null;
        if (Find(body.DestPlaceId ?? body.PlaceId) is not { Kind: PlaceKinds.Locale }) return body;
        var back = body.CameFrom is { } c && Find(c) is { Kind: not PlaceKinds.Locale } p && MayEnter(p, participantId)
            ? p
            : state.State.Places.HomeOf(participantId);
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

    public static bool Unnamed(string name) => name == Somewhere || name.EndsWith(WorkSuffix, StringComparison.Ordinal);

    private string LocaleName(string name, Guid? forParticipant)
    {
        var trimmed = WorldStream.Clip((name ?? string.Empty).Trim().TrimEnd('.'), 60);
        if (trimmed.Length == 0) return Somewhere;
        if (GenericWork.Contains(trimmed) && forParticipant is { } who && state.State.Bodies.TryGetValue(who, out var body))
            return $"{body.Name.Split(' ')[0]}{WorkSuffix}";
        return trimmed;
    }

    private Place? FindByName(string name)
    {
        static string Key(string n) => (n.StartsWith("the ", StringComparison.OrdinalIgnoreCase) ? n[4..] : n).Trim().ToLowerInvariant();
        var key = Key(name);
        var found = state.State.Places.FirstOrDefault(p => p.Kind != PlaceKinds.Home && Key(p.Name) == key)
            ?? state.State.Places.FirstOrDefault(p => p.Kind != PlaceKinds.Home && p.AlsoCalled.Any(n => Key(n) == key));
        return found is null ? null : Resolved(found);
    }

    private string AwaySituation(Body body, Place place)
    {
        var why = body.AwayAs ?? "away";
        var where = Unnamed(place.Name) ? string.Empty : $" {place.At} {place.Name}";
        var doing = body.Activity is { Length: > 0 } a ? $", {Yours(a)}" : string.Empty;
        return $"You're {why}{where}{doing}. That's offscreen: nobody in the World sees you there, but your phone works.";
    }

    private void NoteMentions(Guid speakerId, string content)
        => CreditTalk(Nameable().Where(p => Names(p).Any(n => Mentioned(content, n))).ToList(), new[] { speakerId });

    public Task NoteTopicsAsync(List<string> topics, List<Guid> speakers)
    {
        var places = Nameable().ToList();
        var named = topics
            .Select(t => places.Where(p => Names(p).Any(n => TopicNames(t, n)))
                .OrderByDescending(p => p.Kind == PlaceKinds.Public)
                .ThenBy(p => Names(p).Where(n => TopicNames(t, n)).Min(n => NameWords(n).Count))
                .FirstOrDefault())
            .OfType<Place>()
            .ToList();
        CreditTalk(named, speakers.Where(state.State.Bodies.ContainsKey).ToList());
        return Task.CompletedTask;
    }

    private static readonly HashSet<string> NameStop = new(StringComparer.Ordinal) { "the", "in", "at", "on", "of", "and", "near", "by" };

    private static List<string> NameWords(string s)
        => System.Text.RegularExpressions.Regex.Split(s.ToLowerInvariant(), @"[^\p{L}\p{N}]+")
            .Where(w => w.Length >= 3 && !NameStop.Contains(w)).ToList();

    internal static bool TopicNames(string topic, string placeName)
    {
        var said = NameWords(topic);
        var name = NameWords(placeName);
        return said.Count > 0 && said.All(name.Contains) && said.Count * 2 >= name.Count;
    }

    internal static bool Mentioned(string content, string localeName)
    {
        if (Unnamed(localeName)) return false;
        var key = localeName.StartsWith("the ", StringComparison.OrdinalIgnoreCase) ? localeName[4..] : localeName;
        if (key.Length < 5) return false;
        var at = content.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return false;
        var end = at + key.Length;
        return (at == 0 || !char.IsLetterOrDigit(content[at - 1])) && (end == content.Length || !char.IsLetterOrDigit(content[end]));
    }

    private bool BackfillPurposes()
    {
        var seeds = PlaceSeeds.Public().ToDictionary(p => p.Name);
        var changed = false;
        foreach (var place in state.State.Places.Where(p => p.Purpose is null))
        {
            if (place.Kind == PlaceKinds.Home) place.Purpose = PlaceSeeds.HomePurpose;
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
