using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PartyTown.World.Places;

public sealed record Fare(string Key, double Feeds, int Cost, string? SpotId, bool Cooked, string Where);

public static class Sustenance
{
    public const string Cook = "cook";
    public const string Order = "order";
    public const string ThingPrefix = "thing:";

    public const int MealCosts = 9;
    public const int LightMealCosts = 6;
    public const int SnackCosts = 3;

    private static readonly string[] MealNames =
    {
        "restaurant", "diner", "eatery", "pizzeria", "bistro", "brasserie", "trattoria", "ramen", "sushi", "canteen", "kantine", "snackbar",
    };

    private static readonly string[] LightMealNames = { "café", "cafe", "bakery", "bakkerij", "lunchroom", "tearoom", "patisserie", "koffiehuis" };

    private static readonly string[] SnackNames = { "bar", "pub", "kroeg", "brewery", "brouwerij", "biergarten" };

    private static readonly string[] MealPurposes = { "go out to eat", "out to eat", "get something to eat", "get lunch", "get dinner" };

    private static readonly string[] LightMealPurposes = { "get coffee", "get a coffee", "get breakfast" };

    private static readonly string[] SnackPurposes = { "go for a drink", "for a drink", "for drinks", "have a drink" };

    public static string Meal(DateTimeOffset local) => local.Hour switch
    {
        < 5 => "a late bite",
        < 11 => "breakfast",
        < 15 => "lunch",
        < 17 => "something to eat",
        < 22 => "dinner",
        _ => "a late bite",
    };

    public static double? Sells(Place place)
    {
        if (place.Kind != PlaceKinds.Public) return null;
        switch (place.Archetype)
        {
            case "restaurant": return Self.Bio.Meal;
            case "cafe": return Self.Bio.LightMeal;
            case "bar": return Self.Bio.Snack;
            case null or "place": break;
            default: return null;
        }
        var name = Words(place.Name);
        var purpose = Words(place.Purpose);
        bool Named(string[] any) => any.Any(w => name.Contains($" {w} ", StringComparison.Ordinal));
        bool For(string[] any) => any.Any(w => purpose.Contains($" {w} ", StringComparison.Ordinal));
        return Named(MealNames) || For(MealPurposes) ? Self.Bio.Meal
            : Named(LightMealNames) || For(LightMealPurposes) ? Self.Bio.LightMeal
            : Named(SnackNames) || For(SnackPurposes) ? Self.Bio.Snack
            : null;
    }

    private static string Words(string? text) => $" {Regex.Replace((text ?? string.Empty).ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim()} ";

    public static List<Fare> Of(Place place, Guid who)
    {
        var fares = new List<Fare>();
        if (place.Kind == PlaceKinds.Locale) return fares;
        var things = Things(place);
        var kitchen = things.FirstOrDefault(t => t.Feed.Cooks).SpotId is { } pinned ? place.Spots.FirstOrDefault(s => s.Id == pinned) : null;
        kitchen ??= place.Spots.FirstOrDefault(s => s.Id == "kitchen");
        var sells = Sells(place);
        var cooksHere = place.LivesIn(who) || (place.Kind == PlaceKinds.Public && sells is null && things.Any(t => t.Feed.Cooks));
        if (cooksHere)
            fares.Add(new Fare(Cook, Self.Bio.Meal, 0, kitchen?.Id, true, kitchen is null ? PlacesGrain.AtName(place, who) : $"{kitchen.At} {kitchen.Name}"));
        if (sells is { } sold)
        {
            var counter = place.Spots.FirstOrDefault(s => s.Staff);
            fares.Add(new Fare(Order, sold, sold >= Self.Bio.Meal ? MealCosts : sold >= Self.Bio.LightMeal ? LightMealCosts : SnackCosts, null, false,
                counter is null ? PlacesGrain.AtName(place, who) : $"{counter.At} {counter.Name}"));
        }
        foreach (var (furni, feed, _) in things.Where(t => !t.Feed.Cooks && t.Furni is not null))
            fares.Add(new Fare($"{ThingPrefix}{furni!.Id}", feed.Amount, 0, furni.SpotId, false, $"at {furni.Name}"));
        return fares;
    }

