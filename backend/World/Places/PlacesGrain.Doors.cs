namespace PartyTown.World.Places;

/// <summary>Workplace doors (ADR 0041): staff come and go, the author's access list too, everyone else knocks.</summary>
public sealed partial class PlacesGrain
{
    public async Task WorkAtAsync(Guid participantId, Guid? placeId)
    {
        await SettleAsync();
        var changed = new List<Place>();
        foreach (var place in state.State.Places.Where(p => p.Id != placeId && p.Staff.Contains(participantId)))
        {
            place.Staff.Remove(participantId);
            changed.Add(place);
        }
        if (placeId is { } id && Find(id) is { Kind: not PlaceKinds.Home } at && !at.Staff.Contains(participantId))
        {
            at.Staff.Add(participantId);
            at.Allowed.Remove(participantId);
            SettleDoor(at);
            changed.Add(at);
        }
        if (changed.Count == 0) return;
        await SaveAsync();
        foreach (var place in changed)
        {
            await PublishPlaceAsync(place);
            await ShowOutAsync(place);
        }
    }

    public async Task<Place?> SetAccessAsync(Guid placeId, string access, List<Guid> allowed)
    {
        await SettleAsync();
        if (!PlaceAccess.IsKnown(access) || Find(placeId) is not { Kind: not PlaceKinds.Home } place) return null;
        place.Access = access;
        place.Allowed = allowed.Distinct().Where(id => !place.Staff.Contains(id) && state.State.Bodies.ContainsKey(id)).ToList();
        await SaveAsync();
        await PublishPlaceAsync(place);
        await ShowOutAsync(place);
        return place;
    }

    public async Task ExpectAsync(Guid placeId, Guid hostId, List<Guid> participantIds)
    {
        await SettleAsync();
        if (Find(placeId) is not { } place || !place.IsWorkplaceDoor() || !MayEnter(place, hostId)) return;
        var expected = participantIds.Distinct().Where(id => !MayEnter(place, id)).ToList();
        if (expected.Count == 0) return;
        place.Guests.AddRange(expected);
        await SaveAsync();
        await PublishPlaceAsync(place);
    }

    /// <summary>A staffed Place gets its kit's door the first time; after that the door is the author's.</summary>
    private static void SettleDoor(Place place)
    {
        if (place.Access is null && place.Kind == PlaceKinds.Public && place.Staff.Count > 0)
            place.Access = Kits.Of(place.Archetype).Access ?? PlaceAccess.Open;
    }

    /// <summary>Whoever may no longer be there heads home.</summary>
    private async Task ShowOutAsync(Place place)
    {
        foreach (var body in state.State.Bodies.Values.Where(b => (b.DestPlaceId ?? b.PlaceId) == place.Id && !MayEnter(place, b.ParticipantId)).ToList())
            if (state.State.Places.HomeOf(body.ParticipantId) is { } home) await MoveAsync(body.ParticipantId, home.Id, null);
    }

    /// <summary>A workplace door as someone inside sees it: staff notice who doesn't belong, a visitor that they're visiting.</summary>
    private static string? DoorLine(Place place, Guid viewer, IReadOnlyList<Body> others)
    {
        if (!place.IsWorkplaceDoor()) return null;
        var rule = $"{Capital(place.Name)} is {PlaceAccess.Words(place.Access)}";
        if (!place.Staff.Contains(viewer))
            return place.Allowed.Contains(viewer)
                ? $"You don't work here, but you're welcome {AtName(place, viewer)} any time."
                : $"You don't work here: {rule}{(place.Guests.Contains(viewer) ? ", and you were let in" : string.Empty)}.";
        var outsiders = others.Where(o => !place.Staff.Contains(o.ParticipantId)).ToList();
        if (outsiders.Count == 0) return $"{rule}.";
        string How(Body o) => place.Allowed.Contains(o.ParticipantId) ? "on the access list"
            : place.Guests.Contains(o.ParticipantId) ? "let in"
            : "no idea how they got in";
        return $"{rule}; {string.Join(", ", outsiders.Select(o => $"{o.Name} ({How(o)})"))} {(outsiders.Count == 1 ? "doesn't" : "don't")} work here.";
    }
}
