namespace PartyTown.World.Places;

/// <summary>How a Body looks: named picks from fixed palettes, plus what an LLM designed beyond them (ADR 0028).</summary>
[GenerateSerializer, Alias("PartyTown.World.Places.Appearance")]
public sealed class Appearance
{
    [Id(0)] public string Skin { get; set; } = "light";
    [Id(1)] public string Hair { get; set; } = "brown";
    [Id(2)] public string HairStyle { get; set; } = "short";
    [Id(3)] public string Top { get; set; } = "red";
    [Id(4)] public string Bottom { get; set; } = "navy";
    [Id(5)] public string Accessory { get; set; } = "none";
    /// <summary>Dressed by the Persona from their bio (vs. the stable default).</summary>
    [Id(6)] public bool FromBio { get; set; }
    [Id(7)] public string TopStyle { get; set; } = "tshirt";
    [Id(8)] public string BottomStyle { get; set; } = "trousers";
    [Id(9)] public string Shoes { get; set; } = "black";
    [Id(10)] public string FacialHair { get; set; } = "none";
    [Id(11)] public string Build { get; set; } = "average";
    [Id(12)] public string Height { get; set; } = "average";
    [Id(13)] public string Eyes { get; set; } = "dark";
    [Id(14)] public string ShoeStyle { get; set; } = "sneakers";
    [Id(15)] public string Legwear { get; set; } = "none";
    /// <summary>The Figure as designed, slot by slot over the picks (<see cref="FigureDesign"/>).</summary>
    [Id(16)] public string? Design { get; set; }
    /// <summary>One plain line of what you'd notice; gone once a pick outdates it.</summary>
    [Id(17)] public string? Summary { get; set; }
    [Id(18)] public string BodyType { get; set; } = "straight";
    /// <summary>A Player's own look: going on auto doesn't get them dressed from their bio.</summary>
    [Id(19)] public bool Kept { get; set; }
}

/// <summary>One choosable part of a look, for editors and prompts.</summary>
[GenerateSerializer, Alias("PartyTown.World.Places.AppearanceField")]
public sealed record AppearanceField([property: Id(0)] string Key, [property: Id(1)] string Label, [property: Id(2)] string[] Options);

public static class AppearanceRules
{
    public static readonly string[] Skins = { "light", "fair", "olive", "tan", "brown", "dark" };
    public static readonly string[] Hairs = { "black", "brown", "blonde", "red", "grey", "white", "blue", "pink", "green", "purple" };
    public static readonly string[] Styles = { "short", "long", "curly", "bun", "bald", "mohawk", "ponytail", "afro", "braids", "buzz", "bob", "spiky", "fringe", "scene", "pigtails", "undercut", "dreads" };
    public static readonly string[] Colours = { "red", "orange", "yellow", "green", "teal", "blue", "navy", "purple", "pink", "black", "white", "grey", "brown", "beige", "olive", "maroon" };
    public static readonly string[] Accessories = { "none", "glasses", "sunglasses", "hat", "cap", "beanie", "scarf", "headphones", "earrings", "flower", "bow", "necklace", "bandana", "choker", "studded-belt", "wristbands", "arm-warmers", "wallet-chain" };
    public static readonly string[] TopStyles = { "tshirt", "hoodie", "shirt", "sweater", "jacket", "dress", "tank", "suit", "baby-tee", "band-tee", "long-sleeve", "mesh", "corset", "flannel", "tracksuit" };
    public static readonly string[] BottomStyles = { "trousers", "jeans", "shorts", "skirt", "skinny-jeans", "baggy-jeans", "bootcut", "cargo", "plaid-skirt", "mini-skirt" };
    public static readonly string[] ShoeStyles = { "sneakers", "skate-shoes", "hi-tops", "boots", "platforms", "heels", "sandals", "loafers" };
    public static readonly string[] Legwears = { "none", "knee-socks", "striped-socks", "tights", "fishnets" };
    public static readonly string[] FacialHairs = { "none", "stubble", "beard", "moustache" };
    public static readonly string[] Builds = { "slim", "average", "broad" };
    public static readonly string[] BodyTypes = { "straight", "athletic", "lanky", "stocky", "heavyset", "pear", "hourglass" };
    public static readonly string[] Heights = { "short", "average", "tall" };
    public static readonly string[] EyeColours = { "dark", "brown", "blue", "green", "grey" };

    public static readonly AppearanceField[] Fields =
    {
        new("skin", "Skin", Skins),
        new("hair", "Hair", Hairs),
        new("hairStyle", "Hairstyle", Styles),
        new("eyes", "Eyes", EyeColours),
        new("facialHair", "Facial hair", FacialHairs),
        new("topStyle", "Top", TopStyles),
        new("top", "Top colour", Colours),
        new("bottomStyle", "Bottom", BottomStyles),
        new("bottom", "Bottom colour", Colours),
        new("shoeStyle", "Shoes", ShoeStyles),
        new("shoes", "Shoe colour", Colours),
        new("legwear", "Legwear", Legwears),
        new("accessory", "Extra", Accessories),
        new("bodyType", "Body type", BodyTypes),
        new("build", "Build", Builds),
        new("height", "Height", Heights),
    };

