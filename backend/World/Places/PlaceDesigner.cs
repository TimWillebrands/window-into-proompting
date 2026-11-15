using System.Text.Json.Nodes;
using Orleans.Concurrency;

namespace PartyTown.World.Places;

[GenerateSerializer, Alias("PartyTown.World.Places.PlaceDraft")]
public sealed record PlaceDraft([property: Id(0)] string Json, [property: Id(1)] string Scene, [property: Id(2)] List<Spot> Spots, [property: Id(3)] List<Feature> Features)
{
    public JsonObject Doc => JsonNode.Parse(Json) as JsonObject ?? new JsonObject();
}

[StatelessWorker]
public sealed class PlaceDesignerGrain(IWorldLlm llm, ILogger<PlaceDesignerGrain> logger) : Grain, IPlaceDesignerGrain
{
    public Task<PlaceDraft?> DesignAsync(string brief, bool outdoor, List<string> placesNow, bool home)
        => PlaceDesign.DesignAsync(llm, logger, brief, outdoor, placesNow, home ? PlaceDesign.MissingHomeSpots : null);
}

[Alias("PartyTown.World.Places.IPlaceDesignerGrain")]
public interface IPlaceDesignerGrain : IGrainWithIntegerKey
{
    [Alias("DesignAsync")] Task<PlaceDraft?> DesignAsync(string brief, bool outdoor, List<string> placesNow, bool home);
}

public static class PlaceMaking
{
    public static async Task<Place?> CreatePlaceAsync(IGrainFactory grains, Guid partyId, Guid? madeBy, string text, bool outdoor)
    {
        var places = grains.GetGrain<IPlacesGrain>(partyId);
        var names = PublicNames(await places.GetSnapshotAsync());
        var draft = await Designer(grains).DesignAsync(WorldStream.Clip(text, 400), outdoor, names, home: false);
        return draft is null ? null : await places.CreatePlaceAsync(madeBy, text, draft);
    }

    public static async Task<Place?> RedesignAsync(IGrainFactory grains, Guid partyId, Guid placeId, string text, string? name, bool outdoor)
    {
        var places = grains.GetGrain<IPlacesGrain>(partyId);
        var snapshot = await places.GetSnapshotAsync();
        if (snapshot.Places.FirstOrDefault(p => p.Id == placeId) is not { Kind: PlaceKinds.Home or PlaceKinds.Public } place) return null;
        var home = place.Kind == PlaceKinds.Home;
        var rules = home ? PlaceDesign.HomeRules(Math.Max(1, place.Living().Count()))
            : place.Purpose is { Length: > 0 } purpose ? $"It stays {place.Name}: {purpose}." : $"It stays {place.Name}.";
        var others = PublicNames(snapshot).Where(n => n != place.Name).ToList();
        var draft = await Designer(grains).DesignAsync($"{WorldStream.Clip(text, 1500)}\n{rules}", outdoor, others, home);
        return draft is null ? null : await places.RedesignPlaceAsync(placeId, draft, name);
    }

    private static IPlaceDesignerGrain Designer(IGrainFactory grains) => grains.GetGrain<IPlaceDesignerGrain>(0);

    private static List<string> PublicNames(PlacesSnapshot snapshot)
        => snapshot.Places.Where(p => p.Kind == PlaceKinds.Public).Select(p => p.Name).ToList();
}
