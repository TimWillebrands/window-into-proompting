using System.Text.Json;
using System.Text.Json.Nodes;

namespace PartyTown.World.Places;

[GenerateSerializer, Alias("PartyTown.World.Places.Offer")]
public sealed record Offer([property: Id(0)] string Do, [property: Id(1)] string Gives, [property: Id(2)] bool Leaves, [property: Id(3)] double? Feeds = null);

public sealed record Feed(double Amount, bool Cooks);

public static class Library
{
    private static readonly Lazy<JsonObject> Doc = new(() =>
    {
        using var s = typeof(Library).Assembly.GetManifestResourceStream("PartyTown.World.Places.library.json")!;
        return JsonNode.Parse(s)!.AsObject();
    });

    private static readonly Lazy<Dictionary<string, string>> Colours = new(()
        => Doc.Value["palette"]!.AsObject().ToDictionary(p => p.Key, p => p.Value!.ToString()));

    private static readonly Lazy<Dictionary<string, JsonObject>> ByName = new(()
        => Doc.Value["models"]!.AsArray().OfType<JsonObject>().ToDictionary(m => m["name"]!.ToString()));

    private static readonly Lazy<Dictionary<string, Offer>> Offered = new(() =>
    {
        var offers = new Dictionary<string, Offer>();
        foreach (var (name, v) in Doc.Value["defaults"]!.AsObject())
            if (OfferOf(v as JsonObject) is { } o) offers[name] = o;
        foreach (var (name, m) in ByName.Value)
            if (OfferOf(m) is { } o) offers[name] = o;
        return offers;
    });

    private static readonly Lazy<Dictionary<string, Feed>> Fed = new(()
        => (Doc.Value["feeds"] as JsonObject ?? new JsonObject()).Where(f => f.Value is JsonObject).ToDictionary(f => f.Key, f => new Feed(
            f.Value!["feeds"]?.GetValue<double>() ?? 0,
            f.Value["cooks"]?.GetValueKind() == JsonValueKind.True)));

    public static IReadOnlyDictionary<string, string> Palette => Colours.Value;

    public static IReadOnlyDictionary<string, Feed> Feeds => Fed.Value;

    public static IReadOnlyDictionary<string, JsonObject> Models => ByName.Value;

    public static IReadOnlyDictionary<string, Offer> Offers => Offered.Value;

    public static IEnumerable<(string Group, List<string> Names)> Groups()
    {
        foreach (var (group, names) in Doc.Value["groups"]!.AsObject())
            yield return (group, names!.AsArray().Select(n => n!.ToString()).ToList());
        foreach (var g in ByName.Value.GroupBy(m => m.Value["group"]?.ToString() ?? "more"))
            yield return (g.Key, g.Select(m => m.Key).ToList());
    }

    public static IEnumerable<PrefabInfo> Infos()
        => ByName.Value.Values.Select(m => new PrefabInfo(
            m["name"]!.ToString(),
            m["size"] is JsonArray { Count: 2 } s ? new[] { (int)s[0]!, (int)s[1]! } : new[] { 1, 1 },
            m["solid"]?.GetValueKind() != JsonValueKind.False && m["wall"]?.GetValueKind() != JsonValueKind.True,
            m["wall"]?.GetValueKind() == JsonValueKind.True,
            (m["seats"] as JsonArray)?.Count ?? 0,
            m["about"]?.ToString() ?? string.Empty));

    public static Offer? OfferOf(JsonObject? o)
        => o?["do"]?.ToString() is { Length: > 2 } what
            ? new Offer(WorldStream.Clip(what.Trim().TrimEnd('.'), 80), WorldStream.Clip(o["gives"]?.ToString() ?? "one small, specific thing they notice or find", 200),
                o["leaves"]?.GetValueKind() == JsonValueKind.True, FeedsOf(o["feeds"]))
            : null;

    public static double? FeedsOf(JsonNode? feeds) => feeds?.GetValueKind() == JsonValueKind.String ? feeds.ToString().Trim().ToLowerInvariant() switch
    {
        "snack" => Self.Bio.Snack,
        "meal" => Self.Bio.Meal,
        _ => null,
    } : null;
}
