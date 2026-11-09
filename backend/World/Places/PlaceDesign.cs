using PartyTown.Grains.Generation;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PartyTown.World.Places;

/// <summary>One prefab the client can draw (shared with the frontend, which tests it stays in sync).</summary>
public sealed record PrefabInfo(string Name, int[] Size, bool Solid, bool Wall, int Seats, string About);

/// <summary>Something to do at a Place, pinned to a thing in its scene (ADR 0049).</summary>
[GenerateSerializer, Alias("PartyTown.World.Places.Feature")]
public sealed record Feature([property: Id(0)] string Id, [property: Id(1)] string Name, [property: Id(2)] string SpotId,
    [property: Id(3)] string Do, [property: Id(4)] string Gives, [property: Id(5)] bool Leaves);

/// <summary>A design looked over: the draft when it works, what's wrong when it doesn't, what was mended, and the map with everything on it.</summary>
public sealed record DesignCheck(PlaceDraft? Draft, List<string> Errors, List<string> Notes, string Preview);

/// <summary>Generated Places (ADR 0023, 0049): someone designs spots and a scene; this mends what it can, checks it draws and every spot can be reached.</summary>
public static partial class PlaceDesign
{
    public const int MaxSide = 30;
    public const int MaxFeatures = 5;
    public const int MaxModels = 8;

    private static readonly Lazy<Dictionary<string, PrefabInfo>> Catalogue = new(() =>
    {
        using var s = typeof(PlaceDesign).Assembly.GetManifestResourceStream("PartyTown.World.Places.prefabs.json")!;
        var list = JsonSerializer.Deserialize<List<PrefabInfo>>(s, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var all = list.ToDictionary(p => p.Name);
        foreach (var p in Library.Infos()) all.TryAdd(p.Name, p);
        return all;
    });

    public static IReadOnlyDictionary<string, PrefabInfo> Prefabs => Catalogue.Value;

    private static readonly string[] Grounds =
    {
        "void", "floor", "wall", "window", "door", "water", "street", "quay", "bridge", "grass", "house",
        "sand", "path", "cobble", "deck", "carpet", "tile", "stage", "pond", "flowers", "hedge", "fence", "snow",
    };
    private static readonly HashSet<string> Walkable = new()
    {
        "floor", "door", "street", "quay", "bridge", "grass", "sand", "path", "cobble", "deck", "carpet", "tile", "stage", "flowers", "snow",
    };

    private static readonly Dictionary<char, (string Ground, string? Prop)> Legend = new()
    {
        [' '] = ("void", null), ['.'] = ("floor", null), ['#'] = ("wall", null), ['w'] = ("window", null),
        ['D'] = ("door", null), ['~'] = ("water", null), ['='] = ("street", null), ['-'] = ("quay", null),
        ['b'] = ("bridge", null), ['g'] = ("grass", null), ['H'] = ("house", null), ['T'] = ("quay", "elm"),
        ['t'] = ("grass", "elm"), ['i'] = ("quay", "bollard"), ['L'] = ("quay", "street-lamp"), ['B'] = ("quay", "bike"),
        ['s'] = ("sand", null), [','] = ("path", null), ['c'] = ("cobble", null), ['d'] = ("deck", null),
        ['m'] = ("carpet", null), ['k'] = ("tile", null), ['S'] = ("stage", null), ['p'] = ("pond", null),
        ['f'] = ("flowers", null), ['h'] = ("hedge", null), ['x'] = ("fence", null), ['n'] = ("snow", null),
    };

    private static readonly Dictionary<string, string[]> Styles = new()
    {
        ["floorStyle"] = new[] { "planks", "checker", "tiles", "plain", "concrete" },
        ["wallStyle"] = new[] { "wainscot", "plain", "panel", "stripes", "brick" },
        ["light"] = new[] { "bright", "warm", "dim", "candle", "neon", "cold" },
        ["ambience"] = new[] { "dust", "fireflies", "snow", "rain", "leaves", "petals", "embers", "bubbles", "confetti" },
    };

    /// <summary>What people call things, onto what the catalogue calls them.</summary>
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["couch"] = "sofa", ["settee"] = "sofa", ["bookshelf"] = "books", ["bookcase"] = "books", ["shelves"] = "shelf", ["tree"] = "elm", ["oak"] = "elm",
        ["lamp-post"] = "street-lamp", ["lamppost"] = "street-lamp", ["streetlight"] = "street-lamp", ["bar"] = "counter", ["bar-counter"] = "counter",
        ["bar-stool"] = "stool", ["barstool"] = "stool", ["dining-table"] = "long-table", ["round-table"] = "table", ["cafe-table"] = "table",
        ["carpet"] = "rug", ["flowers"] = "planter", ["flower-pot"] = "plant", ["potted-plant"] = "plant", ["television"] = "tv", ["computer"] = "laptop",
        ["picture"] = "painting", ["art"] = "painting", ["sign"] = "neon", ["neon-sign"] = "neon", ["fire"] = "campfire", ["bonfire"] = "campfire",
        ["fire-pit"] = "campfire", ["firepit"] = "campfire", ["boat"] = "rowboat", ["records"] = "record-crates", ["record-player"] = "turntable",
        ["bookshelves"] = "library-shelves", ["fish-tank"] = "aquarium", ["drums"] = "drum-kit", ["microphone"] = "stage-mic", ["mic"] = "stage-mic",
        ["stall"] = "market-stall", ["fairy-lights"] = "string-lights", ["lights"] = "string-lights", ["candle"] = "candles", ["log"] = "log-seat",
        ["boulder"] = "rock", ["stone"] = "rock", ["hedge"] = "bush", ["shrub"] = "bush", ["bath"] = "bathtub", ["tub"] = "bathtub", ["pillar"] = "column",
        ["screen"] = "folding-screen", ["bunk"] = "bunk-bed", ["sink"] = "kitchen", ["stove"] = "kitchen", ["dj"] = "dj-booth", ["decks"] = "dj-booth",
        ["pinball"] = "arcade", ["notice-board"] = "noticeboard", ["blackboard"] = "chalkboard", ["menu"] = "chalkboard", ["cart"] = "coffee-cart",
    };