    public static Fare? Best(IEnumerable<Fare> fares, int balance)
        => fares.Where(f => f.Cost <= balance).OrderByDescending(f => f.Feeds).ThenBy(f => f.Cost).FirstOrDefault();

    public static Feed? Feeds(Place place, Furni furni)
        => Things(place).FirstOrDefault(t => t.Furni?.Id == furni.Id).Feed is { Cooks: false } feed ? feed : null;

    public static string Label(Fare fare, DateTimeOffset local, IReadOnlyList<string> company, string? since = null)
    {
        var meal = Meal(local);
        var what = fare.Cooked ? company.Count > 0 ? $"Make {meal} for you and {And(company)}, {fare.Where}" : $"Make yourself {meal} {fare.Where}"
            : fare.Key != Order ? $"Get something to eat {fare.Where}"
            : fare.Feeds >= Self.Bio.LightMeal ? $"Order {meal} {fare.Where}"
            : $"Get something small to eat {fare.Where}";
        var notes = new[] { fare.Cost > 0 ? Credits(fare.Cost) : null, since }.OfType<string>().ToList();
        return notes.Count == 0 ? what : $"{what} ({string.Join("; ", notes)})";
    }

    public static string Summary(Fare fare, Place place, Guid who, DateTimeOffset local, IReadOnlyList<string> company)
    {
        var meal = Meal(local);
        var at = PlacesGrain.AtName(place, who);
        if (fare.Cooked) return $"makes {meal} {at}{(company.Count > 0 ? $", for {And(company)} too" : string.Empty)}";
        if (fare.Key == Order)
            return $"has {(fare.Feeds >= Self.Bio.LightMeal ? meal : "something small to eat")} {at} ({Credits(fare.Cost)})";
        return $"gets something to eat {fare.Where} {place.At} {place.Name}{(fare.Cost > 0 ? $" ({Credits(fare.Cost)})" : string.Empty)}";
    }

    public static string Busy(Fare fare, DateTimeOffset local)
        => fare.Cooked ? $"making {Meal(local)}" : fare.Key == Order && fare.Feeds >= Self.Bio.LightMeal ? $"having {Meal(local)}" : "eating";

    public static string Why(Fare fare) => fare.Cooked ? "to make something to eat" : "to get something to eat";

    private static string Credits(int cost) => cost == 1 ? "1 credit" : $"{cost} credits";

    private static string And(IReadOnlyList<string> names)
    {
        var first = names.Select(n => n.Split(' ')[0]).ToList();
        return first.Count == 1 ? first[0] : $"{string.Join(", ", first.SkipLast(1))} and {first[^1]}";
    }

    private static List<(Furni? Furni, Feed Feed, string? SpotId)> Things(Place place)
    {
        var found = new List<(Furni?, Feed, string?)>();
        var scene = Scene(place.CustomScene);
        var pins = scene?["furni"] as JsonObject;
        foreach (var furni in place.Furni)
        {
            var prefab = furni.Prefab ?? pins?[furni.Id]?["p"]?.ToString()
                ?? (furni.Do is { } what ? Library.Offers.FirstOrDefault(o => o.Value.Do == what && Library.Feeds.ContainsKey(o.Key)).Key : null);
            if (prefab is not null && Library.Feeds.TryGetValue(prefab, out var feed)) found.Add((furni, feed, furni.SpotId));
        }
        foreach (var prop in (scene?["props"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            if (prop["p"]?.ToString() is { } name && Library.Feeds.TryGetValue(name, out var feed) && feed.Cooks)
                found.Add((null, feed, prop["spot"]?.ToString()));
        return found;
    }

    private static JsonObject? Scene(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonNode.Parse(json) as JsonObject; }
        catch (System.Text.Json.JsonException) { return null; }
    }
}
