namespace PartyTown.World.Places;

public sealed partial class PlacesGrain
{
    public async Task<bool> LosePowerAsync(Guid placeId)
    {
        await SettleAsync();
        if (Find(placeId) is not { } place || place.PowerLost) return false;
        place.PowerLost = true;
        await SaveAsync();
        await PublishPlaceAsync(place);
        _ = ClockSignalsAsync(place, place.Id, "power-lost", $"{place.Name} has lost power", null, null, Now());
        return true;
    }

    public async Task<bool> RestorePowerAsync(Guid placeId)
    {
        await SettleAsync();
        if (Find(placeId) is not { } place || !place.PowerLost) return false;
        place.PowerLost = false;
        await SaveAsync();
        await PublishPlaceAsync(place);
        _ = ClockSignalsAsync(place, place.Id, "power-restored", $"{place.Name} has its power back", null, null, Now());
        return true;
    }
}
