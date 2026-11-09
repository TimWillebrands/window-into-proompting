using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PartyTown.World.Places;

/// <summary>A 3D model as data (ADR 0049): coloured shapes, grouped and repeated, cleaned so any model's output draws sanely on its footprint.</summary>
public static partial class ThingModel
{
    /// <summary>Parts as written, groups included.</summary>
    public const int MaxParts = 64;
    /// <summary>Shapes drawn once repeats are counted.</summary>
    public const int MaxDrawn = 320;
    public const int MaxSide = 3;
    private static readonly string[] Shapes = { "box", "cyl", "cone", "ball", "dome", "capsule", "gable", "wedge", "pyramid", "ring", "lathe", "extrude", "tube", "sign", "group", "use" };
    public static readonly string[] Tags = { "flicker", "disco", "smoke", "spin", "bob", "sway", "pulse", "flame", "twinkle" };

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex Hex();

    [GeneratedRegex("^\\$[a-z][a-z0-9-]{0,20}$")]
    private static partial Regex TintRef();

    private sealed class Budget
    {
        public int Parts;
        public int Drawn;
        public double Reach = 0.5;
        public Func<string, bool> Known = _ => false;
    }

    /// <summary>Normalised model JSON, or null when nothing usable came back.</summary>
    /// <param name="known">Extra prefab names a `use` may name, beside the catalogue.</param>
    public static string? Clean(JsonNode? raw, Func<string, bool>? known = null) => CleanObject(raw, known)?.ToJsonString();

    public static JsonObject? CleanObject(JsonNode? raw, Func<string, bool>? known = null)
    {
        var parts = raw switch
        {
            JsonArray a => a,
            JsonObject o when o["parts"] is JsonArray a => a,
            _ => null,
        };
        if (parts is null) return null;
        var src = raw as JsonObject;
        var wall = src?["wall"]?.GetValueKind() == JsonValueKind.True;
        var size = Nums(src?["size"], 2) is { } sz
            ? new[] { (int)Math.Clamp(Math.Round(sz[0]), 1, MaxSide), (int)Math.Clamp(Math.Round(sz[1]), 1, MaxSide) }
            : new[] { 1, 1 };
        if (wall) size = new[] { 1, 1 };
        var budget = new Budget
        {
            Reach = Math.Max(size[0], size[1]) / 2.0,
            Known = n => PlaceDesign.Prefabs.ContainsKey(n) || known?.Invoke(n) == true,
        };
        var clean = CleanParts(parts, wall, budget, 1, 0);
        if (clean.Count == 0) return null;

        var model = new JsonObject { ["wall"] = wall };
        if (size[0] != 1 || size[1] != 1) model["size"] = new JsonArray(size[0], size[1]);
        if (!wall && src?["solid"]?.GetValueKind() == JsonValueKind.False) model["solid"] = false;
        if (src?["about"]?.ToString() is { Length: > 0 } about) model["about"] = WorldStream.Clip(about, 80);
        if (!wall && src?["seats"] is JsonArray seats)
        {
            var kept = new JsonArray();
            foreach (var s in seats.OfType<JsonObject>().Take(6))
                if (Nums(s["at"], 2) is { } at)
                    kept.Add(new JsonObject
                    {
                        ["at"] = Arr(Clamp(at[0], 0, size[0] - 1), Clamp(at[1], 0, size[1] - 1)),
                        ["h"] = Clamp(Num(s["h"]) ?? 0.45, 0, 1.5),
                        ["face"] = (int)Clamp(Num(s["face"]) ?? 0, 0, 3),
                    });
            if (kept.Count > 0) model["seats"] = kept;
        }
        if (src?["tint"] is JsonObject tint)
        {
            var kept = new JsonObject();
            foreach (var (k, v) in tint.Take(6))
                if (TintRef().IsMatch("$" + k)) kept[k] = Colour(v, "#999999", tints: false);
            if (kept.Count > 0) model["tint"] = kept;
        }
        if (src?["light"] is JsonObject light)
        {
            var at = Nums(light["at"], 3) ?? new double[] { 0, 1, 0 };
            var colour = Colour(light["colour"] ?? light["color"] ?? light["c"], "#ffcf87", tints: false);
            model["light"] = new JsonObject
            {
                ["at"] = Arr(Clamp(at[0], -budget.Reach, budget.Reach), Clamp(at[1], 0, 3), Clamp(at[2], -budget.Reach, budget.Reach)),
                ["colour"] = Library.Palette.GetValueOrDefault(colour, colour),
                ["power"] = Clamp(Num(light["power"]) ?? 1.5, 0.2, 4),
            };
        }
        model["parts"] = clean;
        return model;
    }

