using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace PartyTown.World.Places;

/// <summary>The LLM-written half of a look (ADR 0028), cleaned so any output draws sanely. Mirrors frontend figure/doc.ts.</summary>
public static partial class FigureDesign
{
    public const int MaxExtras = 8;
    public const int MaxBlobs = 24;
    public const int MaxZones = 8;
    public const int MaxPrints = 3;
    public const int MaxSvg = 6000;
    public const int MaxMoveKeys = 8;

    private static readonly string[] EyeShapes = { "round", "almond", "cat", "droopy", "wide" };
    private static readonly string[] FacialHair = { "stubble", "goatee", "beard", "moustache", "soul-patch" };
    private static readonly string[] Piercings = { "brow", "nose", "septum", "lip", "snakebites", "labret" };
    private static readonly string[] HairZones = { "crown", "top", "fringe", "sideL", "sideR", "back", "nape" };
    private static readonly string[] Necks = { "crew", "v", "scoop", "boat", "turtle", "collar" };
    private static readonly string[] Patterns = { "stripes", "plaid", "checker", "dots", "fishnet", "fade", "stars" };
    private static readonly string[] PrintSpots = { "chest", "back", "belly", "sleeveL", "sleeveR", "thighL", "thighR" };
    private static readonly string[] Shoes = { "sneaker", "skate", "hi-top", "boot", "platform", "heel", "sandal", "loafer" };
    private static readonly string[] BlobShapes = { "ball", "capsule", "ring", "band", "shell" };
    private static readonly string[] Axes = { "x", "y", "z" };
    private static readonly string[] Hands = { "relaxed", "open", "fist", "point", "peace", "horns", "grip" };
    private static readonly string[] Sockets =
    {
        "head", "face", "ear-l", "ear-r", "neck", "chest", "back", "waist", "hip-l", "hip-r",
        "wrist-l", "wrist-r", "forearm-l", "forearm-r", "hand-l", "hand-r", "ankle-l", "ankle-r",
    };

    /// <summary>Design slots a palette pick replaces: buying a hoodie takes the designed top off.</summary>
    private static readonly Dictionary<string, string[]> Replaces = new()
    {
        ["hairStyle"] = new[] { "hair" },
        ["topStyle"] = new[] { "top", "under", "outer" },
        ["top"] = new[] { "top", "under", "outer" },
        ["bottomStyle"] = new[] { "bottom" },
        ["bottom"] = new[] { "bottom" },
        ["legwear"] = new[] { "legwear" },
        ["shoeStyle"] = new[] { "shoes" },
        ["shoes"] = new[] { "shoes" },
        ["skin"] = new[] { "body" },
        ["build"] = new[] { "body" },
        ["bodyType"] = new[] { "body" },
        ["height"] = new[] { "body" },
        ["eyes"] = new[] { "face" },
        ["facialHair"] = new[] { "face" },
    };

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex Hex();

    /// <summary>Normalised design JSON, or null when nothing usable came back. Slots left out stay on the palette.</summary>
    public static string? Clean(JsonNode? raw)
    {
        if (raw is not JsonObject o) return null;
        var clean = new JsonObject();
        void Slot(string key, Func<JsonObject, JsonObject?> f)
        {
            if (o[key] is JsonObject s && f(s) is { } c) clean[key] = c;
        }
        Slot("body", Body);
        Slot("face", Face);
        Slot("hair", Hair);
        Slot("top", Top);
        Slot("outer", Top);
        Slot("under", Top);
        Slot("bottom", Bottom);
        Slot("legwear", Legwear);
        Slot("shoes", ShoeSlot);
        Slot("stance", Pose);
        if (o["extras"] is JsonArray extras)
            clean["extras"] = new JsonArray(extras.OfType<JsonObject>().Take(MaxExtras).Select(Extra).OfType<JsonObject>().ToArray<JsonNode?>());
        if (o["moves"] is JsonObject moves)
        {
            var m = new JsonObject();
            foreach (var (name, doc) in moves.Take(4))
                if (doc is JsonObject move && Move(move) is { } c) m[Slug(name)] = c;
            if (m.Count > 0) clean["moves"] = m;
        }
        return clean.Count == 0 ? null : clean.ToJsonString();
    }

