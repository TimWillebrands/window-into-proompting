namespace PartyTown.World.Places;

/// <summary>Physical location in the World (spec: .scratch/places). Spots are canonical; tiles are presentation.</summary>
[GenerateSerializer, Alias("PartyTown.World.Places.Place")]
public sealed class Place
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public string Name { get; set; } = string.Empty;
    /// <summary><c>public</c>, <c>home</c> or <c>locale</c> (offscreen, ADR 0027).</summary>
    [Id(2)] public string Kind { get; set; } = PlaceKinds.Public;
    /// <summary>Home owner; null for public Places.</summary>
    [Id(3)] public Guid? OwnerId { get; set; }
    [Id(4)] public string Description { get; set; } = string.Empty;
    [Id(5)] public List<Spot> Spots { get; set; } = new();
    /// <summary>Place talk Room, created on first entry.</summary>
    [Id(6)] public Guid? TalkRoomId { get; set; }
    [Id(7)] public List<Furni> Furni { get; set; } = new();
    /// <summary>Home only: who's been let in or invited; cleared when the owner leaves.</summary>
    [Id(8)] public List<Guid> Guests { get; set; } = new();
    /// <summary>Home only: who's knocking, and since when.</summary>
    [Id(9)] public Dictionary<Guid, long> Knocks { get; set; } = new();
    /// <summary>Which client scene draws it (ADR 0023): lobby, cafe, centre, home; unknown ones get a generated room.</summary>
    [Id(10)] public string? Scene { get; set; }
    /// <summary>Preposition: "at the Café", "in the city centre".</summary>
    [Id(11)] public string At { get; set; } = "at";
    /// <summary>A generated Place's scene (ADR 0023 format, JSON); wins over <see cref="Scene"/>.</summary>
    [Id(12)] public string? CustomScene { get; set; }
    /// <summary>Who had a public Place made (a shop, a bar); not an owner like a Home's.</summary>
    [Id(13)] public Guid? MadeBy { get; set; }
    /// <summary>Owner's choice of wall and floor colour (hex); null = the scene's.</summary>
    [Id(14)] public string? WallColour { get; set; }
    [Id(15)] public string? FloorColour { get; set; }
    /// <summary>What it's for, one plain line (ADR 0026): "where people get coffee and linger".</summary>
    [Id(16)] public string? Purpose { get; set; }
    /// <summary>Which part of the city it's in (ADR 0027); null for a Locale nobody has placed yet.</summary>
    [Id(17)] public string? DistrictId { get; set; }
    /// <summary>When a Locale was Opened into this Place (ADR 0027); null for seeded, made and home Places.</summary>
    [Id(18)] public long? OpenedAt { get; set; }
    /// <summary>The kit it opened from: office, bar, park…</summary>
    [Id(19)] public string? Archetype { get; set; }
}

/// <summary>A part of the city that holds Places (ADR 0027): "the centre", "Zuid".</summary>
[GenerateSerializer, Alias("PartyTown.World.Places.District")]
public sealed class District
{
    /// <summary>Slug: centre, zuid, de-pijp.</summary>
    [Id(0)] public string Id { get; set; } = string.Empty;
    [Id(1)] public string Name { get; set; } = string.Empty;
}

public static class Districts
{
    public const string Centre = "centre";

    public static District Seed() => new() { Id = Centre, Name = "the centre" };

    /// <summary>"Amsterdam Zuid" → "amsterdam-zuid"; letters and digits only, dashed.</summary>
    public static string Slug(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.StartsWith("the ", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[4..];
        var chars = trimmed.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Two drawn Places in different Districts are a tram ride apart; unplaced Locales are never.</summary>
    public static bool Apart(Place a, Place b)
        => a.DistrictId is { } x && b.DistrictId is { } y && x != y;
}

/// <summary>A named place to be inside a Place: "the window table". What Personas reason in.</summary>
[GenerateSerializer, Alias("PartyTown.World.Places.Spot")]
public sealed class Spot
{
    [Id(0)] public string Id { get; set; } = string.Empty;
    [Id(1)] public string Name { get; set; } = string.Empty;
    [Id(2)] public int Capacity { get; set; } = 2;
    [Id(3)] public bool Seat { get; set; }
    /// <summary>Preposition for "sitting on the sofa" / "in the kitchen".</summary>
    [Id(4)] public string At { get; set; } = "at";
    /// <summary>Poses this spot affords beyond standing or sitting (see <see cref="Poses"/>).</summary>
    [Id(5)] public List<string> Poses { get; set; } = new();
    /// <summary>Behind the counter: where someone working here stands (ADR 0026).</summary>
    [Id(6)] public bool Staff { get; set; }
}

[GenerateSerializer, Alias("PartyTown.World.Places.Posture")]
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<Posture>))]
public enum Posture
{
    Standing,
    Sitting,
    Walking,
    Lying,
}

