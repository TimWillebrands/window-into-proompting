namespace PartyTown.World.Places;

/// <summary>Districts (ADR 0027): parts of the city; crossing between them is a tram ride.</summary>
public sealed partial class PlacesGrain
{
    private static readonly TimeSpan BetweenDistricts = TimeSpan.FromSeconds(45);

    public async Task<string> DistrictAsync(string name)
    {
        var before = state.State.Districts.Count;
        var id = DistrictFor(name);
        if (state.State.Districts.Count != before) await SaveAsync();
        return id;
    }

    public async Task<bool> SetDistrictAsync(Guid placeId, string districtId)
    {
        if (Find(placeId) is not { } place || state.State.Districts.All(d => d.Id != districtId)) return false;
        place.DistrictId = districtId;
        await SaveAsync();
        await PublishPlaceAsync(place);
        return true;
    }

    public Task<List<string>> GetDistrictNamesAsync() => Task.FromResult(state.State.Districts.Select(d => d.Name).ToList());

    public async Task<bool> SetHomeDistrictAsync(Guid ownerId, string district)
    {
        await SyncCastAsync();
        if (state.State.Places.FirstOrDefault(p => p.Kind == PlaceKinds.Home && p.OwnerId == ownerId) is not { } home) return false;
        var id = await DistrictAsync(district);
        if (home.DistrictId == id) return true;
        home.DistrictId = id;
        await SaveAsync();
        await PublishPlaceAsync(home);
        await EnsureStreetsAsync(id);
        return true;
    }

    private string DistrictName(string? id)
        => state.State.Districts.FirstOrDefault(d => d.Id == id)?.Name ?? "another part of town";

    /// <summary>Worlds from before Districts: everything drawn is in the centre.</summary>
    private bool BackfillDistricts()
    {
        var changed = false;
        if (state.State.Districts.All(d => d.Id != Districts.Centre))
        {
            state.State.Districts.Insert(0, Districts.Seed());
            changed = true;
        }
        foreach (var place in state.State.Places.Where(p => p.DistrictId is null && p.Kind != PlaceKinds.Locale))
        {
            place.DistrictId = Districts.Centre;
            changed = true;
        }
        // Locales whose name says where they are ("… in Amsterdam-Zuid") go there.
        foreach (var locale in state.State.Places.Where(p => p is { DistrictId: null, Kind: PlaceKinds.Locale }))
            if (Districts.FromName(locale.Name) is { } said)
            {
                locale.DistrictId = DistrictFor(said);
                changed = true;
            }
        return changed;
    }

    /// <summary>Synchronous resolve-or-create, for activation backfills (saved by the caller).</summary>
    private string DistrictFor(string name)
    {
        var slug = Districts.Slug(name);
        if (slug.Length == 0) return Districts.Centre;
        if (state.State.Districts.FirstOrDefault(d => Districts.Same(d.Id, slug)) is { } known) return known.Id;
        state.State.Districts.Add(new District { Id = slug, Name = WorldStream.Clip(name.Trim(), 40) });
        return slug;
    }
}
