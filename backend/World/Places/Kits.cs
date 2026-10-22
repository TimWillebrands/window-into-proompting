using System.Text.RegularExpressions;

namespace PartyTown.World.Places;

/// <summary>An archetype a Locale opens as (ADR 0027): spots, purpose and a description, chosen not designed.</summary>
public sealed record Kit(string Archetype, string Purpose, string Description, string At, string[] Keywords, Spot[] Spots);

/// <summary>The kits a Locale can open from at Sketched fidelity; drawn by the client's generated room until a scene kit exists.</summary>
public static class Kits
{
    private static Spot S(string id, string name, int capacity, string at = "at", bool seat = false, bool staff = false)
        => new() { Id = id, Name = name, Capacity = capacity, At = at, Seat = seat, Staff = staff };

    public static readonly Kit[] All =
    {
        new("office", "where people work at desks and grab coffee between meetings", "an open-plan office with rows of desks", "at",
            new[] { "office", "work", "agency", "firm", "headquarters", "hq", "coworking", "bank" },
            new[] { S("desks", "the desks", 4, seat: true, staff: true), S("coffee", "the coffee corner", 3), S("meeting", "the meeting room", 4, "in", seat: true), S("window", "the window", 2) }),
        new("cafe", "where people get coffee and linger", "a small café with a counter and a few tables", "at",
            new[] { "café", "cafe", "coffee", "bakery", "lunchroom", "tearoom", "patisserie" },
            new[] { S("counter", "the counter", 3, staff: true), S("window-table", "the window table", 2, seat: true), S("big-table", "the big table", 4, seat: true) }),
        new("bar", "where people go for a drink", "a bar with a long counter and a corner sofa", "at",
            new[] { "bar", "pub", "brewery", "brouwerij", "club", "lounge", "café bar" },
            new[] { S("bar", "the bar", 4, staff: true), S("high-tables", "the high tables", 4), S("sofa", "the corner sofa", 3, "on", seat: true) }),
        new("restaurant", "where people go out to eat", "a restaurant with a row of tables and an open kitchen", "at",
            new[] { "restaurant", "diner", "eatery", "pizzeria", "bistro", "kitchen", "ramen", "sushi" },
            new[] { S("pass", "the kitchen pass", 2, staff: true), S("tables", "the tables", 4, seat: true), S("booth", "the booth", 4, "in", seat: true) }),
        new("shop", "where people shop and browse", "a shop with a till and crowded shelves", "at",
            new[] { "shop", "store", "supermarket", "boutique", "bookshop", "bookstore", "market", "albert heijn", "mall" },
            new[] { S("till", "the till", 2, staff: true), S("aisles", "the aisles", 4, "in"), S("window", "the shop window", 2, "by") }),
        new("gym", "where people work out", "a gym with machines, mats and a front desk", "at",
            new[] { "gym", "fitness", "climbing", "boulder", "pool", "yoga", "dojo", "sports" },
            new[] { S("front-desk", "the front desk", 2, staff: true), S("machines", "the machines", 4, "by"), S("mats", "the mats", 4, "on") }),
        new("park", "where people walk, sit in the grass and get some air", "a park with lawns, a pond and benches", "in",
            new[] { "park", "garden", "beach", "square", "plein", "field", "woods", "forest", "zoo" },
            new[] { S("bench", "the bench", 3, "on", seat: true), S("lawn", "the lawn", 5, "on", seat: true), S("pond", "the pond", 3, "by") }),
        new("station", "where people catch trains and wait", "a station hall with platforms and a kiosk", "at",
            new[] { "station", "platform", "airport", "terminal", "tram stop", "bus stop" },
            new[] { S("platform", "the platform", 5, "on"), S("kiosk", "the kiosk", 2, staff: true), S("benches", "the benches", 3, "on", seat: true) }),
        new("school", "where people study and teach", "classrooms off a long hallway, and a quiet library", "at",
            new[] { "school", "university", "uni", "college", "campus", "library", "academy", "lecture" },
            new[] { S("classroom", "the classroom", 5, "in", seat: true), S("hallway", "the hallway", 4, "in"), S("library", "the library tables", 4, "at", seat: true) }),
        new("clinic", "where people go to see a doctor", "a clinic with a reception desk and a waiting room", "at",
            new[] { "clinic", "hospital", "doctor", "dentist", "gp", "pharmacy", "physio" },
            new[] { S("reception", "the reception desk", 2, staff: true), S("waiting", "the waiting room", 5, "in", seat: true) }),
    };

    public static readonly Kit Place = new("place", "where people come and go", "a plain room with a few places to sit", "at",
        Array.Empty<string>(),
        new[] { S("entrance", "the entrance", 3), S("main", "the main room", 5, "in"), S("corner", "the corner", 2, seat: true) });

    public static Kit Of(string? archetype) => All.FirstOrDefault(k => k.Archetype == archetype) ?? Place;

    /// <summary>The archetype a name says plainly ("the climbing gym" → gym); null when it doesn't.</summary>
    public static string? FromName(string name)
    {
        var n = $" {Regex.Replace(name.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim()} ";
        return All.FirstOrDefault(k => k.Keywords.Any(w => n.Contains($" {w} ", StringComparison.Ordinal)))?.Archetype;
    }

    /// <summary>Fresh spots from a kit, poses filled.</summary>
    public static List<Spot> SpotsOf(Kit kit)
    {
        var spots = kit.Spots.Select(s => new Spot { Id = s.Id, Name = s.Name, Capacity = s.Capacity, At = s.At, Seat = s.Seat, Staff = s.Staff }).ToList();
        Poses.Fill(spots);
        return spots;
    }
}
