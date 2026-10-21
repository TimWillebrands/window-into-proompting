using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PartyTown.World.Places;

/// <summary>A generated 3D model for one thing (ADR 0023 prefab parts), cleaned so any model output draws sanely in one tile.</summary>
public static partial class ThingModel
{
    public const int MaxParts = 28;
    private static readonly string[] Shapes = { "box", "cyl", "cone", "ball", "gable", "ring", "sign" };

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex Hex();

    /// <summary>Normalised parts JSON, or null when nothing usable came back.</summary>
    public static string? Clean(JsonNode? raw)
    {
        var parts = raw switch
        {
            JsonArray a => a,
            JsonObject o when o["parts"] is JsonArray a => a,
            _ => null,
        };
        if (parts is null) return null;
        var wall = raw is JsonObject obj && obj["wall"]?.GetValueKind() == System.Text.Json.JsonValueKind.True;
        var clean = new JsonArray();
        foreach (var p in parts.OfType<JsonObject>().Take(MaxParts))
            if (CleanPart(p, wall) is { } c) clean.Add(c);
        if (clean.Count == 0) return null;
        return new JsonObject { ["wall"] = wall, ["parts"] = clean }.ToJsonString();
    }

    private static JsonObject? CleanPart(JsonObject p, bool wall)
    {
        var shape = Shapes.FirstOrDefault(p.ContainsKey);
        if (shape is null) return null;
        var o = new JsonObject();
        switch (shape)
        {
            case "box":
            case "gable":
                if (Nums(p[shape], 3) is not { } b) return null;
                o[shape] = Arr(Clamp(b[0], 0.01, 1), Clamp(b[1], 0.01, 2), Clamp(b[2], 0.01, 1));
                break;
            case "cyl":
                if (Nums(p[shape], 2) is not { } c) return null;
                o[shape] = c.Length >= 3
                    ? Arr(Clamp(c[0], 0.005, 0.5), Clamp(c[1], 0.005, 0.5), Clamp(c[2], 0.01, 2))
                    : Arr(Clamp(c[0], 0.005, 0.5), Clamp(c[1], 0.01, 2));
                o["sides"] = (int)Clamp(Num(p["sides"]) ?? 10, 3, 24);
                break;
            case "cone":
                if (Nums(p[shape], 2) is not { } k) return null;
                o[shape] = Arr(Clamp(k[0], 0.005, 0.5), Clamp(k[1], 0.01, 2));
                o["sides"] = (int)Clamp(Num(p["sides"]) ?? 10, 3, 24);
                break;
            case "ball":
                if (Num(p[shape]) is not { } r) return null;
                o[shape] = Clamp(r, 0.01, 0.5);
                if (Num(p["squash"]) is { } sq) o["squash"] = Clamp(sq, 0.15, 3);
                break;
            case "ring":
                if (Nums(p[shape], 2) is not { } g) return null;
                o[shape] = Arr(Clamp(g[0], 0.02, 0.5), Clamp(g[1], 0.005, 0.15));
                break;
            case "sign":
                if (Nums(p[shape], 2) is not { } s) return null;
                o[shape] = Arr(Clamp(s[0], 0.05, 1), Clamp(s[1], 0.05, 1));
                o["text"] = WorldStream.Clip(p["text"]?.ToString() ?? string.Empty, 24);
                o["fg"] = Colour(p["fg"], "#ffffff");
                break;
        }
        var at = Nums(p["at"], 3) ?? new double[] { 0, 0, 0 };
        o["at"] = Arr(Clamp(at[0], -0.5, 0.5), Clamp(at[1], wall ? 0.3 : -0.1, 2.3), Clamp(at[2], -0.5, 0.5));
        o["c"] = Colour(p["c"], "#999999");
        foreach (var axis in new[] { "rx", "ry", "rz" })
            if (Num(p[axis]) is { } a) o[axis] = Clamp(a, -Math.PI, Math.PI);
        if (p["glow"]?.GetValueKind() == System.Text.Json.JsonValueKind.True) o["glow"] = true;
        if (Num(p["opacity"]) is { } op && op < 1) o["opacity"] = Clamp(op, 0.2, 1);
        return o;
    }

    private static string Colour(JsonNode? n, string fallback)
        => n?.ToString() is { } s && Hex().IsMatch(s.Trim()) ? s.Trim().ToLowerInvariant() : fallback;

    private static double Clamp(double v, double lo, double hi) => Math.Round(Math.Clamp(double.IsFinite(v) ? v : lo, lo, hi), 3);

    private static JsonArray Arr(params double[] xs) => new(xs.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray());

    private static double? Num(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d)) return d;
        return double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : null;
    }

    private static double[]? Nums(JsonNode? n, int min)
    {
        if (n is not JsonArray a || a.Count < min) return null;
        var xs = a.Select(Num).ToList();
        return xs.Any(x => x is null) ? null : xs.Select(x => x!.Value).ToArray();
    }

    public const string Prompt = """
        You make 3D models for a cosy low-poly world, out of a few coloured primitives. One model fills at most one floor tile.
        Units: the tile is 1 wide (x) by 1 deep (z), centred on 0,0; y is up from the floor. A person is about 1.1 tall, a chair seat 0.45, a table 0.75.
        The front faces +z. Parts (all optional fields except the shape and c):
          {"box": [w, h, d], "at": [x, y, z], "c": "#rrggbb"}            at = bottom centre
          {"cyl": [r, h]} or {"cyl": [rTop, rBottom, h]}, "sides": 3-24    at = bottom centre
          {"cone": [r, h]}                                                 at = bottom centre
          {"ball": r, "squash": 1}                                         at = centre
          {"gable": [w, h, d]}  a roof prism, ridge along x                at = bottom centre
          {"ring": [r, tube]}  a torus standing in the xy plane            at = centre
          {"sign": [w, h], "text": "SHORT", "fg": "#ffffff"}  flat, facing +z, text on it
          Extra: "rx"/"ry"/"rz" radians to turn a part, "glow": true for lights and screens, "opacity": 0.2-1 for glass.
        Use 4 to 20 parts. Make it recognisable and charming, with real colours, not grey.
        Things that hang on a wall (posters, paintings, clocks, shelves, mirrors): set "wall": true and build them flat against z = -0.5, at y 0.8 to 2.0.
        Output ONLY JSON: {"wall": false, "parts": [ ... ]}
        Example, a potted cactus: {"wall": false, "parts": [{"cyl": [0.16, 0.13, 0.28], "at": [0, 0, 0], "c": "#c1663a"}, {"cyl": [0.09, 0.45], "at": [0, 0.28, 0], "c": "#4f8f3a", "sides": 8}, {"cyl": [0.05, 0.18], "at": [0.1, 0.45, 0], "c": "#4f8f3a", "rz": -0.6, "sides": 6}, {"ball": 0.04, "at": [0, 0.75, 0], "c": "#ff6fa8"}]}
        """;
}