/// <summary>A Participant's physical presence: exactly one, always in exactly one Place.</summary>
[GenerateSerializer, Alias("PartyTown.World.Places.Body")]
public sealed class Body
{
    [Id(0)] public Guid ParticipantId { get; set; }
    [Id(1)] public string Name { get; set; } = string.Empty;
    [Id(2)] public Guid PlaceId { get; set; }
    [Id(3)] public string SpotId { get; set; } = string.Empty;
    [Id(4)] public Posture Posture { get; set; } = Posture.Standing;
    /// <summary>What the Body is busy with, third person ("on their phone, texting"); null when idle.</summary>
    [Id(5)] public string? Activity { get; set; }
    [Id(6)] public Guid? DestPlaceId { get; set; }
    [Id(7)] public string? DestSpotId { get; set; }
    [Id(8)] public long? DepartedAt { get; set; }
    [Id(9)] public long? ArriveAt { get; set; }
    [Id(10)] public bool IsHuman { get; set; }
    /// <summary>When the Body settled where it is; drives restlessness.</summary>
    [Id(11)] public long ArrivedAt { get; set; }
    /// <summary>When <see cref="Activity"/> wears off.</summary>
    [Id(12)] public long? ActivityUntil { get; set; }
    [Id(13)] public bool Asleep { get; set; }
    /// <summary>Woken in the night (a human's message); stays up until then.</summary>
    [Id(14)] public long? WokenUntil { get; set; }
    [Id(15)] public Appearance? Appearance { get; set; }
    /// <summary>How the Body is holding itself at its spot (see <see cref="Poses"/>); null = plain standing or sitting.</summary>
    [Id(16)] public string? Pose { get; set; }
    /// <summary>The other Body in a pair pose (a hug, a slow dance).</summary>
    [Id(17)] public Guid? PoseWith { get; set; }
    /// <summary>When a momentary pose (hug, high five) ends.</summary>
    [Id(18)] public long? PoseUntil { get; set; }
    /// <summary>Why the Body is at a Locale ("at work"); null when not away.</summary>
    [Id(19)] public string? AwayAs { get; set; }
    /// <summary>The Place it left to go away, to return to.</summary>
    [Id(20)] public Guid? CameFrom { get; set; }
    /// <summary>Sent away by its Day's whereabouts (not a shift); only then does the Day bring it back.</summary>
    [Id(21)] public bool AwayByDay { get; set; }
    /// <summary>"yyyy-MM-dd:startMinute" of the last stretch it went away for, so it goes once per stretch.</summary>
    [Id(22)] public string? AwayStretch { get; set; }

    public bool IsWalking => DestPlaceId is not null;
}

[GenerateSerializer, Alias("PartyTown.World.Places.PlacesSnapshot")]
public sealed class PlacesSnapshot
{
    [Id(0)] public List<Place> Places { get; set; } = new();
    [Id(1)] public List<Body> Bodies { get; set; } = new();
    [Id(2)] public long Now { get; set; }
    /// <summary>Furni as the rules read them now, so clients never re-implement decay.</summary>
    [Id(3)] public List<FurniView> Furni { get; set; } = new();
    [Id(4)] public List<District> Districts { get; set; } = new();
}

[GenerateSerializer, Alias("PartyTown.World.Places.FurniView")]
public sealed class FurniView
{
    [Id(0)] public Guid PlaceId { get; set; }
    [Id(1)] public string FurniId { get; set; } = string.Empty;
    [Id(2)] public string Kind { get; set; } = string.Empty;
    [Id(3)] public string Name { get; set; } = string.Empty;
    [Id(4)] public string SpotId { get; set; } = string.Empty;
    [Id(5)] public double Condition { get; set; }
    [Id(6)] public string? Notable { get; set; }
    [Id(7)] public string? CareLabel { get; set; }
    [Id(8)] public string? UseLabel { get; set; }
    [Id(9)] public bool On { get; set; }
    [Id(10)] public string? Style { get; set; }
    /// <summary>Generated 3D model (parts JSON, see <see cref="ThingModel"/>); null = drawn from its kind.</summary>
    [Id(11)] public string? Model { get; set; }
    [Id(12)] public int? TileX { get; set; }
    [Id(13)] public int? TileZ { get; set; }
    [Id(14)] public int Rot { get; set; }
}

