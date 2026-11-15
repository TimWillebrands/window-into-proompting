namespace PartyTown.World.Places;

[GenerateSerializer, Alias("PartyTown.World.Places.Place")]
public sealed class Place
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public string Name { get; set; } = string.Empty;
    [Id(2)] public string Kind { get; set; } = PlaceKinds.Public;
    [Id(3)] public Guid? OwnerId { get; set; }
    [Id(4)] public string Description { get; set; } = string.Empty;
    [Id(5)] public List<Spot> Spots { get; set; } = new();
    [Id(6)] public Guid? TalkRoomId { get; set; }
    [Id(7)] public List<Furni> Furni { get; set; } = new();
    [Id(8)] public List<Guid> Guests { get; set; } = new();
    [Id(9)] public Dictionary<Guid, long> Knocks { get; set; } = new();
    [Id(10)] public string? Scene { get; set; }
    [Id(11)] public string At { get; set; } = "at";
    [Id(12)] public string? CustomScene { get; set; }
    [Id(13)] public Guid? MadeBy { get; set; }
    [Id(14)] public string? WallColour { get; set; }
    [Id(15)] public string? FloorColour { get; set; }
    [Id(16)] public string? Purpose { get; set; }
    [Id(17)] public string? DistrictId { get; set; }
    [Id(18)] public long? OpenedAt { get; set; }
    [Id(19)] public string? Archetype { get; set; }
    [Id(20)] public long? MentionedAt { get; set; }
    [Id(21)] public bool Dormant { get; set; }
    [Id(22)] public List<Guid> Housemates { get; set; } = new();
    [Id(23)] public bool Vacated { get; set; }
    [Id(24)] public bool Private { get; set; }
    [Id(25)] public List<Guid> Keyholders { get; set; } = new();
    [Id(26)] public Dictionary<Guid, List<long>> Doorsteps { get; set; } = new();
    [Id(27)] public string? Access { get; set; }
    [Id(28)] public List<Guid> Staff { get; set; } = new();
    [Id(29)] public List<Guid> Allowed { get; set; } = new();
    [Id(30)] public string? Lately { get; set; }
    [Id(31)] public long LatelyAt { get; set; }
    [Id(32)] public List<string> AlsoCalled { get; set; } = new();
    [Id(33)] public Guid? SameAs { get; set; }
}

public static class PlaceAccess
{
    public const string Open = "open";
    public const string Appointment = "appointment";
    public const string Staff = "staff";

    public static readonly string[] All = { Open, Appointment, Staff };

    public static bool IsKnown(string? access) => access is Open or Appointment or Staff;

    public static bool Shut(Place place)
        => place.Kind == PlaceKinds.Public && (place.Access is Appointment or Staff) && place.Staff.Count > 0;

    public static string Words(string? access) => access switch
    {
        Staff => "staff only",
        Appointment => "by appointment",
        _ => "open to anyone",
    };
}

public static class Homes
{
    public static IEnumerable<Guid> Living(this Place place)
    {
        if (place.Kind != PlaceKinds.Home) yield break;
        if (place.OwnerId is { } owner && !place.Vacated) yield return owner;
        foreach (var mate in place.Housemates) yield return mate;
    }

    public static bool LivesIn(this Place place, Guid who) => place.Living().Contains(who);

    public static bool IsClosed(this Place place) => place.Kind == PlaceKinds.Home || place.Private || PlaceAccess.Shut(place);

    public static bool Keyed(this Place place) => place.Kind == PlaceKinds.Home || place.Private;

    public static bool IsWorkplaceDoor(this Place place) => !place.Keyed() && PlaceAccess.Shut(place);

    public static IEnumerable<Guid> Hosts(this Place place)
        => place.Kind == PlaceKinds.Home ? place.Living()
            : place.Private && place.MadeBy is { } owner ? new[] { owner }
            : PlaceAccess.Shut(place) ? place.Staff
            : Enumerable.Empty<Guid>();

    public static Place? HomeOf(this IEnumerable<Place> places, Guid who) => places.FirstOrDefault(p => p.LivesIn(who));

    public static void NoteDoorstep(this Place place, Guid who, long now)
    {
        var moments = place.Doorsteps.TryGetValue(who, out var known) ? known : place.Doorsteps[who] = new List<long>();
        moments.Add(now);
        if (moments.Count > MaxDoorsteps) moments.RemoveAt(0);
    }

    public static int DoorstepsLately(this Place place, Guid who, long now)
        => place.Doorsteps.GetValueOrDefault(who)?.Count(at => now - at < DoorstepsLast.TotalMilliseconds) ?? 0;

    private const int MaxDoorsteps = 8;
    private static readonly TimeSpan DoorstepsLast = TimeSpan.FromDays(7);

    public static string Named(IReadOnlyList<string> people)
    {
        var first = people.Select(n => n.Split(' ')[0]).ToList();
        return first.Count == 1 ? $"{first[0]}'s place" : $"{string.Join(", ", first.SkipLast(1))} and {first[^1]}'s place";
    }
}

[GenerateSerializer, Alias("PartyTown.World.Places.District")]
public sealed class District
{
    [Id(0)] public string Id { get; set; } = string.Empty;
    [Id(1)] public string Name { get; set; } = string.Empty;
}

public static class Districts
{
    public const string Centre = "centre";

    public static District Seed() => new() { Id = Centre, Name = "the centre" };

