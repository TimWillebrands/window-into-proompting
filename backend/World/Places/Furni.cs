namespace PartyTown.World.Places;

/// <summary>A placed object with state (spec phase 4). Decay is computed from timestamps, never ticked.</summary>
[GenerateSerializer, Alias("PartyTown.World.Places.Furni")]
public sealed class Furni
{
    [Id(0)] public string Id { get; set; } = string.Empty;
    /// <summary>One of <see cref="FurniKinds"/>.</summary>
    [Id(1)] public string Kind { get; set; } = string.Empty;
    /// <summary>How it's referred to, with where it is: "the plant by the window".</summary>
    [Id(2)] public string Name { get; set; } = string.Empty;
    [Id(3)] public string SpotId { get; set; } = string.Empty;
    /// <summary>Last watered/cleaned/fixed.</summary>
    [Id(4)] public long TendedAt { get; set; }
    [Id(5)] public bool Broken { get; set; }
    [Id(6)] public int Dirty { get; set; }
    [Id(7)] public long? OnUntil { get; set; }
    /// <summary>What it's doing while on, e.g. the song on the jukebox.</summary>
    [Id(8)] public string? Playing { get; set; }
    [Id(9)] public string? PlayedBy { get; set; }
    /// <summary>Decor only: poster, painting, plant, rug, lamp, books, guitar, photo or thing.</summary>
    [Id(10)] public string? Style { get; set; }
    /// <summary>Generated 3D model (parts JSON, see <see cref="ThingModel"/>).</summary>
    [Id(11)] public string? Model { get; set; }
    /// <summary>Where its owner put it; null = the scene decides.</summary>
    [Id(12)] public int? TileX { get; set; }
    [Id(13)] public int? TileZ { get; set; }
    /// <summary>Quarter turns.</summary>
    [Id(14)] public int Rot { get; set; }
    /// <summary>Who it belongs to (put up, bought, made).</summary>
    [Id(15)] public Guid? OwnerId { get; set; }
    /// <summary>A catalogue model it's drawn as (ADR 0027), picked from its description; wins over <see cref="Model"/>.</summary>
    [Id(16)] public string? Prefab { get; set; }
}

public static class FurniKinds
{
    public const string Plant = "plant";
    public const string Cups = "cups";
    public const string Coffee = "coffee";
    public const string Jukebox = "jukebox";
    public const string Lamp = "lamp";
    public const string Laptop = "laptop";
    public const string Bed = "bed";
    public const string Decor = "decor";
}

/// <summary>Making a Home yours (spec phase 8): what someone puts up, from their own words.</summary>
public static class Decor
{
    public const int PerDay = 2;
    public const int MaxInHome = 8;

    private static readonly (string Style, string[] Words)[] Styles =
    {
        ("poster", new[] { "poster", "print" }),
        ("painting", new[] { "painting", "canvas", "artwork", "art" }),
        ("photo", new[] { "photo", "picture", "polaroid", "frame" }),
        ("plant", new[] { "plant", "monstera", "cactus", "fern", "flowers", "succulent" }),
        ("rug", new[] { "rug", "carpet", "mat" }),
        ("lamp", new[] { "lamp", "lights", "candle", "fairy" }),
        ("books", new[] { "book", "books", "shelf", "bookshelf", "records", "vinyl" }),
        ("guitar", new[] { "guitar", "piano", "keyboard", "instrument", "ukulele" }),
    };

    /// <summary>The style a description reads as; "thing" when nothing matches.</summary>
    public static string StyleOf(string description)
    {
        var lower = description.ToLowerInvariant();
        return Styles.FirstOrDefault(s => s.Words.Any(w => lower.Contains(w))).Style ?? "thing";
    }