    private static JsonArray CleanParts(JsonArray parts, bool wall, Budget budget, int copies, int depth)
    {
        var clean = new JsonArray();
        foreach (var p in parts.OfType<JsonObject>())
        {
            if (budget.Parts >= MaxParts) break;
            if (CleanPart(p, wall, budget, copies, depth) is { } c) clean.Add(c);
        }
        return clean;
    }

    private static JsonObject? CleanPart(JsonObject p, bool wall, Budget budget, int copies, int depth)
    {
        var shape = Shapes.FirstOrDefault(p.ContainsKey);
        if (shape is null) return null;
        var o = new JsonObject();
        var reach = budget.Reach;
        var wide = reach * 2;

        // Repeats first: they decide how much of the budget this part takes.
        var mine = 1;
        if (p["array"] is JsonObject array && Num(array["n"]) is { } an && an >= 2)
        {
            var n = (int)Math.Clamp(an, 2, 24);
            var step = Nums(array["step"], 3) ?? new double[] { 0, 0, 0 };
            o["array"] = new JsonObject { ["n"] = n, ["step"] = Arr(Clamp(step[0], -wide, wide), Clamp(step[1], -3, 3), Clamp(step[2], -wide, wide)) };
            mine *= n;
        }
        if (Num(p["spin"]) is { } spin && spin >= 2)
        {
            o["spin"] = (int)Math.Clamp(spin, 2, 24);
            mine *= (int)Math.Clamp(spin, 2, 24);
        }
        if (p["mirror"]?.ToString() is "x" or "z" or "xz")
        {
            o["mirror"] = p["mirror"]!.ToString();
            mine *= o["mirror"]!.ToString() == "xz" ? 4 : 2;
        }
        copies *= mine;

        budget.Parts++;
        switch (shape)
        {
            case "group":
                if (depth >= 3 || p[shape] is not JsonArray children) return null;
                var kept = CleanParts(children, wall, budget, copies, depth + 1);
                if (kept.Count == 0) return null;
                o[shape] = kept;
                break;
            case "use":
                var name = p[shape]?.ToString() ?? string.Empty;
                if (depth >= 2 || !budget.Known(name) || budget.Drawn + copies * 12 > MaxDrawn) return null;
                budget.Drawn += copies * 12;
                o[shape] = name;
                break;
            default:
                if (budget.Drawn + copies > MaxDrawn) return null;
                budget.Drawn += copies;
                if (!CleanShape(shape, p, o, wide)) return null;
                break;
        }

        var at = Nums(p["at"], 3) ?? new double[] { 0, 0, 0 };
        o["at"] = Arr(Clamp(at[0], -reach, reach), Clamp(at[1], wall && depth == 0 ? 0.3 : -0.5, 3), Clamp(at[2], -reach, reach));
        if (shape is not ("group" or "use")) o["c"] = Colour(p["c"], "#999999");
        foreach (var axis in new[] { "rx", "ry", "rz" })
            if (Num(p[axis]) is { } a) o[axis] = Clamp(a, -Math.PI * 2, Math.PI * 2);
        if (Nums(p["s"], 3) is { } s) o["s"] = Arr(Clamp(s[0], 0.05, 6), Clamp(s[1], 0.05, 6), Clamp(s[2], 0.05, 6));
        if (p["glow"]?.GetValueKind() == JsonValueKind.True) o["glow"] = true;
        else if (p["glow"]?.ToString() == "night") o["glow"] = "night";
        if (Num(p["opacity"]) is { } op && op < 1) o["opacity"] = Clamp(op, 0.2, 1);
        if (p["tag"]?.ToString() is { } tag && Tags.Contains(tag)) o["tag"] = tag;
        return o;
    }

