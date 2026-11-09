using System.Text;

namespace PartyTown.World.Places;

public static partial class PlaceDesign
{
    /// <summary>A whole indoor design that checks out; the guide shows it and a test keeps it true.</summary>
    public const string IndoorExample = """
        {"name": "the Groove Cellar", "at": "at", "description": "a record shop that turned into a listening room, warm and a bit dusty", "purpose": "where people dig through records and stay for one more side",
         "spots": [{"id": "crates", "name": "the record crates", "at": "at", "capacity": 3, "seat": false, "poses": ["lean"]},
                   {"id": "sofas", "name": "the listening corner", "at": "in", "capacity": 4, "seat": true, "poses": ["lounge", "cross-legged"]},
                   {"id": "counter", "name": "the counter", "at": "at", "capacity": 3, "seat": true, "poses": ["lean", "perch"]}],
         "scene": {"map": ["###ww###ww##", "#..........#", "#..........#", "w..........#", "#....mmm...#", "#....mmm...#", "D....mmm...#", "#..........#", "############"],
                   "props": [{"p": "record-crates", "at": [1, 1], "spot": "crates"}, {"p": "record-crates", "at": [2, 1], "tint": {"sleeve": "teal"}},
                             {"p": "lounge-set", "at": [5, 4], "spot": "sofas"}, {"p": "bar-set", "at": [7, 1], "spot": "counter"},
                             {"p": "turntable", "at": [8, 1], "y": 1.04, "name": "the shop turntable", "do": "Put a record on the shop turntable", "gives": "the record they choose and the first seconds of it"},
                             {"p": "neon", "at": [5, 1], "text": "VINYL"}, {"p": "poster", "at": [1, 5]}, {"p": "paper-lantern", "at": [6, 3]}, {"p": "big-plant", "at": [10, 7]}],
                   "spots": {"crates": {"at": [2, 2]}, "sofas": {"at": [6, 6]}, "counter": {"at": [8, 3]}},
                   "floor": "#8a5a3c", "wall": "#d9cbb0", "accent": "#7a2f3d", "floorStyle": "planks", "wallStyle": "panel", "light": "warm", "ambience": "dust"}}
        """;

    /// <summary>A whole outdoor design that checks out.</summary>
    public const string OutdoorExample = """
        {"name": "the Wishing Garden", "at": "in", "description": "a walled garden behind the canal houses, with a pond and a tree full of paper wishes", "purpose": "where people sit by the pond and leave a wish",
         "spots": [{"id": "pond", "name": "the bench by the pond", "at": "on", "capacity": 2, "seat": true, "poses": ["lounge"]},
                   {"id": "tree", "name": "the wishing tree", "at": "by", "capacity": 3, "seat": false, "poses": ["lean"]},
                   {"id": "picnic", "name": "the picnic table", "at": "at", "capacity": 4, "seat": true, "poses": ["perch"]}],
         "scene": {"outdoor": true,
                   "map": ["HHHHHHHHHHHH", "gggtgggggfff", "g,,,,,,,,ffg", "g,gggggg,ggg", "g,gppppg,gtg", "g,gppppg,ggg", "g,,,,,,,,ggg", "ggg,gggggggg", "hhh,hhhhhhhh"],
                   "entrances": {"*": [3, 8]},
                   "props": [{"p": "bench", "at": [4, 3], "spot": "pond"}, {"p": "reeds", "at": [6, 5]}, {"p": "wishing-tree", "at": [9, 5], "spot": "tree"},
                             {"p": "picnic-table", "at": [9, 2], "spot": "picnic"}, {"p": "string-lights", "at": [5, 7]}, {"p": "lantern", "at": [2, 7]}],
                   "spots": {"pond": {"at": [5, 2]}, "tree": {"at": [8, 6]}, "picnic": {"at": [8, 3]}},
                   "ambience": "petals"}}
        """;

    /// <summary>The catalogue by group; ★ marks things that come with something to do.</summary>
    public static string CatalogueText()
    {
        var sb = new StringBuilder();
        var listed = new HashSet<string>();
        string Line(PrefabInfo p) => $"    {p.Name}{(Library.Offers.ContainsKey(p.Name) ? " ★" : "")} {p.Size[0]}x{p.Size[1]}{(p.Seats > 0 ? $" seats {p.Seats}" : "")}{(p.Wall ? " wall" : !p.Solid ? " walkable" : "")}: {p.About}";
        foreach (var (group, names) in Library.Groups())
        {
            var infos = names.Where(n => Prefabs.ContainsKey(n) && listed.Add(n)).Select(n => Prefabs[n]).ToList();
            if (infos.Count == 0) continue;
            sb.AppendLine($"  {group}:");
            foreach (var p in infos) sb.AppendLine(Line(p));
        }
        var rest = Prefabs.Values.Where(p => !listed.Contains(p.Name)).ToList();
        if (rest.Count > 0) sb.AppendLine("  more:");
        foreach (var p in rest) sb.AppendLine(Line(p));
        return sb.ToString().TrimEnd();
    }

