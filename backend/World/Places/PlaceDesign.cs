using System.Text.Json;
using System.Text.Json.Nodes;

namespace PartyTown.World.Places;

/// <summary>One prefab the client can draw (shared with the frontend, which tests it stays in sync).</summary>
public sealed record PrefabInfo(string Name, int[] Size, bool Solid, bool Wall, int Seats, string About);

/// <summary>Generated Places (ADR 0023): the model designs spots and a scene; this checks the scene draws and every spot can be reached.</summary>
public static class PlaceDesign
{
    public const int MaxSide = 30;

    private static readonly Lazy<Dictionary<string, PrefabInfo>> Catalogue = new(() =>
    {
        using var s = typeof(PlaceDesign).Assembly.GetManifestResourceStream("PartyTown.World.Places.prefabs.json")!;
        var list = JsonSerializer.Deserialize<List<PrefabInfo>>(s, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return list.ToDictionary(p => p.Name);
    });

    public static IReadOnlyDictionary<string, PrefabInfo> Prefabs => Catalogue.Value;

    private static readonly string[] Grounds = { "void", "floor", "wall", "window", "door", "water", "street", "quay", "bridge", "grass", "house" };
    private static readonly HashSet<string> Walkable = new() { "floor", "door", "street", "quay", "bridge", "grass" };

    private static readonly Dictionary<char, (string Ground, string? Prop)> Legend = new()
    {
        [' '] = ("void", null), ['.'] = ("floor", null), ['#'] = ("wall", null), ['w'] = ("window", null),
        ['D'] = ("door", null), ['~'] = ("water", null), ['='] = ("street", null), ['-'] = ("quay", null),
        ['b'] = ("bridge", null), ['g'] = ("grass", null), ['H'] = ("house", null), ['T'] = ("quay", "elm"),
        ['t'] = ("grass", "elm"), ['i'] = ("quay", "bollard"), ['L'] = ("quay", "street-lamp"), ['B'] = ("quay", "bike"),
    };

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

    /// <summary>What's wrong with a scene for these spots; empty when it draws and every spot has room and a way in.</summary>
    public static List<string> Check(JsonObject scene, IReadOnlyList<Spot> spots)
    {
        var errors = new List<string>();
        var rows = Rows(scene["map"]);
        if (rows is null) return new List<string> { "map is missing" };
        var (h, w) = (rows.Count, rows[0].Length);
        if (h < 5 || w < 5 || h > MaxSide || w > MaxSide) errors.Add($"map must be between 5x5 and {MaxSide}x{MaxSide}, is {w}x{h}");

        var legend = new Dictionary<char, (string Ground, string? Prop)>(Legend);
        if (scene["legend"] is JsonObject extra)
            foreach (var (k, v) in extra)
            {
                if (k.Length != 1 || v is not JsonObject cell) { errors.Add($"legend key '{k}' must be one character"); continue; }
                var ground = cell["ground"]?.ToString() ?? string.Empty;
                var prop = cell["prop"]?.ToString();
                if (!Grounds.Contains(ground)) errors.Add($"legend '{k}': ground must be one of {string.Join(", ", Grounds)}");
                if (prop is not null && !Prefabs.ContainsKey(prop)) errors.Add($"legend '{k}': no prefab '{prop}'");
                legend[k[0]] = (ground, prop);
            }

        var blocked = new bool[w, h];
        var ground2 = new string[w, h];
        for (var z = 0; z < h; z++)
            for (var x = 0; x < w; x++)
            {
                var ch = rows[z][x];
                if (!legend.TryGetValue(ch, out var cell)) { errors.Add($"unknown map char '{ch}' at {x},{z}"); cell = ("void", null); }
                ground2[x, z] = cell.Ground;
                blocked[x, z] = !Walkable.Contains(cell.Ground)
                    || (cell.Prop is { } p && Prefabs.TryGetValue(p, out var pi) && pi.Solid && !pi.Wall);
            }

        var seatsBySpot = new Dictionary<string, int>();
        foreach (var prop in (scene["props"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            var name = prop["p"]?.ToString() ?? string.Empty;
            if (!Prefabs.TryGetValue(name, out var info)) { errors.Add($"no prefab '{name}'"); continue; }
            if (Pair(prop["at"]) is not var (px, pz)) { errors.Add($"prop '{name}' needs at: [x, z]"); continue; }
            var rot = (int)(Num(prop["rot"]) ?? 0);
            var (sw, sd) = rot % 2 == 0 ? (info.Size[0], info.Size[1]) : (info.Size[1], info.Size[0]);
            if (px < 0 || pz < 0 || px + sw > w || pz + sd > h) { errors.Add($"prop '{name}' at {px},{pz} doesn't fit on the map"); continue; }
            if (info.Solid && !info.Wall)
                for (var dz = 0; dz < sd; dz++)
                    for (var dx = 0; dx < sw; dx++)
                        blocked[px + dx, pz + dz] = true;
            if (prop["spot"]?.ToString() is { } sid) seatsBySpot[sid] = seatsBySpot.GetValueOrDefault(sid) + info.Seats;
        }

        var start = new List<(int X, int Z)>();
        for (var z = 0; z < h; z++)
            for (var x = 0; x < w; x++)
                if (ground2[x, z] == "door") start.Add((x, z));
        if (scene["entrances"] is JsonObject entrances)
            foreach (var (_, v) in entrances)
                if (Pair(v) is var (ex, ez) && ex >= 0 && ez >= 0 && ex < w && ez < h) start.Add((ex, ez));
        if (start.Count == 0) errors.Add("there's no way in: put a door 'D' on the edge (or an entrances '*' tile when outdoors)");

        var reach = Reachable(start, blocked, w, h);
        var spotDefs = scene["spots"] as JsonObject ?? new JsonObject();
        foreach (var spot in spots)
        {
            if (spotDefs[spot.Id] is not JsonObject def || Pair(def["at"]) is not var (sx, sz)) { errors.Add($"spot '{spot.Id}' has no at: [x, z]"); continue; }
            if (sx < 0 || sz < 0 || sx >= w || sz >= h) { errors.Add($"spot '{spot.Id}' at {sx},{sz} is off the map"); continue; }
            var r = (int)(Num(def["r"]) ?? 2);
            var room = 0;
            for (var dz = -r; dz <= r; dz++)
                for (var dx = -r; dx <= r; dx++)
                    if (sx + dx >= 0 && sz + dz >= 0 && sx + dx < w && sz + dz < h && reach[sx + dx, sz + dz]) room++;
            if (room == 0) errors.Add($"spot '{spot.Id}' at {sx},{sz} can't be reached from the door");
            else if (Math.Min(room, 8) + seatsBySpot.GetValueOrDefault(spot.Id) < spot.Capacity)
                errors.Add($"spot '{spot.Id}' needs room for {spot.Capacity}, has {room}");
        }
        return errors;
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

    public static string Prompt(bool outdoor) => $$$"""
        You design one new place in a small, cosy low-poly 3D world seen from above at an angle (like Habbo Hotel). People hang out there.
        Output ONLY JSON:
        {"name": "the Rooftop Bar", "at": "at", "description": "one sentence a visitor would notice", "purpose": "what people come here for, one plain line", "spots": [...], "scene": {...}}

        "at" is the preposition: "at the Rooftop Bar", "in the park".
        "purpose": plain words, no slogan: "where people go for a drink after work".
        spots: 3 to 5 named places to be, what people say they're at: {"id": "bar", "name": "the bar", "at": "at|on|in|by", "capacity": 2-5, "seat": true|false, "poses": [...]}
          poses it affords, from: lean, lounge, perch, dance, lie, cross-legged.

        scene:
          "map": array of equal-length strings, 8 to 20 wide and 7 to 16 tall. One char per tile, rows go away from the viewer.
          Legend: ' ' nothing, '.' floor, '#' wall, 'w' wall with window, 'D' door (walkable), '~' water, '=' street, '-' paving,
                  'b' bridge, 'g' grass, 'H' narrow canal house, 'T' tree on paving, 't' tree on grass, 'i' bollard, 'L' street lamp, 'B' parked bike.
          {{{(outdoor
            ? "Outdoors: no walls; edge it with 'H' houses or water at the top and left rows, and add \"entrances\": {\"*\": [x, z]} on a walkable edge tile, and \"outdoor\": true."
            : "Indoors: surround the floor with '#'/'w' walls and put one 'D' door in the left or bottom wall. Only the top and left walls are drawn tall, so put windows there.")}}}
          "props": [{"p": "prefab", "at": [x, z], "rot": 0-3, "spot": "spot id if people use its seats", "y": lift onto a counter or desk, "text": "for neon signs"}]
            at = the prop's top-left tile; rot = quarter turns (0 faces down the map, toward the viewer). Props mustn't sit on walls (except wall ones, on the tile in front of a top or left wall).
          "spots": {"spot id": {"at": [x, z]}}  a walkable tile at the middle of each spot, with free floor around it.
          "floor": "#rrggbb", "wall": "#rrggbb"
          Leave a walkable path from the door to every spot.

        Prefabs (name, footprint wide x deep, seats):
        {{{string.Join("\n", Prefabs.Values.Select(p => $"  {p.Name} {p.Size[0]}x{p.Size[1]}{(p.Seats > 0 ? $" seats {p.Seats}" : "")}: {p.About}"))}}}

        Example (a café):
        {"name": "the Café", "at": "at", "description": "a small café that smells of coffee", "purpose": "where people get coffee and linger", "spots": [{"id": "counter", "name": "the counter", "at": "at", "capacity": 3, "seat": false, "poses": ["lean"]}, {"id": "booth", "name": "the corner booth", "at": "in", "capacity": 3, "seat": true, "poses": ["lounge"]}],
         "scene": {"map": ["##ww#######", "#.........#", "#.........#", "#.........#", "D.........#", "#.........#", "###########"],
                   "props": [{"p": "booth", "at": [1, 1], "spot": "booth"}, {"p": "counter", "at": [6, 1]}, {"p": "espresso", "at": [8, 1], "y": 1.04}, {"p": "stool", "at": [6, 2], "rot": 2, "spot": "counter"}, {"p": "big-plant", "at": [1, 5]}],
                   "spots": {"counter": {"at": [7, 2]}, "booth": {"at": [2, 3]}}, "floor": "#b98a5a", "wall": "#e6d7b8"}}
        """;
}