    private static bool CleanShape(string shape, JsonObject p, JsonObject o, double wide)
    {
        var r = wide / 2;
        switch (shape)
        {
            case "box":
            case "gable":
            case "wedge":
            case "pyramid":
                if (Nums(p[shape], 3) is not { } b) return false;
                o[shape] = Arr(Clamp(b[0], 0.005, wide), Clamp(b[1], 0.005, 2.6), Clamp(b[2], 0.005, wide));
                if (shape == "pyramid" && Num(p["top"]) is { } top) o["top"] = Clamp(top, 0, 0.98);
                break;
            case "cyl":
                if (Nums(p[shape], 2) is not { } c) return false;
                o[shape] = c.Length >= 3
                    ? Arr(Clamp(c[0], 0, r), Clamp(c[1], 0, r), Clamp(c[2], 0.005, 2.6))
                    : Arr(Clamp(c[0], 0.005, r), Clamp(c[1], 0.005, 2.6));
                o["sides"] = (int)Clamp(Num(p["sides"]) ?? 10, 3, 24);
                break;
            case "cone":
            case "capsule":
                if (Nums(p[shape], 2) is not { } k) return false;
                o[shape] = Arr(Clamp(k[0], 0.005, r), Clamp(k[1], 0.01, 2.6));
                if (shape == "cone") o["sides"] = (int)Clamp(Num(p["sides"]) ?? 10, 3, 24);
                break;
            case "ball":
            case "dome":
                if (Num(p[shape]) is not { } br) return false;
                o[shape] = Clamp(br, 0.01, r);
                if (Num(p["squash"]) is { } sq) o["squash"] = Clamp(sq, 0.15, 3);
                break;
            case "ring":
                if (Nums(p[shape], 2) is not { } g) return false;
                o[shape] = Arr(Clamp(g[0], 0.02, r), Clamp(g[1], 0.005, 0.15));
                if (Num(p["arc"]) is { } arc) o["arc"] = Clamp(arc, 0.1, Math.PI * 2);
                break;
            case "lathe":
                if (Points(p[shape], 2, 16) is not { Count: >= 2 } profile) return false;
                o[shape] = new JsonArray(profile.Select(q => (JsonNode)Arr(Clamp(q[0], 0, r), Clamp(q[1], -0.5, 2.6))).ToArray());
                o["sides"] = (int)Clamp(Num(p["sides"]) ?? 10, 3, 24);
                break;
            case "extrude":
                if (Points(p[shape], 2, 24) is not { Count: >= 3 } outline) return false;
                o[shape] = new JsonArray(outline.Select(q => (JsonNode)Arr(Clamp(q[0], -wide, wide), Clamp(q[1], -2.6, 2.6))).ToArray());
                o["depth"] = Clamp(Num(p["depth"]) ?? 0.05, 0.005, wide);
                break;
            case "tube":
                if (Points(p[shape], 3, 12) is not { Count: >= 2 } path) return false;
                o[shape] = new JsonArray(path.Select(q => (JsonNode)Arr(Clamp(q[0], -wide, wide), Clamp(q[1], -0.5, 3), Clamp(q[2], -wide, wide))).ToArray());
                o["r"] = Clamp(Num(p["r"]) ?? 0.02, 0.004, 0.2);
                if (p["closed"]?.GetValueKind() == JsonValueKind.True) o["closed"] = true;
                break;
            case "sign":
                if (Nums(p[shape], 2) is not { } s) return false;
                o[shape] = Arr(Clamp(s[0], 0.05, wide), Clamp(s[1], 0.05, 1));
                o["text"] = WorldStream.Clip(p["text"]?.ToString() ?? string.Empty, 24);
                o["fg"] = Colour(p["fg"], "#ffffff", tints: false) is var fg && Library.Palette.TryGetValue(fg, out var hex) ? hex : Colour(p["fg"], "#ffffff", tints: false);
                break;
        }
        return true;
    }

    /// <summary>Hex, a palette name, or a `$tint`; the fallback otherwise.</summary>
    private static string Colour(JsonNode? n, string fallback, bool tints = true)
    {
        var s = n?.ToString().Trim();
        if (s is null) return fallback;
        if (Hex().IsMatch(s)) return s.ToLowerInvariant();
        var name = s.ToLowerInvariant().Replace(' ', '-').Replace('_', '-');
        if (Library.Palette.ContainsKey(name)) return name;
        return tints && TintRef().IsMatch(name) ? name : fallback;
    }

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