    public static string Prompt(bool outdoor) => $$$"""
        You design one new place in a small, cosy low-poly 3D world seen from above at an angle (like Habbo Hotel). People walk in, hang out at its spots, and do things there.
        Output ONLY JSON:
        {"name": "the Rooftop Bar", "at": "at", "description": "one sentence a visitor would notice", "purpose": "what people come here for, one plain line", "spots": [...], "scene": {...}}
        "at" is the preposition: "at the Rooftop Bar", "in the park". "purpose": plain words, no slogan: "where people go for a drink after work".

        THE SHORT WAY (this alone makes a good place)
        1. Map: "map" is an array of equal-length strings, 8 to 20 wide and 7 to 16 tall, one char per tile. The first row is the back; x counts right, z counts down. The viewer looks from the bottom-right.
           {{{(outdoor
            ? "Outdoors: no walls. Edge the back row and left column with 'H' houses, 'h' hedge, 'x' fence or water. Add \"entrances\": {\"*\": [x, z]} on a walkable edge tile and \"outdoor\": true."
            : "Indoors: '.' floor, ringed by '#' walls with a few 'w' windows in the top row and left column (only those walls are drawn tall), and one 'D' door in the left or bottom wall.")}}}
        2. Spots: 3 to 5 named places to be, what people say they're at:
           {"id": "bar", "name": "the bar", "at": "at|on|in|by", "capacity": 2-5, "seat": true|false, "poses": ["lean", "lounge", "perch", "dance", "lie", "cross-legged"]}
           and where each one is: scene "spots": {"bar": {"at": [x, z]}}, a free floor tile with free floor around it.
        3. Furnish: scene "props": [{"p": "prefab name", "at": [x, z], "rot": 0-3, "spot": "spot id"}]
           "at" is the prop's top-left tile; it covers its footprint (wide x deep) from there. "rot" is quarter turns; 0 faces the viewer (down the map). "spot" lends its seats to that spot.
           Start from sets: one prop is a whole spot (dining-set, cafe-set, lounge-set, bar-set, desk-set, picnic-table).
           Small things go on a counter or table with "y": 1.04 (counter), 0.76 (table), 0.79 (desk).
           Wall things (marked wall) go on the floor tile in front of a top or left wall. Keep a walkable path from the way in to every spot.
        4. Something to do: things marked ★ come with something to do there; place one or two. Or give any prop its own:
           {"p": "k-shelves", "at": [3, 1], "name": "the seed drawers", "do": "Open one of the seed drawers", "gives": "which seed is in it and what the packet says", "leaves": false}
           "do" starts with a verb, one action. "gives" says what someone comes away with: a concrete find, never a feeling. "leaves": true when what they do stays for the next visitor to find (a wish, a note, a move).
           Up to {{{MaxFeatures}}} per place. This is what makes people come back, so make it fit what the place is for.
        5. Mood: "floor", "wall", "accent": "#rrggbb"; "floorStyle": {{{string.Join(" | ", Styles["floorStyle"])}}}; "wallStyle": {{{string.Join(" | ", Styles["wallStyle"])}}};
           "light": {{{string.Join(" | ", Styles["light"])}}} (dim, candle and neon keep it night inside, lamps on); "ambience": {{{string.Join(" | ", Styles["ambience"])}}}.

        MAP LEGEND
          ' ' nothing   '.' floor   '#' wall   'w' window   'D' door
          'm' carpet (accent colour)   'k' tiles   'S' stage (raised, walkable)
          'g' grass   'f' flowers   ',' dirt path   's' sand   'c' cobbles   'd' wooden deck   '-' paving   '=' street   'n' snow   'b' bridge
          '~' canal water   'p' pond   'h' hedge   'x' fence   'H' canal house       (water, pond, hedge, fence and houses block walking)
          't' tree on grass   'T' tree on paving   'L' street lamp   'i' bollard   'B' parked bike

        PREFABS (name, footprint wide x deep, seats; ★ = comes with something to do)
        {{{CatalogueText()}}}
          Tints recolour a prop: "tint": {"fabric": "#2f5d50"} (sofa, armchair: fabric; rug: rug; chair: wood; many library things have their own). "text" sets a neon's words.

        GOING FURTHER (optional)
          Your own models: scene "models": {"my-totem": {"size": [1, 1], "parts": [...]}} and then {"p": "my-totem", "at": [...]} like any prefab. Up to {{{MaxModels}}}; the part format is the thing guide's
          (box, cyl, cone, ball, lathe, extrude, tube, group, use, array, spin, mirror, glow, tag for motion, seats, light).
          Your own letters: scene "legend": {"Q": {"ground": "grass", "prop": "pine"}}.
          Rooms within a room: '#' inside the floor is drawn as a low partition; leave a '.' gap as the doorway.

        What comes back when it doesn't check out is a list of what's wrong and the map with props lettered and spots numbered. Small slips (a near-miss prefab name, a spot on top of furniture) are mended for you and noted.

        Example:
        {{{(outdoor ? OutdoorExample : IndoorExample)}}}
        """;
}
