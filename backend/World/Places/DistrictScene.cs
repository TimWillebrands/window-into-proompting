using System.Text.Json.Nodes;

namespace PartyTown.World.Places;

/// <summary>A District's own streets (ADR 0027), built not designed; checked by <see cref="PlaceDesign.Check"/> like any made scene.</summary>
public static class DistrictScene
{
    public const int MinVenues = 2;
    public const int MaxShown = 4;
    /// <summary>Homes in a District that earn it streets without any venue (lives-in anchors).</summary>
    public const int MinResidents = 3;
    private const int Height = 12;
    private static readonly string[] Towers = { "k-office-block", "k-skyscraper", "k-small-building", "k-low-building" };

    public static List<Spot> Spots()
    {
        var spots = new List<Spot>
        {
            new() { Id = "tram-stop", Name = "the tram stop", Capacity = 4 },
            new() { Id = "square", Name = "the square", At = "on", Capacity = 5 },
            new() { Id = "bench", Name = "the bench on the square", At = "on", Capacity = 3, Seat = true },
        };
        Poses.Fill(spots);
        return spots;
    }

    /// <summary>The scene for these venues; the same District always gets the same towers.</summary>
    public static JsonObject Build(string districtId, IReadOnlyList<Place> venues)
    {
        var shown = venues.Take(MaxShown).ToList();
        var slots = new List<Place?>();
        foreach (var v in shown)
        {
            slots.Add(null);
            slots.Add(v);
        }
        slots.Add(null);
        var width = Math.Min(PlaceDesign.MaxSide, slots.Count * 3 + 2);
        var rows = Enumerable.Range(0, Height).Select(_ => new char[width]).ToArray();
        for (var x = 0; x < width; x++)
        {
            for (var z = 0; z <= 3; z++) rows[z][x] = '-';
            rows[4][x] = '=';
            rows[5][x] = '=';
            rows[6][x] = x % 6 == 3 ? 'L' : '-';
            for (var z = 7; z <= 10; z++) rows[z][x] = 'g';
            rows[11][x] = '-';
        }
        for (var z = 6; z <= 10; z++) rows[z][width / 2] = '-';

        var legend = new JsonObject();
        var props = new JsonArray();
        var entrances = new JsonObject();
        var seed = districtId.Sum(c => c);
        for (var i = 0; i < slots.Count; i++)
        {
            var x = 1 + i * 3;
            if (x + 3 > width) break;
            if (slots[i] is { } venue)
            {
                var ch = (char)('1' + legend.Count);
                for (var z = 0; z <= 2; z++)
                    for (var dx = 0; dx < 3; dx++)
                        rows[z][x + dx] = z == 2 && dx == 1 ? ch : 'H';
                legend[ch.ToString()] = new JsonObject
                {
                    ["ground"] = "house",
                    ["house"] = new JsonObject { ["sign"] = Sign(venue), ["storeys"] = 3, ["awning"] = "#3d6b8c" },
                };
                entrances[EntranceKey(venue)] = new JsonArray(x + 1, 3);
            }
            else
            {
                props.Add(new JsonObject { ["p"] = Towers[(seed + i) % Towers.Length], ["at"] = new JsonArray(x, 0) });
            }
        }
        props.Add(new JsonObject { ["p"] = "k-park-bench", ["at"] = new JsonArray(2, 8), ["spot"] = "bench" });
        props.Add(new JsonObject { ["p"] = "k-tree-large", ["at"] = new JsonArray(width - 4, 8) });
        entrances["*"] = new JsonArray(width / 2, 11);
        return new JsonObject
        {
            ["outdoor"] = true,
            ["map"] = string.Join("\n", rows.Select(r => new string(r))),
            ["legend"] = legend,
            ["props"] = props,
            ["spots"] = new JsonObject
            {
                ["tram-stop"] = new JsonObject { ["at"] = new JsonArray(width / 2, 6) },
                ["square"] = new JsonObject { ["at"] = new JsonArray(width / 2 + 2, 9) },
                ["bench"] = new JsonObject { ["at"] = new JsonArray(3, 9) },
            },
            ["entrances"] = entrances,
            ["focus"] = new JsonArray(width / 2, 6),
        };
    }

    /// <summary>The client finds a door by the id of the Place a Body comes from (scene names are shared by every kit venue).</summary>
    public static string EntranceKey(Place venue) => venue.Id.ToString();

    /// <summary>"the climbing gym" → GYM; long last words fall back to the archetype.</summary>
    public static string Sign(Place venue)
    {
        var last = venue.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? venue.Name;
        var word = last.Length <= 9 ? last : venue.Archetype ?? last[..9];
        return word.ToUpperInvariant();
    }
}