[GenerateSerializer, Alias("PartyTown.World.Places.PlacesState")]
public sealed class PlacesState
{
    [Id(0)] public List<Place> Places { get; set; } = new();
    [Id(1)] public Dictionary<Guid, Body> Bodies { get; set; } = new();
    /// <summary>When the next rule-based mishap (a machine breaking) may happen; 0 = not scheduled.</summary>
    [Id(2)] public long NextMishapAt { get; set; }
    /// <summary>Per participant: "placeId:yyyy-MM-dd" for each local day they spent time there; last 7 days.</summary>
    [Id(3)] public Dictionary<Guid, List<string>> Visits { get; set; } = new();
    [Id(4)] public List<District> Districts { get; set; } = new();
}

public static class PlaceKinds
{
    public const string Public = "public";
    public const string Home = "home";
    public const string Locale = "locale";
}

/// <summary>The Places a World starts with, and the Home template.</summary>
public static class PlaceSeeds
{
    public static List<Place> Public() => new List<Place>
    {
        new Place
        {
            Id = Guid.NewGuid(), Name = "the Lobby", Kind = PlaceKinds.Public, Scene = "lobby", DistrictId = Districts.Centre,
            Description = "a big carpeted hall with a reception desk, worn sofas and a noticeboard",
            Purpose = "where people come in, wait for someone and check the noticeboard",
            Spots = new List<Spot>
            {
                new() { Id = "entrance", Name = "the entrance", Capacity = 4 },
                new() { Id = "sofas", Name = "the sofas", At = "on", Capacity = 4, Seat = true },
                new() { Id = "desk", Name = "the reception desk", Capacity = 3, Staff = true },
                new() { Id = "window", Name = "the window bench", At = "on", Capacity = 2, Seat = true },
            },
        },
        new Place
        {
            Id = Guid.NewGuid(), Name = "the Café", Kind = PlaceKinds.Public, Scene = "cafe", DistrictId = Districts.Centre,
            Description = "a small café that smells of coffee, with a counter, a few tables and a jukebox",
            Purpose = "where people get coffee and linger",
            Spots = new List<Spot>
            {
                new() { Id = "counter", Name = "the counter", Capacity = 3, Staff = true },
                new() { Id = "window-table", Name = "the window table", Capacity = 2, Seat = true },
                new() { Id = "booth", Name = "the corner booth", Capacity = 4, Seat = true },
                new() { Id = "jukebox", Name = "the jukebox", Capacity = 2 },
            },
        },
        new Place
        {
            Id = Guid.NewGuid(), Name = "the city centre", Kind = PlaceKinds.Public, Scene = "centre", At = "in", DistrictId = Districts.Centre,
            Description = "the old centre outside: two canals lined with crooked gabled houses, cobbled streets, a humped bridge and bikes chained to every railing",
            Purpose = "where people pass through, sit by the water and run into each other",
            Spots = new List<Spot>
            {
                new() { Id = "bridge", Name = "the bridge", At = "on", Capacity = 4 },
                new() { Id = "bench", Name = "the bench by the canal", At = "on", Capacity = 3, Seat = true },
                new() { Id = "steps", Name = "the steps down to the water", At = "on", Capacity = 3, Seat = true },
                new() { Id = "cart", Name = "the herring cart", Capacity = 3 },
                new() { Id = "bikes", Name = "the bike racks", At = "by", Capacity = 2 },
            },
        },
    }.Select(WithPoses).ToList();

    public static Place Home(Guid ownerId, string ownerName) => WithPoses(new Place
    {
        Id = Guid.NewGuid(),
        Name = $"{ownerName.Split(' ')[0]}'s place",
        Kind = PlaceKinds.Home,
        Scene = "home",
        DistrictId = Districts.Centre,
        OwnerId = ownerId,
        Description = "a small apartment",
        Purpose = "where they live",
        Spots = new List<Spot>
        {
            new() { Id = "sofa", Name = "the sofa", At = "on", Capacity = 3, Seat = true },
            new() { Id = "desk", Name = "the desk", Capacity = 1, Seat = true },
            new() { Id = "kitchen", Name = "the kitchen", At = "in", Capacity = 3 },
            new() { Id = "bed", Name = "the bed", At = "on", Capacity = 2, Seat = true },
        },
    });

    /// <summary>An offscreen Place (ADR 0027): one spot, never drawn, no talk Room.</summary>
    public static Place Locale(string name) => new()
    {
        Id = Guid.NewGuid(), Name = name, Kind = PlaceKinds.Locale, Description = name,
        Spots = new List<Spot> { new() { Id = "there", Name = name, Capacity = 99 } },
    };

    private static Place WithPoses(Place p)
    {
        Poses.Fill(p.Spots);
        return p;
    }
}
