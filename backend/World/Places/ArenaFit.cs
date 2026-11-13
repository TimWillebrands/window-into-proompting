using System.Text.Json.Nodes;
using PartyTown.Services.SystemOne;

namespace PartyTown.World.Places;

/// <summary>How a Place lands on whoever's in it (ADR 0057): its feel in plain facts, the fit System One reads off it, and what that does to staying.</summary>
public static class ArenaFit
{
    public const string Key = "arena";
    /// <summary>Things named in the feel; the rest is counted.</summary>
    private const int Named = 5;
    private const int BareAt = 3;
    private static readonly TimeSpan Usual = TimeSpan.FromMinutes(45);

    private static readonly IReadOnlyDictionary<string, string?> Moods = new Dictionary<string, string?>
    {
        ["better"] = "it lifts their mood",
        ["same"] = "it doesn't do much either way",
        ["worse"] = "it wears on them",
    };

    private static readonly IReadOnlyDictionary<string, string> Lights = new Dictionary<string, string>
    {
        ["bright"] = "Bright light", ["warm"] = "Warm light", ["dim"] = "Dim light",
        ["candle"] = "Candlelight", ["neon"] = "Neon light", ["cold"] = "Cold light",
    };

    private static readonly IReadOnlyDictionary<string, string> Airs = new Dictionary<string, string>
    {
        ["dust"] = "dust hanging in the air", ["fireflies"] = "fireflies about", ["snow"] = "snow falling", ["rain"] = "rain coming down",
        ["leaves"] = "leaves drifting", ["petals"] = "petals drifting", ["embers"] = "embers drifting up", ["bubbles"] = "bubbles floating",
        ["confetti"] = "confetti everywhere",
    };

    /// <summary>The Place as it strikes you, facts only: light, air and what's in it; null when there's nothing to say.</summary>
    public static string? Feel(Place place)
    {
        var scene = Scene(place.CustomScene);
        var mood = string.Join(", ", new[]
        {
            scene?["light"]?.ToString() is { } l && Lights.TryGetValue(l, out var light) ? light : null,
            scene?["ambience"]?.ToString() is { } a && Airs.TryGetValue(a, out var air) ? air : null,
        }.OfType<string>());
        if (mood.Length > 0 && !char.IsUpper(mood[0])) mood = char.ToUpperInvariant(mood[0]) + mood[1..];

        // To-do and decor already have their own lines; they still count toward how full it is.
        var props = (scene?["props"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Concat((scene?["furni"] as JsonObject ?? new JsonObject()).Select(f => f.Value).OfType<JsonObject>()).ToList();
        var names = place.Furni.Where(f => f.Do is null && f.Kind != FurniKinds.Decor).Select(f => f.Name)
            .Concat(props.Where(p => p["do"] is null).Select(PropName))
            .Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var count = place.Furni.Count + props.Count;
        var shown = names.Take(Named).ToList();
        var things = count == 0 ? "Nothing much in it."
            : shown.Count == 0 ? null
            : count <= BareAt ? $"Bare, apart from {And(shown)}."
            : $"In it: {And(shown)}{(count > shown.Count ? ", and more" : string.Empty)}.";
        var feel = string.Join(" ", new[] { mood.Length > 0 ? mood + "." : null, things }.OfType<string>());
        return feel.Length == 0 ? null : feel;
    }

    /// <summary>What's changed since they last took it in: what's there, and what's gone wrong with it.</summary>
    public static string Signature(Place place, long now)
        => $"{place.Id:N}:{place.Furni.Count}:{place.Furni.Count(f => f.Kind == FurniKinds.Decor)}:{place.Furni.Count(f => FurniRules.Notable(f, now) is not null)}:{place.CustomScene?.Length ?? 0}";

    public static ChoiceQuestion Question(string name)
        => new($"Being here, the way this place is, how does it sit with {name}, being who they are?", Moods);

    /// <summary>Expected surroundings pull less: new 1, a week of daily visits 1/8; a known room that changed is half new.</summary>
    public static double Novelty(int daysKnown, bool changed) => Math.Max(changed ? 0.5 : 0, 1.0 / (1 + Math.Max(0, daysKnown)));

    /// <summary>The appraisal (p better), damped by how expected the place is.</summary>
    public static double Felt(double better, double novelty) => 0.5 + (better - 0.5) * Math.Clamp(novelty, 0, 1);

    /// <summary>How long before getting out pulls: about 20 min where it wears on you, 90 where it lifts you.</summary>
    public static TimeSpan Restless(double? fit) => Usual * Math.Pow(2, (Math.Clamp(fit ?? 0.5, 0, 1) - 0.5) * 2);

    private static string PropName(JsonObject prop)
        => prop["name"]?.ToString() is { Length: > 0 } named ? named : (prop["p"]?.ToString() ?? string.Empty).Replace('-', ' ');

    private static string And(IReadOnlyList<string> items)
        => items.Count <= 1 ? string.Join(string.Empty, items) : $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}";

    private static JsonObject? Scene(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonNode.Parse(json) as JsonObject; }
        catch (System.Text.Json.JsonException) { return null; }
    }
}

/// <summary>How a Place last sat with someone: p(it lifts them), undamped.</summary>
public sealed record PlaceFit(Guid PlaceId, double Better);
