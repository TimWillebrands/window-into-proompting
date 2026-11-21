namespace PartyTown.World.Places;

public sealed partial class PlacesGrain
{
    private readonly Dictionary<Guid, long> _fareMissed = new();
    private bool _faring;

    private bool FareDue(Place place)
        => Fares.Due(place) && !(_fareMissed.TryGetValue(place.Id, out var at) && Now() - at < Fares.RetryAfter.TotalMilliseconds);

    private void SettleFaresSoon()
    {
        if (_faring || !state.State.Places.Any(FareDue)) return;
        _faring = true;
        _ = SettleFaresAsync();
    }

    private async Task SettleFaresAsync()
    {
        try
        {
            while (state.State.Places.FirstOrDefault(FareDue) is { } place && !await StoppedAsync())
            {
                var signature = Fares.For(place);
                if (Fares.OfKit(place) is { } kit) place.Fare = kit;
                else if (await JudgeFareAsync(place) is var (fare, p))
                {
                    logger.LogInformation("Fare {Place}: {Fare} (p={P:F2})", place.Name, fare, p);
                    place.Fare = fare;
                }
                else
                {
                    _fareMissed[place.Id] = Now();
                    continue;
                }
                place.FareFor = signature;
                if (!state.State.Places.Contains(place)) continue;
                await SaveAsync();
                await PublishPlaceAsync(place);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Settling what Places sell failed");
        }
        finally
        {
            _faring = false;
        }
    }

    private async Task<(string Fare, double P)?> JudgeFareAsync(Place place)
    {
        try
        {
            return Fares.Read(await judge.AskAsync(Fares.State(place), Fares.Question(place), CancellationToken.None));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Judging what {Place} sells failed; trying again later", place.Name);
            return null;
        }
    }
}
