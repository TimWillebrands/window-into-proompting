namespace PartyTown.World.Places;

/// <summary>How a Body looks (spec phase 8). Named choices from fixed palettes, so pixel art stays coherent.</summary>
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
}

public static class AppearanceRules
{
    public static readonly string[] Skins = { "light", "fair", "olive", "tan", "brown", "dark" };
    public static readonly string[] Hairs = { "black", "brown", "blonde", "red", "grey", "white", "blue", "pink" };
    public static readonly string[] Styles = { "short", "long", "curly", "bun", "bald", "mohawk" };
    public static readonly string[] Colours = { "red", "orange", "yellow", "green", "teal", "blue", "navy", "purple", "pink", "black", "white", "grey", "brown" };
    public static readonly string[] Accessories = { "none", "glasses", "hat", "beanie", "scarf", "headphones" };

    /// <summary>A stable look from the id until (or unless) the Persona dresses themselves.</summary>
    public static Appearance Default(Guid id)
    {
        var b = id.ToByteArray();
        return new Appearance
        {
            Skin = Skins[b[2] % Skins.Length],
            Hair = Hairs[b[3] % 5],
            HairStyle = Styles[b[4] % Styles.Length],
            Top = Colours[b[5] % Colours.Length],
            Bottom = Colours[6 + b[6] % 7],
            Accessory = b[7] % 3 == 0 ? Accessories[1 + b[8] % (Accessories.Length - 1)] : "none",
        };
    }

    /// <summary>Accept a model's choices only where they're on the palette; keep the default otherwise.</summary>
    public static Appearance Merge(Appearance fallback, IReadOnlyDictionary<string, string?> picked)
    {
        string Pick(string key, string[] options, string current)
            => picked.TryGetValue(key, out var v) && v is not null && options.Contains(v.Trim().ToLowerInvariant())
                ? v.Trim().ToLowerInvariant()
                : current;
        return new Appearance
        {
            Skin = Pick("skin", Skins, fallback.Skin),
            Hair = Pick("hair", Hairs, fallback.Hair),
            HairStyle = Pick("hairStyle", Styles, fallback.HairStyle),
            Top = Pick("top", Colours, fallback.Top),
            Bottom = Pick("bottom", Colours, fallback.Bottom),
            Accessory = Pick("accessory", Accessories, fallback.Accessory),
            FromBio = true,
        };
    }

    public static string Prompt => $$"""
        Dress this person for a small pixel-art world, based on who they are. Choose ONE option per field, exactly as written.
        skin: {{string.Join(", ", Skins)}}
        hair: {{string.Join(", ", Hairs)}}
        hairStyle: {{string.Join(", ", Styles)}}
        top: {{string.Join(", ", Colours)}}
        bottom: {{string.Join(", ", Colours)}}
        accessory: {{string.Join(", ", Accessories)}}
        Output ONLY JSON: {"skin": "...", "hair": "...", "hairStyle": "...", "top": "...", "bottom": "...", "accessory": "..."}
        """;
}