    private static List<double[]>? Points(JsonNode? n, int dims, int max)
        => n is JsonArray a ? a.Take(max).Select(q => Nums(q, dims)).OfType<double[]>().ToList() : null;

    /// <summary>The whole format, for whoever models: an LLM in the World or an agent over MCP.</summary>
    public static string Prompt => $$$"""
        You make 3D models for a cosy low-poly world out of coloured shapes. Keep it simple if you like: a handful of boxes and cylinders is a fine model.
        Units: one floor tile is 1 wide (x) by 1 deep (z), centred on 0,0; y is up from the floor. A person is about 1.1 tall, a chair seat 0.45, a table 0.75. The front faces +z.

        Output ONLY JSON: {"wall": false, "parts": [ ... ]}

        Shapes (each part is one shape plus "at": [x, y, z] and "c": colour):
          {"box": [w, h, d]}                       at = bottom centre
          {"cyl": [r, h]} or {"cyl": [rTop, rBottom, h]}, "sides": 3-24
          {"cone": [r, h]}   {"capsule": [r, h]}   {"pyramid": [w, h, d], "top": 0-1 to cut it flat}
          {"gable": [w, h, d]}  roof prism, ridge along x      {"wedge": [w, h, d]}  ramp, high at -z
          {"ball": r, "squash": 1}  at = centre    {"dome": r}  half ball, at = flat side
          {"ring": [r, tube]}  stands in the xy plane, at = centre
          {"sign": [w, h], "text": "SHORT", "fg": "#ffffff"}  flat, faces +z
        Colour: "#rrggbb" or a name: {{{string.Join(", ", Library.Palette.Keys)}}}.
        Extras on any part: "rx"/"ry"/"rz" radians, "glow": true (lights, screens, flames), "opacity": 0.2-1 (glass, water).

        That is enough. If you want more:
          {"lathe": [[r, y], ...]}  a profile turned round the y axis: vases, bottles, lampshades, columns
          {"extrude": [[x, y], ...], "depth": 0.05}  a flat outline given thickness: stars, leaves, arches, guitar bodies
          {"tube": [[x, y, z], ...], "r": 0.02}  a pipe through points: cables, handles, vines, neon
          {"group": [parts...], "at": [...], "ry": ...}  parts moved, turned and repeated as one
          {"use": "chair", "at": [...], "ry": ...}  a whole catalogue prefab as a part
          Repeat instead of retyping: "array": {"n": 4, "step": [0.2, 0, 0]} a row; "spin": 6 copies round the y axis (give "at" an x offset first); "mirror": "x" | "z" | "xz" a twin across the middle.
          "s": [x, y, z] stretches a part. "tag" makes it move: {{{string.Join(", ", Tags)}}}.
          Model level: "size": [w, d] up to 3x3 tiles; "seats": [{"at": [tileX, tileZ], "h": 0.45, "face": 0-3}] so people can sit on it; "solid": false for rugs and things overhead;
          "light": {"at": [x, y, z], "colour": "#ffcf87", "power": 0.5-3}; "tint": {"cloth": "red"} with "c": "$cloth" so a placement can recolour it.
        At most {{{MaxParts}}} parts as written. Real colours, not grey. Recognisable beats detailed.
        Things on a wall (posters, clocks, shelves, mirrors): "wall": true, built flat against z = -0.5, between y 0.8 and 2.0.

        Example, a potted cactus:
        {"wall": false, "parts": [{"cyl": [0.16, 0.13, 0.28], "at": [0, 0, 0], "c": "terracotta"}, {"capsule": [0.09, 0.5], "at": [0, 0.28, 0], "c": "moss"}, {"cyl": [0.05, 0.18], "at": [0.1, 0.45, 0], "c": "moss", "rz": -0.6, "sides": 6}, {"ball": 0.04, "at": [0, 0.8, 0], "c": "pink"}]}
        Example, a round stool, legs repeated:
        {"wall": false, "seats": [{"at": [0, 0], "h": 0.5, "face": 0}], "parts": [{"cyl": [0.2, 0.06], "at": [0, 0.46, 0], "c": "$seat"}, {"cyl": [0.02, 0.46], "at": [0.14, 0, 0], "c": "dark-wood", "spin": 4}], "tint": {"seat": "teal"}}
        """;
}