    /// <summary>A stored scene's tile map, row by row; empty for a hand-made scene.</summary>
    public static List<string> MapRows(string? stored)
        => stored is null ? new List<string>() : Rows(JsonNode.Parse(stored)?["map"]) ?? new List<string>();

    /// <summary>Rows as strings, from a string or an array of strings, padded to one width.</summary>
    public static List<string>? Rows(JsonNode? map)
    {
        var rows = map switch
        {
            JsonArray a => a.Select(r => r?.ToString() ?? string.Empty).ToList(),
            JsonValue v => v.ToString().Split('\n').ToList(),
            _ => null,
        };
        if (rows is null) return null;
        rows = rows.Select(r => r.TrimEnd()).SkipWhile(string.IsNullOrWhiteSpace).Reverse().SkipWhile(string.IsNullOrWhiteSpace).Reverse().ToList();
        if (rows.Count == 0) return null;
        var indent = rows.Where(r => r.Trim().Length > 0).Min(r => r.Length - r.TrimStart().Length);
        rows = rows.Select(r => r.Length >= indent ? r[indent..] : string.Empty).ToList();
        var w = rows.Max(r => r.Length);
        return rows.Select(r => r.PadRight(w)).ToList();
    }

    /// <summary>The map as cells, with what blocks walking; null when there's no map.</summary>
    private sealed class Grid
    {
        public required List<string> Rows;
        public required string[,] Ground;
        public required bool[,] Blocked;
        public int W => Rows[0].Length;
        public int H => Rows.Count;
        public bool In(int x, int z) => x >= 0 && z >= 0 && x < W && z < H;
        public bool IsWall(int x, int z) => In(x, z) && Ground[x, z] is "wall" or "window";
    }

    private static Dictionary<char, (string Ground, string? Prop)> LegendOf(JsonObject scene, IReadOnlyDictionary<string, PrefabInfo> prefabs, List<string>? errors)
    {
        var legend = new Dictionary<char, (string Ground, string? Prop)>(Legend);
        if (scene["legend"] is JsonObject extra)
            foreach (var (k, v) in extra)
            {
                if (k.Length != 1 || v is not JsonObject cell) { errors?.Add($"legend key '{k}' must be one character"); continue; }
                var ground = cell["ground"]?.ToString() ?? string.Empty;
                var prop = cell["prop"]?.ToString();
                if (!Grounds.Contains(ground)) errors?.Add($"legend '{k}': ground must be one of {string.Join(", ", Grounds)}");
                if (prop is not null && !prefabs.ContainsKey(prop)) errors?.Add($"legend '{k}': no prefab '{prop}'");
                legend[k[0]] = (ground, prop);
            }
        return legend;
    }