    /// <summary>A stable look from the id until (or unless) the Persona dresses themselves.</summary>
    public static Appearance Default(Guid id)
    {
        var b = id.ToByteArray();
        return new Appearance
        {
            Skin = Skins[b[2] % Skins.Length],
            Hair = Hairs[b[3] % 5],
            HairStyle = Styles[b[4] % 12],
            Top = Colours[b[5] % Colours.Length],
            Bottom = Colours[6 + b[6] % 7],
            Accessory = b[7] % 3 == 0 ? Accessories[1 + b[8] % 12] : "none",
            TopStyle = TopStyles[b[9] % 5],
            BottomStyle = BottomStyles[b[10] % 4],
            Shoes = new[] { "black", "brown", "white", "grey", "navy" }[b[11] % 5],
            Build = Builds[b[12] % Builds.Length],
            BodyType = BodyTypes[b[15] % BodyTypes.Length],
            Height = Heights[b[13] % Heights.Length],
            Eyes = EyeColours[b[14] % EyeColours.Length],
        };
    }

    public static string Get(Appearance a, string key) => key switch
    {
        "skin" => a.Skin,
        "hair" => a.Hair,
        "hairStyle" => a.HairStyle,
        "top" => a.Top,
        "bottom" => a.Bottom,
        "accessory" => a.Accessory,
        "topStyle" => a.TopStyle,
        "bottomStyle" => a.BottomStyle,
        "shoes" => a.Shoes,
        "shoeStyle" => a.ShoeStyle,
        "legwear" => a.Legwear,
        "facialHair" => a.FacialHair,
        "build" => a.Build,
        "bodyType" => a.BodyType,
        "height" => a.Height,
        "eyes" => a.Eyes,
        _ => string.Empty,
    };

    /// <summary>Accept choices only where they're on the palette; keep <paramref name="fallback"/>'s otherwise.</summary>
    public static Appearance Merge(Appearance fallback, IReadOnlyDictionary<string, string?> picked, bool fromBio = true)
    {
        string Pick(string key)
        {
            var options = Fields.First(f => f.Key == key).Options;
            return picked.TryGetValue(key, out var v) && v is not null && options.Contains(v.Trim().ToLowerInvariant())
                ? v.Trim().ToLowerInvariant()
                : Get(fallback, key);
        }
        var merged = new Appearance
        {
            Skin = Pick("skin"),
            Hair = Pick("hair"),
            HairStyle = Pick("hairStyle"),
            Top = Pick("top"),
            Bottom = Pick("bottom"),
            Accessory = Pick("accessory"),
            TopStyle = Pick("topStyle"),
            BottomStyle = Pick("bottomStyle"),
            Shoes = Pick("shoes"),
            FacialHair = Pick("facialHair"),
            Build = Pick("build"),
            BodyType = Pick("bodyType"),
            Height = Pick("height"),
            Eyes = Pick("eyes"),
            ShoeStyle = Pick("shoeStyle"),
            Legwear = Pick("legwear"),
            FromBio = fromBio || fallback.FromBio,
            Kept = fallback.Kept,
        };
        var changed = Fields.Select(f => f.Key).Where(k => Get(merged, k) != Get(fallback, k)).ToList();
        merged.Design = FigureDesign.Without(fallback.Design, changed);
        merged.Summary = changed.Count == 0 ? fallback.Summary : null;
        return merged;
    }

    /// <summary>What changed, in words: "pink hair, a hoodie".</summary>
    public static string Changes(Appearance before, Appearance after)
    {
        var parts = new List<string>();
        foreach (var f in Fields)
        {
            var (a, b) = (Get(before, f.Key), Get(after, f.Key));
            if (a == b) continue;
            parts.Add(f.Key switch
            {
                "hair" => $"{b} hair",
                "hairStyle" => $"{b} hairstyle",
                "topStyle" => b == "dress" ? "a dress" : $"a {b}",
                "top" => $"a {b} top",
                "bottomStyle" => b,
                "bottom" => $"{b} {(after.BottomStyle == "skirt" ? "skirt" : "bottoms")}",
                "shoes" => $"{b} shoes",
                "shoeStyle" => b,
                "legwear" => b == "none" ? "bare legs" : b,
                "accessory" => b == "none" ? $"no more {a}" : b,
                "facialHair" => b == "none" ? "clean-shaven" : $"a {b}",
                _ => $"{f.Label.ToLowerInvariant()} {b}",
            });
        }
        return string.Join(", ", parts);
    }

    public static string Describe(Appearance a)
        => string.Join(", ", Fields.Select(f => $"{f.Key}: {Get(a, f.Key)}"));
}
