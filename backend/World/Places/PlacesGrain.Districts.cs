namespace PartyTown.World.Places;

/// <summary>Districts (ADR 0027): parts of the city; crossing between them is a tram ride.</summary>
public sealed partial class PlacesGrain
{
    private static readonly TimeSpan BetweenDistricts = TimeSpan.FromMinutes(4);

    public async Task<string> DistrictAsync(string name)
    {
        var slug = Districts.Slug(name);
        if (slug.Length == 0) return Districts.Centre;
        if (state.State.Districts.FirstOrDefault(d => Districts.Same(d.Id, slug)) is { } known) return known.Id;
        var trimmed = WorldStream.Clip(name.Trim(), 40);
        state.State.Districts.Add(new District { Id = slug, Name = trimmed });
        await SaveAsync();
        return slug;
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
        return changed;
    }
}