    private static Grid? GridOf(JsonObject scene, IReadOnlyDictionary<string, PrefabInfo> prefabs, List<string>? errors)
    {
        var rows = Rows(scene["map"]);
        if (rows is null) return null;
        var (h, w) = (rows.Count, rows[0].Length);
        var legend = LegendOf(scene, prefabs, errors);
        var grid = new Grid { Rows = rows, Ground = new string[w, h], Blocked = new bool[w, h] };
        for (var z = 0; z < h; z++)
            for (var x = 0; x < w; x++)
            {
                var ch = rows[z][x];
                if (!legend.TryGetValue(ch, out var cell)) { errors?.Add($"unknown map char '{ch}' at {x},{z}"); cell = ("void", null); }
                grid.Ground[x, z] = cell.Ground;
                grid.Blocked[x, z] = !Walkable.Contains(cell.Ground)
                    || (cell.Prop is { } p && prefabs.TryGetValue(p, out var pi) && pi.Solid && !pi.Wall);
            }
        return grid;
    }

    private static (int W, int D) Footprint(PrefabInfo info, JsonObject prop)
        => (int)(Num(prop["rot"]) ?? 0) % 2 == 0 ? (info.Size[0], info.Size[1]) : (info.Size[1], info.Size[0]);

    /// <summary>What's wrong with a scene for these spots; empty when it draws and every spot has room and a way in.</summary>
    /// <param name="own">The scene's own models, beside the catalogue.</param>
    public static List<string> Check(JsonObject scene, IReadOnlyList<Spot> spots, IReadOnlyDictionary<string, PrefabInfo>? own = null)
    {
        var errors = new List<string>();
        var prefabs = own is null ? Prefabs : new Dictionary<string, PrefabInfo>(Prefabs.Concat(own).GroupBy(p => p.Key).Select(g => g.Last()));
        var grid = GridOf(scene, prefabs, errors);
        if (grid is null) return new List<string> { "map is missing" };
        var (h, w) = (grid.H, grid.W);
        if (h < 5 || w < 5 || h > MaxSide || w > MaxSide) errors.Add($"map must be between 5x5 and {MaxSide}x{MaxSide}, is {w}x{h}");
        var blocked = grid.Blocked;

        var seatsBySpot = new Dictionary<string, int>();
        foreach (var prop in (scene["props"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            var name = prop["p"]?.ToString() ?? string.Empty;
            if (!prefabs.TryGetValue(name, out var info)) { errors.Add($"no prefab '{name}'{Suggest(name, prefabs.Keys)}"); continue; }
            if (Pair(prop["at"]) is not var (px, pz)) { errors.Add($"prop '{name}' needs at: [x, z]"); continue; }
            var (sw, sd) = Footprint(info, prop);
            if (px < 0 || pz < 0 || px + sw > w || pz + sd > h) { errors.Add($"prop '{name}' at {px},{pz} doesn't fit on the map ({sw}x{sd} tiles from its top-left)"); continue; }
            if (info.Solid && !info.Wall)
                for (var dz = 0; dz < sd; dz++)
                    for (var dx = 0; dx < sw; dx++)
                    {
                        if (grid.IsWall(px + dx, pz + dz)) errors.Add($"prop '{name}' at {px},{pz} sits on a wall tile ({px + dx},{pz + dz}); props go on floor");
                        blocked[px + dx, pz + dz] = true;
                    }
            if (prop["spot"]?.ToString() is { } sid) seatsBySpot[sid] = seatsBySpot.GetValueOrDefault(sid) + info.Seats;
        }

        var start = Entrances(scene, grid);
        if (start.Count == 0) errors.Add("there's no way in: put a door 'D' on the edge (or an entrances '*' tile when outdoors)");
        else if (start.All(s => blocked[s.X, s.Z])) errors.Add("the way in is blocked: keep the door or entrance tile free of props");

        var reach = Reachable(start, blocked, w, h);
        var spotDefs = scene["spots"] as JsonObject ?? new JsonObject();
        foreach (var spot in spots)
        {
            if (spotDefs[spot.Id] is not JsonObject def || Pair(def["at"]) is not var (sx, sz)) { errors.Add($"spot '{spot.Id}' has no at: [x, z] in scene.spots"); continue; }
            if (sx < 0 || sz < 0 || sx >= w || sz >= h) { errors.Add($"spot '{spot.Id}' at {sx},{sz} is off the map"); continue; }
            var r = (int)(Num(def["r"]) ?? 2);
            var room = 0;
            for (var dz = -r; dz <= r; dz++)
                for (var dx = -r; dx <= r; dx++)
                    if (sx + dx >= 0 && sz + dz >= 0 && sx + dx < w && sz + dz < h && reach[sx + dx, sz + dz]) room++;
            if (room == 0) errors.Add($"spot '{spot.Id}' at {sx},{sz} can't be reached from the door: something solid or a wall is in the way");
            else if (Math.Min(room, 8) + seatsBySpot.GetValueOrDefault(spot.Id) < spot.Capacity)
                errors.Add($"spot '{spot.Id}' needs room for {spot.Capacity}, has {room} free tiles and {seatsBySpot.GetValueOrDefault(spot.Id)} seats: clear floor around it, tag seats with \"spot\": \"{spot.Id}\", or lower its capacity");
        }
        return errors;
    }

    private static List<(int X, int Z)> Entrances(JsonObject scene, Grid grid)
    {
        var start = new List<(int X, int Z)>();
        for (var z = 0; z < grid.H; z++)
            for (var x = 0; x < grid.W; x++)
                if (grid.Ground[x, z] == "door") start.Add((x, z));
        if (scene["entrances"] is JsonObject entrances)
            foreach (var (_, v) in entrances)
                if (Pair(v) is var (ex, ez) && grid.In(ex, ez)) start.Add((ex, ez));
        return start;
    }

    /// <summary>" (did you mean sofa, k-lounge-sofa?)" for a name the catalogue doesn't have.</summary>
    private static string Suggest(string name, IEnumerable<string> names)
    {
        var close = Closest(name, names).Take(3).ToList();
        return close.Count == 0 ? string.Empty : $" (did you mean {string.Join(", ", close)}?)";
    }

    private static IEnumerable<string> Closest(string name, IEnumerable<string> names)
    {
        var words = name.ToLowerInvariant().Split('-', ' ', '_').Where(w => w.Length > 2).ToList();
        return names.Select(n => (Name: n, Score: words.Count(w => n.Contains(w)) * 3 - Distance(n, name) / 4.0))
            .Where(n => n.Score > 0 || Distance(n.Name, name) <= 2)
            .OrderByDescending(n => n.Score).Select(n => n.Name);
    }

    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    /// <summary>Mends what has one obvious fix, and says so: near-miss prefab names, things on walls, spots on furniture, unknown styles.</summary>
    private static void Mend(JsonObject scene, IReadOnlyList<Spot> spots, IReadOnlyDictionary<string, PrefabInfo> prefabs, List<string> notes)
    {
        foreach (var (key, allowed) in Styles)
            if (scene[key] is { } style && !allowed.Contains(style.ToString()))
            {
                notes.Add($"{key} '{style}' isn't one of {string.Join(", ", allowed)}; left out");
                scene.Remove(key);
            }
        foreach (var key in new[] { "floor", "wall", "accent" })
            if (scene[key] is { } colour && Library.Palette.TryGetValue(colour.ToString(), out var hex)) scene[key] = hex;

        var grid = GridOf(scene, prefabs, null);
        if (grid is null) return;
        if (scene["props"] is JsonObject) scene.Remove("props");
        var props = scene["props"] as JsonArray ?? new JsonArray();
        foreach (var prop in props.OfType<JsonObject>())
        {
            // "prefab"/"name" for "p", "x"/"z" for "at": common slips.
            if (prop["p"] is null && (prop["prefab"] ?? prop["type"]) is { } named) prop["p"] = named.ToString();
            if (prop["at"] is null && Num(prop["x"]) is { } x0 && Num(prop["z"] ?? prop["y"]) is { } z0) prop["at"] = new JsonArray((int)x0, (int)z0);
            var name = prop["p"]?.ToString() ?? string.Empty;
            if (!prefabs.ContainsKey(name))
            {
                var slug = name.Trim().ToLowerInvariant().Replace(' ', '-').Replace('_', '-');
                var fix = prefabs.ContainsKey(slug) ? slug
                    : Aliases.TryGetValue(slug, out var alias) ? alias
                    : prefabs.Keys.FirstOrDefault(k => Distance(k, slug) <= 1);
                if (fix is null) continue;
                if (fix != slug) notes.Add($"prop '{name}' read as '{fix}'");
                prop["p"] = name = fix;
            }
            if (!prefabs.TryGetValue(name, out var info) || Pair(prop["at"]) is not var (px, pz)) continue;
            var (sw, sd) = Footprint(info, prop);
            // On a wall tile: step onto the floor next to it (wall things hang on the wall behind their tile).
            if (grid.IsWall(px, pz) && sw == 1 && sd == 1)
            {
                var to = new[] { (px, pz + 1), (px + 1, pz), (px, pz - 1), (px - 1, pz) }.FirstOrDefault(t => grid.In(t.Item1, t.Item2) && Walkable.Contains(grid.Ground[t.Item1, t.Item2]), (-1, -1));
                if (to.Item1 >= 0)
                {
                    notes.Add($"prop '{name}' moved off the wall, from {px},{pz} to {to.Item1},{to.Item2}");
                    prop["at"] = new JsonArray(to.Item1, to.Item2);
                    (px, pz) = to;
                }
            }
            if (info.Solid && !info.Wall)
                for (var dz = 0; dz < sd; dz++)
                    for (var dx = 0; dx < sw; dx++)
                        if (grid.In(px + dx, pz + dz)) grid.Blocked[px + dx, pz + dz] = true;
        }

        // A solid thing on the only way in would shut the Place; nothing to mend there, Check says so.
        var reach = Reachable(Entrances(scene, grid), grid.Blocked, grid.W, grid.H);
        if (scene["spots"] is not JsonObject spotDefs) scene["spots"] = spotDefs = new JsonObject();
        foreach (var spot in spots)
        {
            var given = spotDefs[spot.Id] is JsonObject def ? Pair(def["at"]) : Pair(spotDefs[spot.Id]);
            // No tile given: next to the first thing tagged with this spot.
            given ??= props.OfType<JsonObject>().Where(p => p["spot"]?.ToString() == spot.Id).Select(p => Pair(p["at"])).FirstOrDefault(p => p is not null);
            if (given is not var (sx, sz)) continue;
            if (grid.In(sx, sz) && reach[sx, sz]) { if (spotDefs[spot.Id] is not JsonObject) spotDefs[spot.Id] = new JsonObject { ["at"] = new JsonArray(sx, sz) }; continue; }
            (int X, int Z)? best = null;
            for (var r = 1; r <= 4 && best is null; r++)
                for (var dz = -r; dz <= r && best is null; dz++)
                    for (var dx = -r; dx <= r && best is null; dx++)
                        if (grid.In(sx + dx, sz + dz) && reach[sx + dx, sz + dz]) best = (sx + dx, sz + dz);
            if (best is not { } b) continue;
            notes.Add($"spot '{spot.Id}' moved from {sx},{sz} to the free tile {b.X},{b.Z}");
            var keep = spotDefs[spot.Id] as JsonObject ?? new JsonObject();
            keep["at"] = new JsonArray(b.X, b.Z);
            if (keep.Parent is null) spotDefs[spot.Id] = keep;
        }
    }

    /// <summary>The scene's own models, cleaned in place; what they're like as prefabs.</summary>
    private static Dictionary<string, PrefabInfo> OwnModels(JsonObject scene, List<string> errors, List<string> notes)
    {
        var own = new Dictionary<string, PrefabInfo>();
        if (scene["models"] is not JsonObject models) { scene.Remove("models"); return own; }
        var cleaned = new JsonObject();
        foreach (var (rawName, raw) in models.ToList())
        {
            var name = Slug(rawName);
            if (name.Length == 0) continue;
            if (cleaned.Count >= MaxModels) { notes.Add($"model '{name}' left out: at most {MaxModels} models of your own per place"); continue; }
            if (ThingModel.CleanObject(raw) is not { } model) { errors.Add($"model '{name}' has no usable parts; see get_design_guide thing"); continue; }
            cleaned[name] = model;
            var size = model["size"] is JsonArray { Count: 2 } s ? new[] { (int)s[0]!, (int)s[1]! } : new[] { 1, 1 };
            var wall = model["wall"]?.GetValueKind() == JsonValueKind.True;
            own[name] = new PrefabInfo(name, size, !wall && model["solid"]?.GetValueKind() != JsonValueKind.False, wall,
                (model["seats"] as JsonArray)?.Count ?? 0, model["about"]?.ToString() ?? "your own model");
        }
        scene["models"] = cleaned;
        return own;
    }

    /// <summary>Things to do, lifted out of the props: the designer's own first, then what the catalogue's things afford, up to <see cref="MaxFeatures"/>.</summary>
    public static List<Feature> Featurize(JsonObject scene, IReadOnlyList<Spot> spots)
    {
        var features = new List<Feature>();
        if (scene["props"] is not JsonArray props || spots.Count == 0) return features;
        var furni = scene["furni"] as JsonObject ?? new JsonObject();
        var spotDefs = scene["spots"] as JsonObject ?? new JsonObject();
        var picked = new List<(JsonObject Prop, Offer Offer)>();
        foreach (var prop in props.OfType<JsonObject>())
            if (Library.OfferOf(prop) is { } own) picked.Add((prop, own));
        var seen = new HashSet<string>();
        foreach (var prop in props.OfType<JsonObject>())
        {
            var name = prop["p"]?.ToString() ?? string.Empty;
            if (prop["do"] is not null || !Library.Offers.TryGetValue(name, out var offer) || !seen.Add(name)) continue;
            picked.Add((prop, offer));
        }
        foreach (var (prop, offer) in picked.Take(MaxFeatures))
        {
            var prefab = prop["p"]?.ToString() ?? "thing";
            var id = Slug(prop["id"]?.ToString() ?? prefab);
            if (id.Length == 0) id = "thing";
            for (var n = 2; furni.ContainsKey(id) || HomeThings.Contains(id) || features.Any(f => f.Id == id); n++) id = $"{Slug(prefab)}-{n}";
            var name = prop["name"]?.ToString()?.Trim() is { Length: > 0 } given ? WorldStream.Clip(given, 40) : $"the {(prefab.StartsWith("k-") ? prefab[2..] : prefab).Replace('-', ' ')}";
            var spot = spots.FirstOrDefault(s => s.Id == prop["spot"]?.ToString())?.Id ?? Nearest(prop, spots, spotDefs);
            var pin = (JsonObject)prop.DeepClone();
            foreach (var key in new[] { "do", "gives", "leaves", "name", "id" }) pin.Remove(key);
            furni[id] = pin;
            props.Remove(prop);
            features.Add(new Feature(id, name, spot, offer.Do, offer.Gives, offer.Leaves));
        }
        // Whatever wasn't picked keeps no stray keys the client doesn't read.
        foreach (var prop in props.OfType<JsonObject>())
            foreach (var key in new[] { "do", "gives", "leaves" }) prop.Remove(key);
        if (features.Count > 0) scene["furni"] = furni;
        return features;
    }

    private static string Nearest(JsonObject prop, IReadOnlyList<Spot> spots, JsonObject spotDefs)
    {
        var at = Pair(prop["at"]) ?? (0, 0);
        return spots.OrderBy(s => spotDefs[s.Id] is JsonObject def && Pair(def["at"]) is var (x, z)
            ? Math.Abs(x - at.Item1) + Math.Abs(z - at.Item2) : int.MaxValue).First().Id;
    }

    private static string Slug(string s)
        => new string(s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');

    /// <summary>The map with props lettered and spots numbered, and a key underneath: what a designer without eyes gets to look at.</summary>
    public static string Preview(JsonObject scene, IReadOnlyList<Spot> spots, IReadOnlyDictionary<string, PrefabInfo>? own = null)
    {
        var prefabs = own is null ? Prefabs : new Dictionary<string, PrefabInfo>(Prefabs.Concat(own).GroupBy(p => p.Key).Select(g => g.Last()));
        var grid = GridOf(scene, prefabs, null);
        if (grid is null) return string.Empty;
        var cells = grid.Rows.Select(r => r.ToCharArray()).ToList();
        var key = new List<string>();
        const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var i = 0;
        var placed = (scene["props"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Concat((scene["furni"] as JsonObject ?? new JsonObject()).Select(f => f.Value).OfType<JsonObject>());
        foreach (var prop in placed)
        {
            if (!prefabs.TryGetValue(prop["p"]?.ToString() ?? string.Empty, out var info) || Pair(prop["at"]) is not var (px, pz)) continue;
            var mark = letters[i++ % letters.Length];
            var (sw, sd) = Footprint(info, prop);
            for (var dz = 0; dz < sd; dz++)
                for (var dx = 0; dx < sw; dx++)
                    if (grid.In(px + dx, pz + dz)) cells[pz + dz][px + dx] = mark;
            key.Add($"{mark} {info.Name} at {px},{pz}{(info.Wall ? " (on the wall)" : !info.Solid ? " (walkable)" : string.Empty)}{(prop["spot"] is { } s ? $" → {s}" : string.Empty)}");
        }
        var spotDefs = scene["spots"] as JsonObject ?? new JsonObject();
        var n = 1;
        foreach (var spot in spots)
        {
            if (spotDefs[spot.Id] is JsonObject def && Pair(def["at"]) is var (sx, sz) && grid.In(sx, sz)) cells[sz][sx] = (char)('0' + n % 10);
            key.Add($"{n++ % 10} spot {spot.Id} ({spot.Name}, room for {spot.Capacity})");
        }
        var sb = new StringBuilder();
        sb.Append("   ").AppendLine(string.Concat(Enumerable.Range(0, grid.W).Select(x => (x % 10).ToString())));
        for (var z = 0; z < grid.H; z++) sb.Append($"{z,2} ").AppendLine(new string(cells[z]));
        foreach (var line in key) sb.AppendLine(line);
        return sb.ToString().TrimEnd();
    }

    private static bool[,] Reachable(List<(int X, int Z)> start, bool[,] blocked, int w, int h)
    {
        var seen = new bool[w, h];
        var queue = new Queue<(int X, int Z)>();
        foreach (var s in start.Where(s => !blocked[s.X, s.Z])) { seen[s.X, s.Z] = true; queue.Enqueue(s); }
        while (queue.TryDequeue(out var c))
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var (x, z) = (c.X + dx, c.Z + dz);
                if (x < 0 || z < 0 || x >= w || z >= h || seen[x, z] || blocked[x, z]) continue;
                seen[x, z] = true;
                queue.Enqueue((x, z));
            }
        return seen;
    }

    private static double? Num(JsonNode? n)
        => n is JsonValue v && v.TryGetValue<double>(out var d) ? d
            : n is JsonValue v2 && v2.TryGetValue<int>(out var i) ? i
            : double.TryParse(n?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : null;

    private static (int, int)? Pair(JsonNode? n)
        => n is JsonArray { Count: >= 2 } a && Num(a[0]) is { } x && Num(a[1]) is { } z ? ((int)x, (int)z) : null;

    private static readonly string[] HomeSpots = { "sofa", "desk", "kitchen", "bed" };
    public static readonly string[] HomeThings = { FurniKinds.Plant, FurniKinds.Coffee, FurniKinds.Cups, FurniKinds.Laptop, FurniKinds.Bed };

    /// <summary>What a Home's design must hold, for how many live there.</summary>
    public static string HomeRules(int living)
        => $"It's a home {(living <= 1 ? "one person lives" : $"{living} people live")} in. Spots must include these ids, plus up to two more: {string.Join(", ", HomeSpots)}. Leave out the bed, laptop, coffee maker, dishes and plant: they get placed next to their spots. Add \"things\": {{{string.Join(", ", HomeThings.Select(t => $"\"{t}\": \"...\""))}}}, what each of those is here, with where it is (\"the dented kettle on the camping stove\").";

    public static string? MissingHomeSpots(IReadOnlyList<Spot> spots)
        => HomeSpots.Except(spots.Select(s => s.Id)).ToList() is { Count: > 0 } missing ? $"spots must include: {string.Join(", ", missing)}" : null;

    /// <summary>Asks the designer until the scene checks out, three tries at most; null when it never does.</summary>
    /// <param name="rule">What's wrong with these spots beyond the scene check; null when they're fine.</param>
    public static async Task<PlaceDraft?> DesignAsync(IWorldLlm llm, ILogger logger, string brief, bool outdoor, IReadOnlyList<string> placesNow, Func<IReadOnlyList<Spot>, string?>? rule = null)
    {
        var first = $"Design: {brief}\nPlaces that already exist: {string.Join(", ", placesNow)}";
        var user = first;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            JsonObject? json;
            try
            {
                json = await llm.CompleteJsonAsync(JobComplexity.General, Prompt(outdoor), user, CancellationToken.None, 0.8);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Designing a Place failed");
                return null;
            }
            var (draft, errors) = Validate(json, outdoor, rule);
            if (draft is not null) return draft;
            user = $"{first}\n\nYour last design:\n{json?.ToJsonString()}\n\nIt didn't work:\n- {string.Join("\n- ", errors.Take(8))}\nFix these and output the whole JSON again.";
        }
        return null;
    }

