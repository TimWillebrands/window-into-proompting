using System.Text.Json.Nodes;
using PartyTown.World.Places;
using PartyTown.World.Self;

namespace BackendTest;

public partial class PlacesTest
{
    private static PlaceDraft Draft(string name, string purpose)
    {
        var json = (JsonObject)JsonNode.Parse(PlaceDesign.IndoorExample)!;
        json["name"] = name;
        json["purpose"] = purpose;
        return PlaceDesign.Inspect(json, outdoor: false).Draft!;
    }

    private async Task<Place> FareSettledAsync(Guid partyId, Guid placeId, Func<Place, bool> done)
    {
        var places = _fx.Places(partyId);
        for (var i = 0; i < 100; i++)
        {
            var place = (await places.GetSnapshotAsync()).Places.Single(p => p.Id == placeId);
            if (done(place)) return place;
            await Task.Delay(20);
        }
        return (await places.GetSnapshotAsync()).Places.Single(p => p.Id == placeId);
    }

    [Fact]
    public async Task AMadePublicPlace_GetsWhatItSellsJudged_InTheBackground_AndARenameAsksAgain()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);

        var made = (await places.CreatePlaceAsync(denise, "a ramen counter", Draft("Tante Sjaan", "a ramen counter under the tracks")))!;

        var judged = await FareSettledAsync(partyId, made.Id, p => p.Fare is not null);
        Assert.Equal((Fares.Meal, "Tante Sjaan\na ramen counter under the tracks"), (judged.Fare, judged.FareFor));
        Assert.Equal(Bio.Meal, Sustenance.Sells(judged));
        Assert.Contains(ScriptedJudge.Instance.FaresAsked, s => s.StartsWith("A place in town: Tante Sjaan, a ramen counter under the tracks."));

        await places.ClaimAsync(made.Id, denise, "the Noodle Arch");
        await places.GetSnapshotAsync();

        var renamed = await FareSettledAsync(partyId, made.Id, p => p.FareFor?.StartsWith("the Noodle Arch") == true);
        Assert.Equal(Fares.Meal, renamed.Fare);
        Assert.Contains(ScriptedJudge.Instance.FaresAsked, s => s.StartsWith("A place in town: the Noodle Arch"));
    }

    [Fact]
    public async Task AKitRestaurant_SellsMeals_WithoutAskingTheJudge()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);

        var sora = (await places.OpenAsync(await places.LocaleAsync("Sora", denise), "restaurant", "author"))!;

        Assert.Equal(Fares.Meal, sora.Fare);
        Assert.Equal(Bio.Meal, Sustenance.Sells(sora));
        await places.GetSnapshotAsync();
        Assert.DoesNotContain(ScriptedJudge.Instance.FaresAsked, s => s.StartsWith("A place in town: Sora"));
    }

    [Fact]
    public async Task AHome_IsNeverJudged()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var made = (await places.CreatePlaceAsync(denise, "a ramen counter", Draft("the Ramen Hatch", "where people slurp ramen standing up")))!;
        await FareSettledAsync(partyId, made.Id, p => p.Fare is not null);

        var home = (await places.GetSnapshotAsync()).Places.Single(p => p.OwnerId == denise);

        Assert.Null(home.Fare);
        Assert.DoesNotContain(ScriptedJudge.Instance.FaresAsked, s => s.StartsWith($"A place in town: {home.Name}"));
    }
}
