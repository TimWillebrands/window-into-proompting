using System.Globalization;
using System.Text;
using PartyTown.Services.SystemOne;

namespace PartyTown.World.Places;

public sealed partial class PlacesGrain
{
    private const string Elsewhere = "somewhere else";
    private const int ShortlistSize = 6;
    private const double SameAt = 0.5;
    private const int MaxAlsoCalled = 12;
    private static readonly string[] CityWords = { "amsterdam", "city", "town" };

    private Place Resolved(Place place) => place.SameAs is { } to && Find(to) is { } target ? target : place;

    private static IEnumerable<string> Names(Place place) => place.AlsoCalled.Prepend(place.Name);

    private List<string> DistrictNames() => state.State.Districts.Select(d => d.Name).ToList();

    internal static HashSet<string> CoreWords(string name, IEnumerable<string> districts)
    {
        var where = districts.SelectMany(NameWords).Concat(CityWords).Select(Unaccented).ToHashSet();
        return NameWords(name).Select(Unaccented).Where(w => !where.Contains(w)).ToHashSet();
    }

    private static string Unaccented(string word)
        => new(word.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());

    internal static List<Place> Shortlist(string name, IEnumerable<Place> places, IReadOnlyCollection<string> districts)
    {
        var words = CoreWords(name, districts);
        var kind = Kits.FromName(name);
        return places
            .Where(p => p.Kind != PlaceKinds.Home && p.SameAs is null && !Unnamed(p.Name) && !IsStreets(p))
            .Select(p => (Place: p, Shared: Names(p).SelectMany(n => CoreWords(n, districts)).Distinct().Count(words.Contains)))
            .Where(x => x.Shared > 0 || (kind is not null && x.Place.Kind == PlaceKinds.Public && x.Place.Archetype == kind))
            .OrderByDescending(x => x.Shared).ThenByDescending(x => x.Place.Kind == PlaceKinds.Public)
            .Take(ShortlistSize).Select(x => x.Place).ToList();
    }

    internal static bool Covers(Place drawn, Place locale, IReadOnlyCollection<string> districts)
    {
        var words = CoreWords(locale.Name, districts);
        return words.Count > 0 && (locale.DistrictId is null || locale.DistrictId == drawn.DistrictId)
            && words.IsSubsetOf(Names(drawn).SelectMany(n => CoreWords(n, districts)));
    }

    internal static string? PickSame(SystemOneAnswers answers)
    {
        if (!answers.Choices.TryGetValue("same", out var probs) || probs.Count == 0) return null;
        var best = probs.MaxBy(kv => kv.Value);
        return best.Key != Elsewhere && best.Value >= SameAt ? best.Key : null;
    }

    private async Task<Place?> SamePlaceAsync(string name, string? district, List<Place> candidates, string question)
    {
        if (candidates.Count == 0) return null;
        var byKey = new Dictionary<string, Place>();
        var options = new Dictionary<string, string?>();
        foreach (var p in candidates)
        {
            var key = p.DistrictId is { } d ? $"{p.Name} ({DistrictName(d)})" : p.Name;
            if (byKey.TryAdd(key, p)) options[key] = p.Purpose ?? p.Description;
        }
        options[Elsewhere] = "a place that isn't any of these";
        try
        {
            var where = string.IsNullOrWhiteSpace(district) ? string.Empty : $", in {district}";
            var answers = await judge.AskAsync($"Someone in town names a place: \"{name}\"{where}.",
                new Dictionary<string, SystemOneQuestion> { ["same"] = new ChoiceQuestion(question, options) }, CancellationToken.None);
            return PickSame(answers) is { } pick ? byKey.GetValueOrDefault(pick) : null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Judging whether {Name} is a known place failed; taking it as new", name);
            return null;
        }
    }

    private static void AddName(Place place, string name)
    {
        if (Names(place).Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))) return;
        place.AlsoCalled.Add(name);
        if (place.AlsoCalled.Count > MaxAlsoCalled) place.AlsoCalled.RemoveAt(0);
    }

    private Task<Place?> AlreadyDrawnAsync(Place locale)
    {
        var district = locale.DistrictId ?? Districts.Centre;
        var drawn = state.State.Places.Where(p => p.Kind == PlaceKinds.Public && p.Id != locale.Id && (p.DistrictId ?? Districts.Centre) == district);
        return SamePlaceAsync(locale.Name, DistrictName(district), Shortlist(locale.Name, drawn, DistrictNames()),
            "It's about to go on the map. Is it really one of these places already there, under another name? Pick one only if it's clearly the same place.");
    }

    private async Task FoldAsync(Place locale, Place into)
    {
        locale.SameAs = into.Id;
        AddName(into, locale.Name);
        var moved = Occupants(locale.Id).ToList();
        foreach (var body in moved)
        {
            body.PlaceId = into.Id;
            body.DestPlaceId = null;
            body.DestSpotId = null;
            Arrive(body, into);
        }
        await SaveAsync();
        foreach (var body in moved) await PublishAsync(body);
        if (moved.Count > 0) await SyncTalkRoomAsync(into.Id);
        logger.LogInformation("{Locale} is {Place}: folded into it instead of opening", locale.Name, into.Name);
    }

    private static readonly TimeSpan SweepAfter = TimeSpan.FromMinutes(1);
    private IGrainTimer? _sweep;

    private void SweepSoon()
        => _sweep = this.RegisterGrainTimer(async () =>
        {
            _sweep?.Dispose();
            try
            {
                var folded = await SweepSameAsync();
                state.State.SameSwept = true;
                await SaveAsync();
                logger.LogInformation("Same-place sweep folded {Count} Locales: {Folded}", folded.Count, string.Join("; ", folded));
            }
            catch (Exception ex) { logger.LogWarning(ex, "Same-place sweep failed; it runs again on the next activation"); }
        }, new GrainTimerCreationOptions(SweepAfter, Timeout.InfiniteTimeSpan));

    public async Task<List<string>> SweepSameAsync()
    {
        await SettleAsync();
        var folded = new List<string>();
        foreach (var locale in state.State.Places.Where(p => p is { Kind: PlaceKinds.Locale, SameAs: null } && !Unnamed(p.Name)).ToList())
            if (await AlreadyDrawnAsync(locale) is { } drawn)
            {
                await FoldAsync(locale, drawn);
                folded.Add($"{locale.Name} → {drawn.Name}");
            }
        return folded;
    }

    private void CreditTalk(IEnumerable<Place> named, IReadOnlyCollection<Guid> speakers)
    {
        var districts = DistrictNames();
        var drawn = state.State.Places.Where(p => p.Kind == PlaceKinds.Public && !IsStreets(p)).ToList();
        var now = Now();
        foreach (var place in named.Select(p => p.Kind == PlaceKinds.Locale ? drawn.FirstOrDefault(d => Covers(d, p, districts)) ?? p : p).DistinctBy(p => p.Id))
        {
            if (place.Kind == PlaceKinds.Locale)
                foreach (var speaker in speakers)
                    _ = GrainFactory.GetGrain<Growth.IGrowthGrain>(PartyId).NoteAsync(place.Id, speaker, Growth.FootfallSources.Talk);
            else if (place.OpenedAt is not null) place.MentionedAt = now;
        }
    }

    private IEnumerable<Place> Nameable()
        => state.State.Places.Where(p => p.Kind is PlaceKinds.Locale or PlaceKinds.Public && p.SameAs is null && !Unnamed(p.Name) && !IsStreets(p));
}
