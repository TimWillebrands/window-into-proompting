using PartyTown.Grains;
using PartyTown.Services.SystemOne;

namespace PartyTown.World.Places;

/// <summary>Openings (ADR 0027): a Locale becomes a drawn Place from a kit, keeping its id; the author can close it again.</summary>
public sealed partial class PlacesGrain
{
    public const int MaxOpened = 24;
    /// <summary>Visit days someone needs at a Locale before a visit there can open it.</summary>
    public const int RegularDays = 2;

    public async Task<Place?> OpenAsync(Guid localeId, string? archetype, string reason)
    {
        await SettleAsync();
        if (Find(localeId) is not { Kind: PlaceKinds.Locale } place || place.Name == Elsewhere) return null;
        if (state.State.Places.Count(p => p.OpenedAt is not null && p.Kind == PlaceKinds.Public) >= MaxOpened) return null;
        var kit = Kits.Of(archetype ?? Kits.FromName(place.Name) ?? await ClassifyAsync(place.Name));
        place.Kind = PlaceKinds.Public;
        place.Archetype = kit.Archetype;
        place.Spots = Kits.SpotsOf(kit);
        place.Purpose ??= kit.Purpose;
        if (place.Description == place.Name) place.Description = kit.Description;
        place.At = kit.At;
        place.Scene = "sketch";
        place.DistrictId ??= Districts.Centre;
        place.OpenedAt = Now();
        if (kit.Archetype == "office" && place.Name.EndsWith("'s work", StringComparison.Ordinal))
            place.Name = place.Name.Replace("'s work", "'s office", StringComparison.Ordinal);
        EnsureFurni(place);
        foreach (var body in Occupants(place.Id).ToList()) Arrive(body, place);
        await SaveAsync();
        await PublishPlaceAsync(place);
        foreach (var body in Occupants(place.Id)) await PublishAsync(body);
        await SyncTalkRoomAsync(place.Id);
        var where = place.DistrictId == Districts.Centre ? string.Empty : $" in {DistrictName(place.DistrictId)}";
        _ = GrainFactory.GetGrain<IWorldClockGrain>(PartyId).AnnounceAsync($"{Capital(place.Name)}{where} is on the map now.");
        logger.LogInformation("Opened {Place} as {Archetype} ({Reason})", place.Name, kit.Archetype, reason);
        return place;
    }

    public async Task<bool> CloseAsync(Guid placeId)
    {
        await SettleAsync();
        if (Find(placeId) is not { OpenedAt: not null, Kind: PlaceKinds.Public } place) return false;
        var inside = Occupants(place.Id).ToList();
        place.Kind = PlaceKinds.Locale;
        place.Spots = PlaceSeeds.Locale(place.Name).Spots;
        place.Scene = null;
        place.OpenedAt = null;
        foreach (var body in inside)
        {
            EndPose(body);
            body.SpotId = place.Spots[0].Id;
            body.AwayAs ??= body.Activity == "working" ? "at work" : "out";
            if (body.Activity == "working") body.Activity = null;
            await PublishAsync(body);
        }
        if (place.TalkRoomId is { } room)
            await GrainFactory.GetGrain<IChatGroupGrain>(room).SetParticipantIdsAsync(new HashSet<Guid>());
        await SaveAsync();
        await PublishPlaceAsync(place);
        return true;
    }

    public static readonly TimeSpan GoesQuiet = TimeSpan.FromDays(7);

    /// <summary>Opened a week ago or more, and nobody's been there or named it in a week. Never deleted; any visit or mention wakes it.</summary>
    internal static bool IsDormant(Place place, long now, IEnumerable<(Guid Place, string Day)> visits, string since)
        => place is { OpenedAt: { } opened, Kind: PlaceKinds.Public }
            && now - opened >= GoesQuiet.TotalMilliseconds
            && (place.MentionedAt is not { } named || now - named >= GoesQuiet.TotalMilliseconds)
            && !visits.Any(v => v.Place == place.Id && string.CompareOrdinal(v.Day, since) >= 0);

    private void RefreshDormancy()
    {
        var now = Now();
        var since = DayKey(-(int)GoesQuiet.TotalDays + 1);
        var visits = state.State.Visits.Values.SelectMany(v => v.Select(SplitVisit)).ToList();
        foreach (var place in state.State.Places) place.Dormant = IsDormant(place, now, visits, since);
    }

    /// <summary>Someone who was away here is now simply here: at work behind the counter, otherwise wherever there's room.</summary>
    private void Arrive(Body body, Place place)
    {
        var working = body.AwayAs is "at work" or "working";
        var spot = (working ? place.Spots.FirstOrDefault(s => s.Staff) : null) ?? PickSpot(place, null, body.ParticipantId) ?? place.Spots[0];
        body.SpotId = spot.Id;
        body.Posture = spot.Seat ? Posture.Sitting : Posture.Standing;
        body.Activity = working ? "working" : body.Activity;
        body.ActivityUntil = working ? null : body.ActivityUntil;
        body.AwayAs = null;
        body.AwayByDay = false;
        body.CameFrom = null;
    }

    /// <summary>A visit to someone at a Locale they're a regular at opens it (a concrete plan needs a place).</summary>
    private async Task<bool> OpenForVisitAsync(Place locale)
    {
        var regulars = state.State.Visits.Count(kv => kv.Value.Select(SplitVisit).Count(v => v.Place == locale.Id) >= RegularDays);
        return regulars > 0 && await OpenAsync(locale.Id, null, "visit") is not null;
    }

    /// <summary>System One picks the kit when the name doesn't say; a plain room otherwise.</summary>
    private async Task<string?> ClassifyAsync(string name)
    {
        if (!judge.Enabled) return null;
        try
        {
            var answers = await judge.AskAsync($"A place people in town talk about: \"{name}\"",
                new Dictionary<string, SystemOneQuestion>
                {
                    ["kind"] = new ChoiceQuestion("What kind of place is it most likely?",
                        Kits.All.ToDictionary(k => k.Archetype, k => (string?)k.Purpose)),
                }, CancellationToken.None);
            return answers.SampleChoice("kind", 0.3);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Classifying {Place} failed; opening it as a plain room", name);
            return null;
        }
    }
}