    /// <summary>"I'll hang a vintage Bowie poster over the sofa." → "a vintage Bowie poster over the sofa".</summary>
    public static string Label(string description)
    {
        var t = System.Text.RegularExpressions.Regex.Replace(description.Trim(),
            @"^(i('ll| will| want to| think i'll)?\s+)?(finally\s+)?(put up|hang( up)?|put|place|set up|add|bring out|buy|get)\s+",
            string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        t = t.TrimEnd('.', '!', ' ');
        return WorldStream.Clip(t.Length > 0 ? t : description, 60);
    }

    /// <summary>How many were put up since local midnight.</summary>
    public static int PlacedToday(IEnumerable<Furni> furni, long now, long localMidnight)
        => furni.Count(f => f.Kind == FurniKinds.Decor && f.TendedAt >= localMidnight && f.TendedAt <= now);
}

/// <summary>What one use or care action asks of the Places grain.</summary>
public sealed record FurniAction(string Label, int Priority, bool Care);

/// <summary>Furni behaviour as pure functions: condition, how it reads, what it affords, what acting does.</summary>
public static class FurniRules
{
    public static readonly TimeSpan PlantDriesIn = TimeSpan.FromHours(24);
    public static readonly TimeSpan SongLasts = TimeSpan.FromMinutes(20);
    public const int CupsPileUpAt = 3;

    /// <summary>0 = needs care badly, 1 = fine.</summary>
    public static double Condition(Furni f, long now) => f.Kind switch
    {
        FurniKinds.Plant => Math.Clamp(1 - (now - f.TendedAt) / PlantDriesIn.TotalMilliseconds, 0, 1),
        FurniKinds.Cups => Math.Clamp(1 - f.Dirty / 5.0, 0, 1),
        FurniKinds.Coffee or FurniKinds.Lamp => f.Broken ? 0 : 1,
        _ => 1,
    };

    public static bool IsOn(Furni f, long now) => f.OnUntil > now;

    /// <summary>A short clause when it's worth noticing; null when unremarkable.</summary>
    public static string? Notable(Furni f, long now)
    {
        var c = Condition(f, now);
        return f.Kind switch
        {
            FurniKinds.Plant when c < 0.35 => $"{f.Name} is wilting",
            FurniKinds.Plant when c < 0.65 => $"{f.Name} looks a bit dry",
            FurniKinds.Cups when f.Dirty >= CupsPileUpAt => $"dirty {(f.Name.Contains("dishes") ? "dishes are" : "cups are")} piling up",
            FurniKinds.Coffee when f.Broken => $"{f.Name} is broken",
            FurniKinds.Lamp when f.Broken => $"{f.Name} keeps flickering",
            FurniKinds.Jukebox when IsOn(f, now) => $"{f.Name} is playing {f.Playing}" + (f.PlayedBy is { } by ? $" ({by} put it on)" : string.Empty),
            _ => null,
        };
    }

    /// <summary>The care this needs now, if any. Priority grows as it gets worse.</summary>
    public static FurniAction? CareFor(Furni f, long now)
    {
        var c = Condition(f, now);
        return f.Kind switch
        {
            FurniKinds.Plant when c < 0.65 => new FurniAction($"Water {f.Name}", c < 0.35 ? 2 : 1, true),
            FurniKinds.Cups when f.Dirty >= CupsPileUpAt => new FurniAction($"Wash up {f.Name}", f.Dirty >= 5 ? 2 : 1, true),
            FurniKinds.Coffee or FurniKinds.Lamp when f.Broken => new FurniAction($"Try to fix {f.Name}", 1, true),
            _ => null,
        };
    }

    /// <summary>Something you can do with it for its own sake.</summary>
    public static FurniAction? UseOf(Furni f, long now) => f.Kind switch
    {
        FurniKinds.Coffee when !f.Broken => new FurniAction($"Make yourself a coffee at {f.Name}", 0, false),
        FurniKinds.Jukebox when !IsOn(f, now) => new FurniAction($"Put a song on {f.Name}", 0, false),
        _ => null,
    };

    /// <summary>Apply care. Returns the activity line ("waters the plant…").</summary>
    public static string Tend(Furni f, long now)
    {
        switch (f.Kind)
        {
            case FurniKinds.Plant: f.TendedAt = now; return $"waters {f.Name}";
            case FurniKinds.Cups: f.Dirty = 0; f.TendedAt = now; return $"washes up {f.Name}";
            default: f.Broken = false; f.TendedAt = now; return $"fixes {f.Name}";
        }
    }