    /// <summary>A design as a draft, or what's wrong with it; the same check whoever designed it.</summary>
    public static (PlaceDraft? Draft, List<string> Errors) Validate(JsonObject? json, bool outdoor, Func<IReadOnlyList<Spot>, string?>? rule = null)
    {
        var check = Inspect(json, outdoor, rule);
        return (check.Draft, check.Errors);
    }

    /// <summary>Mend, check, and lift out the things to do; the preview comes back either way.</summary>
    public static DesignCheck Inspect(JsonObject? json, bool outdoor, Func<IReadOnlyList<Spot>, string?>? rule = null)
    {
        var notes = new List<string>();
        if (json?["scene"] is not JsonObject scene) return new DesignCheck(null, new List<string> { "there's no \"scene\" object" }, notes, string.Empty);
        var spots = SpotsOf(json["spots"] as JsonArray);
        var errors = new List<string>();
        var own = OwnModels(scene, errors, notes);
        var prefabs = new Dictionary<string, PrefabInfo>(Prefabs.Concat(own).GroupBy(p => p.Key).Select(g => g.Last()));
        if (spots.Count >= 2) Mend(scene, spots, prefabs, notes);
        errors.AddRange(spots.Count < 2 ? new List<string> { "give 2 to 6 spots, each {\"id\", \"name\"}" } : Check(scene, spots, own));
        if (rule?.Invoke(spots) is { } broken) errors.Add(broken);
        var preview = Preview(scene, spots, own);
        if (errors.Count > 0) return new DesignCheck(null, errors, notes, preview);
        if (outdoor) scene["outdoor"] = true;
        var features = Featurize(scene, spots);
        return new DesignCheck(new PlaceDraft(json.ToJsonString(), Store(scene), spots, features), errors, notes, preview);
    }