    public static string Slug(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.StartsWith("the ", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[4..];
        var chars = trimmed.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    public static string? FromName(string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(name, @"\bin\s+((?:\p{Lu}[\p{L}'’]*)(?:[\s-]\p{Lu}[\p{L}'’]*)*)\s*$");
        return m.Success ? m.Groups[1].Value : null;
    }

    public static bool Same(string a, string b)
    {
        if (a == b) return true;
        var (short_, long_) = a.Length <= b.Length ? (a, b) : (b, a);
        return short_.Length >= 4 && short_.Split('-').All(long_.Split('-').Contains);
    }

    public static bool Apart(Place a, Place b)
        => a.DistrictId is { } x && b.DistrictId is { } y && x != y;
}

[GenerateSerializer, Alias("PartyTown.World.Places.Spot")]
public sealed class Spot
{
    [Id(0)] public string Id { get; set; } = string.Empty;
    [Id(1)] public string Name { get; set; } = string.Empty;
    [Id(2)] public int Capacity { get; set; } = 2;
    [Id(3)] public bool Seat { get; set; }
    [Id(4)] public string At { get; set; } = "at";
    [Id(5)] public List<string> Poses { get; set; } = new();
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

[GenerateSerializer, Alias("PartyTown.World.Places.Body")]
public sealed class Body
{
    [Id(0)] public Guid ParticipantId { get; set; }
    [Id(1)] public string Name { get; set; } = string.Empty;
    [Id(2)] public Guid PlaceId { get; set; }
    [Id(3)] public string SpotId { get; set; } = string.Empty;
    [Id(4)] public Posture Posture { get; set; } = Posture.Standing;
    [Id(5)] public string? Activity { get; set; }
    [Id(6)] public Guid? DestPlaceId { get; set; }
    [Id(7)] public string? DestSpotId { get; set; }
    [Id(8)] public long? DepartedAt { get; set; }
    [Id(9)] public long? ArriveAt { get; set; }
    [Id(10)] public bool IsHuman { get; set; }
    [Id(11)] public long ArrivedAt { get; set; }
    [Id(12)] public long? ActivityUntil { get; set; }
    [Id(13)] public bool Asleep { get; set; }
    [Id(14)] public long? WokenUntil { get; set; }
    [Id(15)] public Appearance? Appearance { get; set; }
    [Id(16)] public string? Pose { get; set; }
    [Id(17)] public Guid? PoseWith { get; set; }
    [Id(18)] public long? PoseUntil { get; set; }
    [Id(19)] public string? AwayAs { get; set; }
    [Id(20)] public Guid? CameFrom { get; set; }
    [Id(21)] public bool AwayByDay { get; set; }
    [Id(22)] public string? AwayStretch { get; set; }
    [Id(23)] public string? Aura { get; set; }
    [Id(24)] public bool Afk { get; set; }
    [Id(25)] public Guid? LeftFrom { get; set; }
    [Id(26)] public WorkClothes? WorkClothes { get; set; }
    [Id(27)] public Appearance? OwnLook { get; set; }

    public bool IsWalking => DestPlaceId is not null;
}

[GenerateSerializer, Alias("PartyTown.World.Places.PlacesSnapshot")]
public sealed class PlacesSnapshot
{
    [Id(0)] public List<Place> Places { get; set; } = new();
    [Id(1)] public List<Body> Bodies { get; set; } = new();
    [Id(2)] public long Now { get; set; }
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
    [Id(11)] public string? Model { get; set; }
    [Id(12)] public int? TileX { get; set; }
    [Id(13)] public int? TileZ { get; set; }
    [Id(14)] public int Rot { get; set; }
    [Id(15)] public string? Prefab { get; set; }
    [Id(16)] public string? Gives { get; set; }
    [Id(17)] public bool Leaves { get; set; }
    [Id(18)] public List<Take> Takes { get; set; } = new();
}

[GenerateSerializer, Alias("PartyTown.World.Places.PlacesState")]
public sealed class PlacesState
{
    [Id(0)] public List<Place> Places { get; set; } = new();
    [Id(1)] public Dictionary<Guid, Body> Bodies { get; set; } = new();
    [Id(2)] public long NextMishapAt { get; set; }
    [Id(3)] public Dictionary<Guid, List<string>> Visits { get; set; } = new();
    [Id(4)] public List<District> Districts { get; set; } = new();
    [Id(5)] public List<string> CatalogueMisses { get; set; } = new();
    [Id(6)] public string? LastOpenedDay { get; set; }
    [Id(7)] public Dictionary<string, int> MissCounts { get; set; } = new();
    [Id(8)] public bool SameSwept { get; set; }
}

public static class PlaceKinds
{
    public const string Public = "public";
    public const string Home = "home";
    public const string Locale = "locale";
}

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

    public const string HomePurpose = "where they live";

    public static Place Home(Guid ownerId, string ownerName) => WithPoses(new Place
    {
        Id = Guid.NewGuid(),
        Name = Homes.Named(new[] { ownerName }),
        Kind = PlaceKinds.Home,
        Scene = "home",
        DistrictId = Districts.Centre,
        OwnerId = ownerId,
        Description = "a small apartment",
        Purpose = HomePurpose,
        Spots = new List<Spot>
        {
            new() { Id = "sofa", Name = "the sofa", At = "on", Capacity = 3, Seat = true },
            new() { Id = "desk", Name = "the desk", Capacity = 1, Seat = true },
            new() { Id = "kitchen", Name = "the kitchen", At = "in", Capacity = 3 },
            new() { Id = "bed", Name = "the bed", At = "on", Capacity = 2, Seat = true },
        },
    });

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
