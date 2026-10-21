namespace PartyTown.World.Places;

public static class Volumes
{
    public const string Whisper = "whisper";
    public const string Talk = "talk";
    public const string Shout = "shout";
}

/// <summary>Who hears one utterance in a Place (spec § Sound). Keyed off spots, never tiles.</summary>
public static class Earshot
{
    /// <summary>talk = your spot; shout = the whole Place; whisper = the addressee at your spot (else your spot).</summary>
    public static List<Guid> Hearers(IReadOnlyList<Body> occupants, Body speaker, string volume, Guid? to)
    {
        var here = occupants.Where(b => !b.IsWalking || b.DestPlaceId == speaker.PlaceId).ToList();
        IEnumerable<Body> hearers = volume switch
        {
            Volumes.Shout => here,
            Volumes.Whisper when to is { } t && here.Any(b => b.ParticipantId == t && b.SpotId == speaker.SpotId)
                => here.Where(b => b.ParticipantId == t),
            _ => here.Where(b => b.SpotId == speaker.SpotId),
        };
        return hearers.Select(b => b.ParticipantId).Append(speaker.ParticipantId).Distinct().ToList();
    }
}

/// <summary>A pending volume for a Participant's next utterance (Life picked "whisper to Hana", or the human's toggle).</summary>
public sealed record Voice(string Volume, Guid? To, long Until);

[GenerateSerializer, Alias("PartyTown.World.Places.EarshotResult")]
public sealed record EarshotResult([property: Id(0)] string Volume, [property: Id(1)] List<Guid> HeardBy);

/// <summary>Talk you noticed but couldn't make out: tone and maybe your name, never the words.</summary>
[GenerateSerializer, Alias("PartyTown.World.Places.Overheard")]
public sealed class Overheard
{
    [Id(0)] public Guid PlaceId { get; set; }
    [Id(1)] public Guid SpeakerId { get; set; }
    [Id(2)] public string SpeakerName { get; set; } = string.Empty;
    [Id(3)] public string SpotId { get; set; } = string.Empty;
    [Id(4)] public string Volume { get; set; } = Volumes.Talk;
    [Id(5)] public string Tone { get; set; } = "chatting";
    /// <summary>Who was in the Place but out of Earshot.</summary>
    [Id(6)] public List<Guid> Listeners { get; set; } = new();
    /// <summary>Listeners who think they heard their name.</summary>
    [Id(7)] public List<Guid> NameHeardBy { get; set; } = new();
    [Id(8)] public long At { get; set; }
    /// <summary>Who the speaker was talking with (the ones who did hear).</summary>
    [Id(9)] public List<string> WithNames { get; set; } = new();
    [Id(10)] public string PlaceName { get; set; } = string.Empty;
    /// <summary>Listeners next door who caught a shout through the door.</summary>
    [Id(11)] public List<Guid> NextDoor { get; set; } = new();
}

public static class Overhearing
{
    public static readonly string[] Tones = { "laughing", "arguing", "serious", "flirting", "chatting" };

    /// <summary>Public Places share doors; a shout carries into the next one.</summary>
    public static bool NextDoor(Place a, Place b) => a.Id != b.Id && a.Kind == PlaceKinds.Public && b.Kind == PlaceKinds.Public;

    /// <summary>One perception line per group of speakers at a spot (or next door), newest first.</summary>
    public static List<(string Line, Overheard Latest, bool Name)> Lines(IEnumerable<Overheard> heard, Guid listener, Place place)
        => heard
            .GroupBy(o => o.PlaceId == place.Id ? o.SpotId : $"door:{o.PlaceId}")
            .Select(g =>
            {
                var latest = g.OrderByDescending(o => o.At).First();
                var who = g.Select(o => o.SpeakerName).Concat(g.SelectMany(o => o.WithNames)).Distinct().Take(3).ToList();
                if (latest.PlaceId != place.Id)
                    return ($"Through the door from {latest.PlaceName}, {Join(who)} {(who.Count == 1 ? "is" : "are")} calling out; you can't make out the words."
                        + (g.Any(o => o.NameHeardBy.Contains(listener)) ? " You think you heard your name." : string.Empty),
                        latest, g.Any(o => o.NameHeardBy.Contains(listener)));
                var spot = place.Spots.FirstOrDefault(s => s.Id == g.Key)?.Name ?? "the other side";
                var how = latest.Volume == Volumes.Whisper ? "whispering" : latest.Tone switch
                {
                    "laughing" => "laughing about something",
                    "arguing" => "arguing, by the sound of it",
                    "serious" => "talking quietly, sounds serious",
                    "flirting" => "talking, a bit flirty",
                    _ => "chatting",
                };
                var name = g.Any(o => o.NameHeardBy.Contains(listener));
                var line = $"Over at {spot}, {Join(who)} {(who.Count == 1 ? "is" : "are")} {how}; you can't make out the words."
                    + (name ? " You think you heard your name." : string.Empty);
                return (line, latest, name);
            })
            .OrderByDescending(x => x.latest.At)
            .ToList();

    private static string Join(IReadOnlyList<string> names)
        => names.Count <= 1 ? names.FirstOrDefault() ?? "someone" : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";
}