    /// <summary>Apply a use; <paramref name="cups"/> is this Place's cups, which a coffee dirties.</summary>
    public static string? Use(Furni f, Furni? cups, long now, string by, string? intent)
    {
        switch (f.Kind)
        {
            case FurniKinds.Coffee when !f.Broken:
                if (cups is not null) cups.Dirty++;
                return $"makes a coffee at {f.Name}";
            case FurniKinds.Jukebox:
                f.OnUntil = now + (long)SongLasts.TotalMilliseconds;
                f.Playing = SongFrom(intent);
                f.PlayedBy = by;
                return $"puts {f.Playing} on {f.Name}";
            default:
                return null;
        }
    }

    /// <summary>What the Body shows while doing it.</summary>
    public static string BusyWith(Furni f, bool care) => (f.Kind, care) switch
    {
        (FurniKinds.Plant, _) => "watering the plant",
        (FurniKinds.Cups, _) => "doing the dishes",
        (FurniKinds.Coffee, false) => "making a coffee",
        (FurniKinds.Jukebox, _) => "by the jukebox",
        _ => $"fiddling with {f.Name}",
    };

    public static bool Breakable(Furni f) => f.Kind is FurniKinds.Coffee or FurniKinds.Lamp && !f.Broken;

    /// <summary>The song, from the intent if it names one ("put on 'Heroes' by Bowie"), else something.</summary>
    internal static string SongFrom(string? intent)
    {
        if (!string.IsNullOrWhiteSpace(intent))
        {
            var quoted = System.Text.RegularExpressions.Regex.Match(intent, "[\"“'‘]([^\"”'’]{2,60})[\"”'’]");
            if (quoted.Success) return $"\"{quoted.Groups[1].Value.Trim()}\"";
        }
        return Songs[Random.Shared.Next(Songs.Length)];
    }

    private static readonly string[] Songs =
    {
        "an old soul record", "something loud and eighties", "a slow jazz thing", "a song nobody else knows",
        "a cheesy summer hit", "a sad piano track",
    };

    /// <summary>Furni a Place starts with, by template; existing Places get them on first activation.</summary>
    public static List<Furni> SeedFor(Place place, long now)
    {
        Furni F(string id, string kind, string name, string spot) => new() { Id = id, Kind = kind, Name = name, SpotId = spot, TendedAt = now };
        if (place.Kind == PlaceKinds.Home)
            return new List<Furni>
            {
                F("plant", FurniKinds.Plant, "the plant on the windowsill", "sofa"),
                F("coffee", FurniKinds.Coffee, "the coffee maker", "kitchen"),
                F("cups", FurniKinds.Cups, "the dishes", "kitchen"),
                F("laptop", FurniKinds.Laptop, "the laptop", "desk"),
                F("bed", FurniKinds.Bed, "the bed", "bed"),
            };
        return place.Name switch
        {
            "the Café" => new List<Furni>
            {
                F("espresso", FurniKinds.Coffee, "the espresso machine", "counter"),
                F("cups", FurniKinds.Cups, "the cups", "counter"),
                F("jukebox", FurniKinds.Jukebox, "the jukebox", "jukebox"),
                F("plant", FurniKinds.Plant, "the little plant on the window table", "window-table"),
            },
            "the Lobby" => new List<Furni>
            {
                F("plant", FurniKinds.Plant, "the big plant by the window bench", "window"),
                F("lamp", FurniKinds.Lamp, "the lamp at reception", "desk"),
            },
            "the city centre" => new List<Furni>
            {
                F("lamp", FurniKinds.Lamp, "the old street lamp on the bridge", "bridge"),
                F("planters", FurniKinds.Plant, "the geraniums by the bench", "bench"),
                F("coffee", FurniKinds.Coffee, "the coffee cart", "cart"),
            },
            _ => new List<Furni>(),
        };
    }
}
