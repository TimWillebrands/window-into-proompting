namespace PartyTown.World.Places;

/// <summary>Physical location in the World (spec: .scratch/places). Spots are canonical; tiles are presentation.</summary>
[GenerateSerializer, Alias("PartyTown.World.Places.Place")]
public sealed class Place
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public string Name { get; set; } = string.Empty;
    /// <summary><c>public</c> or <c>home</c>.</summary>
    [Id(2)] public string Kind { get; set; } = PlaceKinds.Public;
    /// <summary>Home owner; null for public Places.</summary>
    [Id(3)] public Guid? OwnerId { get; set; }
    [Id(4)] public string Description { get; set; } = string.Empty;
    [Id(5)] public List<Spot> Spots { get; set; } = new();
    /// <summary>Place talk Room, created on first entry.</summary>
    [Id(6)] public Guid? TalkRoomId { get; set; }
}

/// <summary>A named place to be inside a Place: "the window table". What Personas reason in.</summary>
[GenerateSerializer, Alias("PartyTown.World.Places.Spot")]
public sealed class Spot
{
    [Id(0)] public string Id { get; set; } = string.Empty;
    [Id(1)] public string Name { get; set; } = string.Empty;
    [Id(2)] public int Capacity { get; set; } = 2;
    [Id(3)] public bool Seat { get; set; }
}

public enum Posture
{
    Standing,
    Sitting,
    Walking,
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
    /// <summary>What the Body is busy with, e.g. "on their phone"; null when idle.</summary>
    [Id(5)] public string? Activity { get; set; }
    [Id(6)] public Guid? DestPlaceId { get; set; }
    [Id(7)] public string? DestSpotId { get; set; }
    [Id(8)] public long? DepartedAt { get; set; }
    [Id(9)] public long? ArriveAt { get; set; }
    [Id(10)] public bool IsHuman { get; set; }

    public bool IsWalking => DestPlaceId is not null;
}

[GenerateSerializer, Alias("PartyTown.World.Places.PlacesSnapshot")]
public sealed class PlacesSnapshot
{
    [Id(0)] public List<Place> Places { get; set; } = new();
    [Id(1)] public List<Body> Bodies { get; set; } = new();
    [Id(2)] public long Now { get; set; }
}

[GenerateSerializer, Alias("PartyTown.World.Places.PlacesState")]
public sealed class PlacesState
{
    [Id(0)] public List<Place> Places { get; set; } = new();
    [Id(1)] public Dictionary<Guid, Body> Bodies { get; set; } = new();
}

public static class PlaceKinds
{
    public const string Public = "public";
    public const string Home = "home";
}

/// <summary>The Places a World starts with, and the Home template.</summary>
public static class PlaceSeeds
{
    public static List<Place> Public() => new()
    {
        new Place
        {
            Id = Guid.NewGuid(), Name = "the Lobby", Kind = PlaceKinds.Public,
            Description = "a big carpeted hall with a reception desk, worn sofas and a noticeboard",
            Spots = new List<Spot>
            {
                new() { Id = "entrance", Name = "the entrance", Capacity = 4 },
                new() { Id = "sofas", Name = "the sofas", Capacity = 4, Seat = true },
                new() { Id = "desk", Name = "the reception desk", Capacity = 3 },
                new() { Id = "window", Name = "the window bench", Capacity = 2, Seat = true },
            },
        },
        new Place
        {
            Id = Guid.NewGuid(), Name = "the Café", Kind = PlaceKinds.Public,
            Description = "a small café that smells of coffee, with a counter, a few tables and a jukebox",
            Spots = new List<Spot>
            {
                new() { Id = "counter", Name = "the counter", Capacity = 3 },
                new() { Id = "window-table", Name = "the window table", Capacity = 2, Seat = true },
                new() { Id = "booth", Name = "the corner booth", Capacity = 4, Seat = true },
                new() { Id = "jukebox", Name = "the jukebox", Capacity = 2 },
            },
        },
    };

    public static Place Home(Guid ownerId, string ownerName) => new()
    {
        Id = Guid.NewGuid(),
        Name = $"{ownerName.Split(' ')[0]}'s place",
        Kind = PlaceKinds.Home,
        OwnerId = ownerId,
        Description = "a small apartment",
        Spots = new List<Spot>
        {
            new() { Id = "sofa", Name = "the sofa", Capacity = 3, Seat = true },
            new() { Id = "desk", Name = "the desk", Capacity = 1, Seat = true },
            new() { Id = "kitchen", Name = "the kitchen", Capacity = 3 },
            new() { Id = "bed", Name = "the bed", Capacity = 2, Seat = true },
        },
    };
}
