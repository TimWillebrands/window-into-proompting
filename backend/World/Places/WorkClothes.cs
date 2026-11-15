using System.Text.Json.Nodes;

namespace PartyTown.World.Places;

[GenerateSerializer, Alias("PartyTown.World.Places.WorkClothes")]
public sealed class WorkClothes
{
    [Id(0)] public bool OwnClothes { get; set; }
    [Id(1)] public Dictionary<string, string> Picks { get; set; } = new();
    [Id(2)] public string? Design { get; set; }
    [Id(3)] public string? Summary { get; set; }
    [Id(4)] public string For { get; set; } = string.Empty;
    [Id(5)] public int Made { get; set; }
}

public static class WorkClothesRules
{
    public static readonly string[] GarmentPicks = { "topStyle", "top", "bottomStyle", "bottom", "shoeStyle", "shoes", "legwear", "accessory" };
    public static readonly string[] GarmentSlots = { "top", "under", "outer", "bottom", "legwear", "shoes", "extras" };

    public const int Made = 3;

    public static bool Fits(WorkClothes? clothes, string? title)
        => clothes is not null && clothes.Made == Made && string.Equals(clothes.For.Trim(), title?.Trim(), StringComparison.OrdinalIgnoreCase);

    public static WorkClothes Clean(JsonObject answer, string title)
    {
        var own = answer["code"]?.ToString().Trim().ToLowerInvariant() is "none" or "own";
        var picks = new Dictionary<string, string>();
        if (answer["picks"] is JsonObject raw)
            foreach (var key in GarmentPicks)
                if (raw[key]?.ToString().Trim().ToLowerInvariant() is { Length: > 0 } v && AppearanceRules.Fields.First(f => f.Key == key).Options.Contains(v))
                    picks[key] = v;
        var design = Garments(FigureDesign.Clean(answer["design"]));
        var summary = answer["summary"]?.ToString().Trim() is { Length: > 0 } s ? WorldStream.Clip(s, 160) : null;
        if (own || (picks.Count == 0 && design is null)) return new WorkClothes { OwnClothes = true, For = title, Made = Made };
        return new WorkClothes { Picks = picks, Design = design, Summary = summary, For = title, Made = Made };
    }

    public static string? Garments(string? design)
    {
        if (string.IsNullOrWhiteSpace(design) || JsonNode.Parse(design) is not JsonObject doc) return null;
        foreach (var key in doc.Select(kv => kv.Key).Where(k => !GarmentSlots.Contains(k)).ToList()) doc.Remove(key);
        return doc.Count == 0 ? null : doc.ToJsonString();
    }

    public static Appearance Wear(Appearance own, WorkClothes clothes)
    {
        if (clothes.OwnClothes) return own.Copy();
        var look = AppearanceRules.Merge(own, clothes.Picks.ToDictionary(p => p.Key, p => (string?)p.Value), fromBio: false);
        if (clothes.Design is { } work && JsonNode.Parse(work) is JsonObject patch)
        {
            var extras = new JsonArray();
            var mine = look.Design is { } d && JsonNode.Parse(d) is JsonObject current ? current["extras"] as JsonArray : null;
            foreach (var e in (mine ?? new JsonArray()).Concat(patch["extras"] as JsonArray ?? new JsonArray()).Take(FigureDesign.MaxExtras))
                extras.Add(e?.DeepClone());
            patch.Remove("extras");
            look.Design = FigureDesign.Over(look.Design, patch.Count == 0 ? null : patch.ToJsonString());
            if (extras.Count > 0) look.Design = FigureDesign.Over(look.Design, new JsonObject { ["extras"] = extras }.ToJsonString());
        }
        look.Summary = clothes.Summary ?? look.Summary;
        return look;
    }
}
