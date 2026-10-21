namespace PartyTown.World.Places;

/// <summary>Hang-out poses (IMVU-style): how a Body holds itself at a spot. Solo ones are afforded per spot; pair ones need someone at your spot.</summary>
public static class Poses
{
    public const string Lean = "lean";
    public const string Lounge = "lounge";
    public const string Perch = "perch";
    public const string Dance = "dance";
    public const string Lie = "lie";
    public const string CrossLegged = "cross-legged";
    public const string Think = "think";
    public const string Wave = "wave";
    public const string Hug = "hug";
    public const string SlowDance = "slow-dance";
    public const string HighFive = "high-five";
    public const string HoldHands = "hold-hands";

    /// <summary>Anywhere, no spot needed.</summary>
    public static readonly string[] Anywhere = { Think, Wave };
    public static readonly string[] Pair = { Hug, SlowDance, HighFive, HoldHands };
    public static readonly string[] Sitting = { Lounge, Perch, CrossLegged };

    /// <summary>Momentary poses wear off by themselves.</summary>
    public static TimeSpan? Lasts(string pose) => pose switch
    {
        Hug or HighFive or Wave => TimeSpan.FromSeconds(20),
        _ => null,
    };

    public static Posture PostureOf(string pose, Posture current) => pose switch
    {
        Lie => Posture.Lying,
        Lounge or Perch or CrossLegged => Posture.Sitting,
        Lean or Dance or Hug or SlowDance or HighFive or HoldHands => Posture.Standing,
        _ => current,
    };

    /// <summary>What a Body may do at a spot: the spot's own poses, the anywhere ones, and pair ones when someone shares the spot (slow dance only where dancing is).</summary>
    public static List<string> Allowed(Spot spot, bool company)
    {
        var all = spot.Poses.Concat(Anywhere).ToList();
        if (company)
            all.AddRange(Pair.Where(p => p != SlowDance || spot.Poses.Contains(Dance)));
        return all.Distinct().ToList();
    }

    /// <summary>"leaning on the bridge", "slow-dancing with Denise by the jukebox".</summary>
    public static string Describe(string pose, Spot spot, string? with)
    {
        var at = $"{spot.At} {spot.Name}";
        return pose switch
        {
            Lean => $"leaning {(spot.At == "in" ? "against the counter in" : "on")} {spot.Name}",
            Lounge => $"lounging {at}",
            Perch => $"perched {at}",
            Dance => $"dancing {(spot.At == "at" ? "by" : spot.At)} {spot.Name}",
            Lie => $"lying {at}",
            CrossLegged => $"sitting cross-legged {at}",
            Think => $"lost in thought {at}",
            Wave => $"waving {at}",
            Hug => $"hugging {with ?? "someone"} {at}",
            SlowDance => $"slow-dancing with {with ?? "someone"} {at}",
            HighFive => $"high-fiving {with ?? "someone"} {at}",
            HoldHands => $"holding hands with {with ?? "someone"} {at}",
            _ => $"{pose} {at}",
        };
    }

    /// <summary>A pose named in a Life intent ("lean on the railing and watch the boats").</summary>
    public static string? FromIntent(string? intent, IReadOnlyCollection<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(intent)) return null;
        var t = intent.ToLowerInvariant();
        (string Pose, string[] Words)[] cues =
        {
            (SlowDance, new[] { "slow dance", "slow-dance", "slowdance" }),
            (Hug, new[] { "hug", "embrace" }),
            (HighFive, new[] { "high five", "high-five" }),
            (HoldHands, new[] { "hold hands", "holding hands", "hold their hand", "hold her hand", "hold his hand" }),
            (Dance, new[] { "dance", "dancing", "boogie" }),
            (Lean, new[] { "lean" }),
            (Lounge, new[] { "lounge", "sprawl", "kick back", "put my feet up", "stretch out" }),
            (Perch, new[] { "perch", "dangle", "sit on the edge" }),
            (Lie, new[] { "lie down", "lie on", "lay down", "lie back" }),
            (CrossLegged, new[] { "cross-legged", "cross legged" }),
            (Think, new[] { "think", "ponder", "daydream" }),
            (Wave, new[] { "wave" }),
        };
        return cues.FirstOrDefault(c => allowed.Contains(c.Pose) && c.Words.Any(t.Contains)).Pose;
    }

    /// <summary>Give spots without poses the seeded ones for their id; true when any changed.</summary>
    public static bool Fill(IEnumerable<Spot> spots)
    {
        var changed = false;
        foreach (var s in spots.Where(s => s.Poses.Count == 0 && BySpotId.ContainsKey(s.Id)))
        {
            s.Poses = BySpotId[s.Id].ToList();
            changed |= s.Poses.Count > 0;
        }
        return changed;
    }

    /// <summary>Which seeded spots afford which poses, for Places stored before poses existed.</summary>
    public static readonly Dictionary<string, string[]> BySpotId = new()
    {
        ["counter"] = new[] { Lean },
        ["jukebox"] = new[] { Dance, Lean },
        ["booth"] = new[] { Lounge },
        ["window-table"] = Array.Empty<string>(),
        ["entrance"] = new[] { Lean },
        ["sofas"] = new[] { Lounge, Lie },
        ["desk"] = new[] { Lean },
        ["window"] = new[] { Perch, Lounge },
        ["sofa"] = new[] { Lounge, Lie },
        ["kitchen"] = new[] { Lean, Dance },
        ["bed"] = new[] { Lie, Lounge, CrossLegged },
        ["bridge"] = new[] { Lean, Perch },
        ["bench"] = new[] { Lounge, Lie },
        ["steps"] = new[] { Perch, Lounge },
        ["cart"] = new[] { Lean },
        ["bikes"] = new[] { Lean, Perch },
    };
}