    /// <summary>The scene as stored: map rows joined, the rest as the model gave it.</summary>
    public static string Store(JsonObject scene)
    {
        var copy = (JsonObject)scene.DeepClone();
        copy["map"] = string.Join("\n", Rows(scene["map"]) ?? new List<string>());
        return copy.ToJsonString();
    }

    /// <summary>Spots from the design, cleaned: kebab ids, sane capacity, known poses.</summary>
    public static List<Spot> SpotsOf(JsonArray? raw)
    {
        var known = new[] { Poses.Lean, Poses.Lounge, Poses.Perch, Poses.Dance, Poses.Lie, Poses.CrossLegged };
        var spots = new List<Spot>();
        foreach (var s in (raw ?? new JsonArray()).OfType<JsonObject>().Take(6))
        {
            var id = new string((s["id"]?.ToString() ?? string.Empty).ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
            var name = s["name"]?.ToString()?.Trim() ?? string.Empty;
            if (id.Length == 0 || name.Length == 0 || spots.Any(x => x.Id == id)) continue;
            var at = s["at"]?.ToString() is "on" or "in" or "by" ? s["at"]!.ToString() : "at";
            spots.Add(new Spot
            {
                Id = id, Name = WorldStream.Clip(name, 40), At = at,
                Capacity = Math.Clamp((int)(Num(s["capacity"]) ?? 2), 1, 6),
                Seat = s["seat"]?.GetValueKind() == JsonValueKind.True,
                Poses = (s["poses"] as JsonArray ?? new JsonArray()).Select(p => p?.ToString() ?? string.Empty).Where(known.Contains).Distinct().ToList(),
            });
        }
        return spots;
    }

}