    /// <summary>A design with some slots laid over it; later slots win whole.</summary>
    public static string? Over(string? design, string? patch)
    {
        if (patch is null) return design;
        if (Parse(design) is not { } current) return patch;
        foreach (var (key, value) in Parse(patch) ?? new JsonObject())
            current[key] = value?.DeepClone();
        return current.ToJsonString();
    }

    /// <summary>What's left of a design once these palette picks take over.</summary>
    public static string? Without(string? design, IEnumerable<string> picked)
    {
        var keys = picked.ToList();
        if (keys.Count == 0 || Parse(design) is not { } current) return design;
        foreach (var slot in keys.Where(Replaces.ContainsKey).SelectMany(k => Replaces[k]).Distinct())
            current.Remove(slot);
        // A dye keeps the cut: the palette colour shows through.
        if (keys.Contains("hair") && current["hair"] is JsonObject hair)
        {
            hair.Remove("color");
            hair.Remove("tips");
            foreach (var zone in (hair["zones"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                zone.Remove("color");
                zone.Remove("bands");
            }
        }
        return current.Count == 0 ? null : current.ToJsonString();
    }

    /// <summary>Prints still waiting to be drawn, as (slot, index, brief).</summary>
    public static List<(string Slot, int Index, string Brief)> Briefs(string? design)
    {
        var found = new List<(string, int, string)>();
        if (Parse(design) is not { } doc) return found;
        foreach (var slot in new[] { "top", "outer", "under", "bottom" })
        {
            if (doc[slot]?["prints"] is not JsonArray prints) continue;
            for (var i = 0; i < prints.Count; i++)
                if (prints[i]?["svg"] is null && prints[i]?["brief"]?.ToString() is { Length: > 0 } brief)
                    found.Add((slot, i, brief));
        }
        return found;
    }

    /// <summary>Puts drawn artwork in; a print with no artwork is dropped.</summary>
    public static string? WithArtwork(string? design, IReadOnlyDictionary<(string Slot, int Index), string> art)
    {
        if (Parse(design) is not { } doc) return design;
        foreach (var slot in new[] { "top", "outer", "under", "bottom" })
        {
            if (doc[slot] is not JsonObject garment || garment["prints"] is not JsonArray prints) continue;
            var kept = new JsonArray();
            for (var i = 0; i < prints.Count; i++)
            {
                if (prints[i] is not JsonObject p) continue;
                if (art.TryGetValue((slot, i), out var svg)) p["svg"] = svg;
                if (p["svg"] is null) continue;
                p.Remove("brief");
                kept.Add(p.DeepClone());
            }
            if (kept.Count == 0) garment.Remove("prints");
            else garment["prints"] = kept;
        }
        return doc.ToJsonString();
    }

    private static JsonObject? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { return null; }
    }

    // ---- Slots ----

    private static JsonObject Body(JsonObject o) => new()
    {
        ["skin"] = Colour(o["skin"], "#f1c9a5"),
        ["height"] = Unit(o["height"]),
        ["build"] = Unit(o["build"]),
        ["curve"] = Unit(o["curve"], 0.2),
        ["shoulders"] = Unit(o["shoulders"]),
        ["hips"] = Unit(o["hips"]),
        ["legs"] = Unit(o["legs"]),
        ["head"] = Unit(o["head"]),
        ["muscle"] = Unit(o["muscle"], 0.3),
        ["belly"] = Unit(o["belly"], 0.1),
    };

    private static JsonObject Face(JsonObject o)
    {
        var eyes = o["eyes"] as JsonObject ?? new JsonObject();
        var brows = o["brows"] as JsonObject ?? new JsonObject();
        var mouth = o["mouth"] as JsonObject ?? new JsonObject();
        var face = new JsonObject
        {
            ["shape"] = Unit(o["shape"]),
            ["jaw"] = Unit(o["jaw"], 0.4),
            ["nose"] = Unit(o["nose"], 0.4),
            ["ears"] = Unit(o["ears"], 0.45),
            ["eyes"] = new JsonObject
            {
                ["shape"] = Pick(eyes["shape"], EyeShapes, "almond"),
                ["size"] = Unit(eyes["size"], 0.65),
                ["iris"] = Colour(eyes["iris"], "#4a3a2a"),
                ["liner"] = Unit(eyes["liner"], 0.1),
                ["wing"] = Unit(eyes["wing"], 0),
                ["linerColor"] = Colour(eyes["linerColor"], "#1c1418"),
            },
            ["brows"] = new JsonObject
            {
                ["color"] = Colour(brows["color"], "#2a1d18"),
                ["weight"] = Unit(brows["weight"], 0.45),
                ["arch"] = Unit(brows["arch"], 0.45),
            },
            ["mouth"] = new JsonObject
            {
                ["width"] = Unit(mouth["width"]),
                ["lips"] = Unit(mouth["lips"]),
                ["color"] = Colour(mouth["color"], "#b5616a"),
                ["set"] = Number(mouth["set"], -1, 1, 0.1),
            },
        };
        if (OptionalColour(eyes["shadow"]) is { } shadow) ((JsonObject)face["eyes"]!)["shadow"] = shadow;
        if (brows["slit"]?.GetValueKind() == JsonValueKind.True) ((JsonObject)face["brows"]!)["slit"] = true;
        if (OptionalColour(o["blush"]) is { } blush) face["blush"] = blush;
        if (Num(o["freckles"]) is { } freckles && freckles > 0) face["freckles"] = Round(Math.Clamp(freckles, 0, 1));
        if (PickOrNull(o["facialHair"], FacialHair) is { } beard)
        {
            face["facialHair"] = beard;
            if (OptionalColour(o["facialHairColor"]) is { } c) face["facialHairColor"] = c;
        }
        if (o["piercings"] is JsonArray piercings)
        {
            var kept = piercings.Select(p => PickOrNull(p, Piercings)).OfType<string>().Distinct().Select(p => (JsonNode)JsonValue.Create(p)!).ToArray();
            if (kept.Length > 0) face["piercings"] = new JsonArray(kept);
        }
        if (Svg(o["paint"]?.ToString()) is { } paint) face["paint"] = paint;
        return face;
    }

    private static JsonObject Hair(JsonObject o)
    {
        var zones = new JsonArray();
        foreach (var z in (o["zones"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Take(MaxZones))
            if (Zone(z) is { } c) zones.Add(c);
        var hair = new JsonObject { ["cap"] = Unit(o["cap"], 1), ["zones"] = zones };
        if (OptionalColour(o["color"]) is { } color) hair["color"] = color;
        if (OptionalColour(o["tips"]) is { } tips) hair["tips"] = tips;
        return hair;
    }

    private static JsonObject? Zone(JsonObject o)
    {
        JsonNode? at = PickOrNull(o["at"], HairZones) is { } name ? name
            : Nums(o["at"], 4) is { } box ? Arr(Math.Clamp(box[0], -360, 360), Math.Clamp(box[1], -360, 360), Math.Clamp(box[2], -60, 90), Math.Clamp(box[3], -60, 90))
            : null;
        if (at is null) return null;
        var zone = new JsonObject
        {
            ["at"] = at,
            ["count"] = (int)Number(o["count"], 1, 40, 10),
            ["len"] = Number(o["len"], 0.05, 2.2, 0.5),
            ["width"] = Number(o["width"], 0.06, 0.4, 0.2),
            ["jag"] = Unit(o["jag"], 0.3),
            ["lift"] = Unit(o["lift"], 0.05),
            ["fall"] = Unit(o["fall"], 1),
            ["sweep"] = Number(o["sweep"], -1, 1, 0),
            ["curl"] = Unit(o["curl"], 0),
        };
        if (OptionalColour(o["color"]) is { } c) zone["color"] = c;
        if (o["bands"] is JsonArray bands)
        {
            var kept = bands.Select(OptionalColour).OfType<string>().Take(6).Select(b => (JsonNode)JsonValue.Create(b)!).ToArray();
            if (kept.Length > 0) zone["bands"] = new JsonArray(kept);
        }
        return zone;
    }

    private static JsonObject Top(JsonObject o)
    {
        var top = new JsonObject
        {
            ["color"] = Colour(o["color"], "#808080"),
            ["sleeve"] = Unit(o["sleeve"], 0.28),
            ["hem"] = Number(o["hem"], 0, 1, 0.45),
            ["fit"] = Unit(o["fit"], 0.25),
            ["neck"] = Pick(o["neck"], Necks, "crew"),
        };
        if (OptionalColour(o["trim"]) is { } trim) top["trim"] = trim;
        if (OptionalColour(o["sleeveColor"]) is { } sleeve) top["sleeveColor"] = sleeve;
        foreach (var flag in new[] { "hood", "open" })
            if (o[flag]?.GetValueKind() == JsonValueKind.True) top[flag] = true;
        if (Num(o["sheer"]) is { } sheer && sheer > 0) top["sheer"] = Round(Math.Clamp(sheer, 0, 1));
        Surface(o, top);
        return top;
    }

    private static JsonObject Bottom(JsonObject o)
    {
        var bottom = new JsonObject
        {
            ["color"] = Colour(o["color"], "#3b3f58"),
            ["kind"] = Pick(o["kind"], new[] { "pants", "skirt" }, "pants"),
            ["length"] = Unit(o["length"], 1),
            ["fit"] = Unit(o["fit"], 0.3),
            ["flare"] = Unit(o["flare"], 0),
            ["rise"] = Unit(o["rise"], 0.35),
        };
        if (Num(o["pleats"]) is { } pleats && pleats > 0) bottom["pleats"] = Round(Math.Clamp(pleats, 0, 1));
        if (Num(o["rips"]) is { } rips && rips > 0) bottom["rips"] = Round(Math.Clamp(rips, 0, 1));
        if (OptionalColour(o["trim"]) is { } trim) bottom["trim"] = trim;
        Surface(o, bottom);
        return bottom;
    }

    private static JsonObject Legwear(JsonObject o)
    {
        var legwear = new JsonObject { ["color"] = Colour(o["color"], "#141318"), ["length"] = Unit(o["length"], 0.5) };
        if (o["pattern"] is JsonObject p) legwear["pattern"] = Pattern(p);
        if (Num(o["sheer"]) is { } sheer && sheer > 0) legwear["sheer"] = Round(Math.Clamp(sheer, 0, 1));
        return legwear;
    }

    private static JsonObject ShoeSlot(JsonObject o) => new()
    {
        ["kind"] = Pick(o["kind"], Shoes, "sneaker"),
        ["color"] = Colour(o["color"], "#2b2b2b"),
        ["sole"] = Colour(o["sole"], "#ece8df"),
        ["trim"] = Colour(o["trim"], "#ece8df"),
        ["platform"] = Unit(o["platform"], 0),
    };

    private static void Surface(JsonObject o, JsonObject into)
    {
        if (o["pattern"] is JsonObject p) into["pattern"] = Pattern(p);
        var prints = new JsonArray();
        foreach (var print in (o["prints"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Take(MaxPrints))
        {
            var kept = new JsonObject { ["at"] = Pick(print["at"], PrintSpots, "chest"), ["size"] = Unit(print["size"]) };
            if (Svg(print["svg"]?.ToString()) is { } svg) kept["svg"] = svg;
            else if (print["brief"]?.ToString() is { Length: > 0 } brief) kept["brief"] = WorldStream.Clip(brief, 200);
            else continue;
            prints.Add(kept);
        }
        if (prints.Count > 0) into["prints"] = prints;
    }

    private static JsonObject Pattern(JsonObject o)
    {
        var colors = (o["colors"] as JsonArray ?? new JsonArray()).Select(OptionalColour).OfType<string>().Take(4)
            .Select(c => (JsonNode)JsonValue.Create(c)!).ToArray();
        return new JsonObject
        {
            ["kind"] = Pick(o["kind"], Patterns, "stripes"),
            ["colors"] = colors.Length > 0 ? new JsonArray(colors) : new JsonArray("#222222", "#eeeeee"),
            ["scale"] = Unit(o["scale"]),
        };
    }

    private static JsonObject? Extra(JsonObject o)
    {
        var blobs = new JsonArray();
        foreach (var b in (o["blobs"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Take(MaxBlobs))
            if (Blob(b) is { } c) blobs.Add(c);
        if (blobs.Count == 0) return null;
        return new JsonObject
        {
            ["name"] = WorldStream.Clip(o["name"]?.ToString() ?? "extra", 40),
            ["socket"] = Pick(o["socket"], Sockets, "chest"),
            ["blobs"] = blobs,
        };
    }

    private static JsonObject? Blob(JsonObject o)
    {
        if (PickOrNull(o["shape"], BlobShapes) is not { } shape) return null;
        var at = Nums(o["at"], 3) ?? new double[] { 0, 0, 0 };
        var blob = new JsonObject
        {
            ["shape"] = shape,
            ["at"] = Arr(Math.Clamp(at[0], -2, 2), Math.Clamp(at[1], -2, 2), Math.Clamp(at[2], -2, 2)),
            ["r"] = Number(o["r"], 0.005, 1, 0.05),
            ["color"] = Colour(o["color"], "#999999"),
        };
        if (shape == "capsule")
        {
            if (Nums(o["to"], 3) is not { } to) return null;
            blob["to"] = Arr(Math.Clamp(to[0], -2, 2), Math.Clamp(to[1], -2, 2), Math.Clamp(to[2], -2, 2));
        }
        if (Num(o["tube"]) is { } tube) blob["tube"] = Round(Math.Clamp(tube, 0.004, 0.2));
        if (PickOrNull(o["axis"], Axes) is { } axis) blob["axis"] = axis;
        foreach (var flag in new[] { "metal", "hat" })
            if (o[flag]?.GetValueKind() == JsonValueKind.True) blob[flag] = true;
        return blob;
    }

    private static JsonObject Pose(JsonObject o)
    {
        var pose = new JsonObject();
        foreach (var key in new[] { "spine", "head" })
            if (Nums(o[key], 3) is { } v) pose[key] = Arr(Math.Clamp(v[0], -60, 60), Math.Clamp(v[1], -80, 80), Math.Clamp(v[2], -40, 40));
        foreach (var key in new[] { "armL", "armR" })
            if (Nums(o[key], 3) is { } v) pose[key] = Arr(Math.Clamp(v[0], -60, 180), Math.Clamp(v[1], -30, 170), Math.Clamp(v[2], -90, 90));
        foreach (var key in new[] { "legL", "legR" })
            if (Nums(o[key], 2) is { } v) pose[key] = Arr(Math.Clamp(v[0], -60, 120), Math.Clamp(v[1], -30, 70));
        foreach (var key in new[] { "elbowL", "elbowR", "kneeL", "kneeR" })
            if (Num(o[key]) is { } v) pose[key] = Round(Math.Clamp(v, 0, 150));
        if (Num(o["sink"]) is { } sink) pose["sink"] = Round(Math.Clamp(sink, 0, 0.6));
        if (Num(o["shift"]) is { } shift) pose["shift"] = Round(Math.Clamp(shift, -0.1, 0.1));
        foreach (var key in new[] { "handL", "handR" })
            if (PickOrNull(o[key], Hands) is { } hand) pose[key] = hand;
        return pose;
    }

    private static JsonObject? Move(JsonObject o)
    {
        var keys = new JsonArray();
        foreach (var k in (o["keys"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Take(MaxMoveKeys))
        {
            var pose = Pose(k);
            pose["t"] = Unit(k["t"], 0);
            keys.Add(pose);
        }
        return keys.Count == 0 ? null : new JsonObject { ["loop"] = Number(o["loop"], 0.3, 12, 2), ["keys"] = keys };
    }

    // ---- SVG ----

    private static readonly HashSet<string> SvgTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "svg", "g", "path", "circle", "ellipse", "rect", "line", "polyline", "polygon", "text", "tspan",
        "defs", "linearGradient", "radialGradient", "stop", "clipPath",
    };

    private static readonly HashSet<string> SvgAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "viewBox", "xmlns", "id", "d", "x", "y", "x1", "y1", "x2", "y2", "cx", "cy", "r", "rx", "ry", "fx", "fy", "width", "height",
        "points", "transform", "fill", "stroke", "stroke-width", "stroke-linecap", "stroke-linejoin", "stroke-dasharray", "fill-rule",
        "opacity", "fill-opacity", "stroke-opacity", "font-family", "font-size", "font-weight", "font-style", "text-anchor", "letter-spacing",
        "dominant-baseline", "offset", "stop-color", "stop-opacity", "gradientUnits", "gradientTransform", "clip-path", "clip-rule",
    };

    /// <summary>Plain drawing only: elements and attributes off a list, nothing that can fetch or run; null when it isn't SVG.</summary>
    public static string? Svg(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var start = raw.IndexOf("<svg", StringComparison.OrdinalIgnoreCase);
        var end = raw.LastIndexOf("</svg>", StringComparison.OrdinalIgnoreCase);
        if (start < 0 || end < start || end - start > MaxSvg * 2) return null;
        XElement root;
        try
        {
            using var reader = XmlReader.Create(new StringReader(raw[start..(end + 6)]), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            root = XElement.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }
        if (!Scrub(root)) return null;
        // An img only rasterises SVG with this namespace, and the viewBox is what the painter sizes by.
        root.Name = XName.Get("svg", "http://www.w3.org/2000/svg");
        foreach (var e in root.Descendants()) e.Name = XName.Get(e.Name.LocalName, "http://www.w3.org/2000/svg");
        root.SetAttributeValue("viewBox", root.Attribute("viewBox")?.Value ?? "0 0 256 256");
        // Firefox won't draw an SVG image that has no intrinsic size.
        root.SetAttributeValue("width", 256);
        root.SetAttributeValue("height", 256);
        var clean = root.ToString(SaveOptions.DisableFormatting);
        return clean.Length <= MaxSvg ? clean : null;
    }

    private static bool Scrub(XElement e)
    {
        if (!SvgTags.Contains(e.Name.LocalName)) return false;
        foreach (var a in e.Attributes().ToList())
            if (a.IsNamespaceDeclaration || !SvgAttributes.Contains(a.Name.LocalName) || a.Value.Contains("url(", StringComparison.OrdinalIgnoreCase) && !a.Value.Contains("url(#", StringComparison.OrdinalIgnoreCase))
                a.Remove();
        foreach (var child in e.Elements().ToList())
            if (!Scrub(child)) child.Remove();
        return true;
    }

    // ---- Values ----

    private static string Slug(string s) => WorldStream.Clip(new string(s.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray()), 24);

    private static string Pick(JsonNode? n, string[] options, string fallback) => PickOrNull(n, options) ?? fallback;

    private static string? PickOrNull(JsonNode? n, string[] options)
    {
        if (n is not JsonValue v || v.GetValueKind() != JsonValueKind.String) return null;
        var s = v.ToString().Trim();
        return options.FirstOrDefault(o => o.Equals(s, StringComparison.OrdinalIgnoreCase));
    }

    private static string Colour(JsonNode? n, string fallback) => OptionalColour(n) ?? fallback;

    private static string? OptionalColour(JsonNode? n)
    {
        if (n is not JsonValue || n.ToString().Trim() is not { } s || !Hex().IsMatch(s)) return null;
        // The painter's shade maths wants six digits.
        return (s.Length == 4 ? $"#{s[1]}{s[1]}{s[2]}{s[2]}{s[3]}{s[3]}" : s).ToLowerInvariant();
    }

    private static double Round(double v) => Math.Round(v, 3);

    private static double Unit(JsonNode? n, double fallback = 0.5) => Number(n, 0, 1, fallback);

    private static double Number(JsonNode? n, double lo, double hi, double fallback)
        => Round(Math.Clamp(Num(n) is { } v && double.IsFinite(v) ? v : fallback, lo, hi));

    private static JsonArray Arr(params double[] xs) => new(xs.Select(x => (JsonNode)JsonValue.Create(Round(x))!).ToArray());

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
}
