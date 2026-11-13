using PartyTown.World.Places;

namespace PartyTown.World;

/// <summary>How close a persona was to a Happening: salience is relational, so news from across town pulls less than a gull at your table.</summary>
public enum HappeningNear
{
    /// <summary>Not tied to any Place: news everyone hears.</summary>
    Anywhere = 0,
    /// <summary>It happened where they are.</summary>
    Here = 1,
    /// <summary>In their District, a short walk away.</summary>
    Nearby = 2,
    /// <summary>Across town.</summary>
    Away = 3,
}

/// <summary>A Happening as one persona stands to it (ADR 0043).</summary>
/// <param name="Where">"at the Café", or null when it's tied to no Place.</param>
public sealed record Happening(string Text, long Ts, Guid? PlaceId, string? Where, HappeningNear Near);

/// <summary>Pure rules for Happenings: where they land, who's near, how they read.</summary>
public static class HappeningRules
{
    /// <summary>How long a Happening is news where it happened: in the Place's situation, and on witnesses' options.</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromMinutes(45);
    /// <summary>How long it stays in "Lately in the world".</summary>
    public static readonly TimeSpan Lingers = TimeSpan.FromHours(12);

    /// <summary>A public Place for the Narrator to set something in, likelier where people are; null when the World has none.</summary>
    public static Place? PickPlace(IReadOnlyList<Place> places, IReadOnlyList<Body> bodies, Random random)
    {
        var open = places.Where(p => p is { Kind: PlaceKinds.Public, Dormant: false, Vacated: false, Private: false }).ToList();
        if (open.Count == 0) return null;
        var weights = open.Select(p => 1.0 + 2 * bodies.Count(b => b.PlaceId == p.Id && !b.IsWalking)).ToList();
        var roll = random.NextDouble() * weights.Sum();
        for (var i = 0; i < open.Count; i++)
        {
            roll -= weights[i];
            if (roll < 0) return open[i];
        }
        return open[^1];
    }

    /// <summary>What can happen (ADR 0050): each kind has someone or something acting and leaves people a move. Picked here, not by the model.</summary>
    public static readonly string[] Kinds =
    {
        "the weather turns, and it changes what people there can do or how they get home",
        "a stranger comes through (a courier, a tourist, a neighbour, someone lost) and wants something from whoever is around",
        "something there breaks, runs out or stops working, so people have to do without or sort it out",
        "something is delivered, left behind or found there that somebody will have to decide about",
        "an animal gets in or gets in the way, and it doesn't simply leave",
        "word arrives of something going on in the neighbourhood (roadworks, a closure, a street market, a power cut, a strike)",
        "a noise, a smell or a sight from outside that makes people look up and wonder",
        "something is on offer there that won't last (leftovers going free, a spare ticket, the last of something)",
        "one thing people around here keep talking about, that nobody has seen with their own eyes, turns up for real or turns out to be nothing, and that settles it",
    };

    /// <summary>The kind that takes up what people say (ADR 0055): talk the World ratifies or ends.</summary>
    public const int Talk = 8;

    /// <summary>Which kind this time: any but the last few, so the world doesn't settle into one sort of event. Talk only when there's talk.</summary>
    public static int PickKind(IReadOnlyCollection<int> lately, Random random, bool talk = true)
    {
        var all = Enumerable.Range(0, Kinds.Length).Where(i => talk || i != Talk).ToList();
        var fresh = all.Where(i => !lately.Contains(i)).ToList();
        return fresh.Count == 0 ? all[random.Next(all.Count)] : fresh[random.Next(fresh.Count)];
    }

    /// <summary>What people are saying, for the Narrator: steered away from, or for the Talk kind what it's about.</summary>
    public static string Talking(int kind, IReadOnlyCollection<string> worn, IEnumerable<string> lines)
    {
        var said = string.Join("\n", lines);
        if (kind != Talk) return $"What people are talking about (steer away from it):\n{said}";
        var words = worn.Count == 0 ? string.Empty : $"Words they keep reaching for: {string.Join(", ", worn)}\n";
        return $"What people keep coming back to (this time it's about one thing in here):\n{words}{said}";
    }

    /// <summary>How many kinds back a kind stays used up.</summary>
    public const int KindMemory = 4;

    /// <summary>The hour as the Narrator needs it: the day part, and whether it's dark.</summary>
    public static string DayPart(DateTimeOffset local) => local.Hour switch
    {
        < 6 => "the middle of the night, dark outside, most people asleep",
        < 9 => "early morning",
        < 12 => "morning",
        < 14 => "midday",
        < 18 => "afternoon",
        < 22 => "evening",
        _ => "late evening, dark outside",
    };

    /// <summary>Whether it lands in front of people or waits to be found; no head count, or the Narrator starts moving them.</summary>
    public static string Seen(int witnesses) => witnesses == 0
        ? "Nobody is there right now, so leave what whoever comes in next will find."
        : "People are there right now, so it happens where they can see it.";

    /// <summary>Nothing happens for no one: with everybody asleep or away from their screen, the Narrator waits.</summary>
    public static bool AnyoneUp(IEnumerable<Body> bodies) => bodies.Any(b => !b.Asleep && b.Activity != PlacesGrain.Dozing);

    /// <summary>The Narrator's brief: one kind, a real Place, and the test that it matters to whoever is there.</summary>
    public static string Brief(string setting, string kind, string seen, string shape) => $"""
You are the narrator of a small world: a handful of people living their days in one city. Let ONE thing happen {setting}.
This time: {kind}.
It has to matter to whoever is there: something to deal with, something to take up, or something they can no longer do. If nobody would look up for it, it is too small; if it would make the news, it is too big.
{seen}
Stop at the thing itself. What the people who live here do about it is theirs: don't name them, don't say what they do, notice, think or feel, and don't explain what it means. One or two sentences, present tense.
Output ONLY JSON: {shape}
""";

    /// <summary>Where the Narrator sets it, as a phrase for the prompt: a real Place, never an invented street.</summary>
    public static string Setting(Place? place, IReadOnlyList<District> districts)
    {
        if (place is null) return "in their surroundings";
        var district = districts.FirstOrDefault(d => d.Id == place.DistrictId)?.Name;
        var purpose = string.IsNullOrWhiteSpace(place.Purpose) ? string.Empty : $" ({place.Purpose})";
        return $"{place.At} {place.Name}{(district is null ? string.Empty : $", in {district}")}{purpose}. Set it there and only there; don't invent street or place names";
    }

    /// <summary>Who saw it: awake, there, not passing through.</summary>
    public static List<Guid> Witnesses(IEnumerable<Body> bodies, Guid placeId)
        => bodies.Where(b => b.PlaceId == placeId && !b.IsWalking && !b.Asleep).Select(b => b.ParticipantId).ToList();

    /// <summary>Who'd hear of it: anyone in the same District, witnesses included.</summary>
    public static List<Guid> Around(IReadOnlyList<Place> places, IEnumerable<Body> bodies, Guid placeId)
    {
        var district = places.FirstOrDefault(p => p.Id == placeId)?.DistrictId;
        return bodies.Where(b => b.PlaceId == placeId
                || (district is not null && places.FirstOrDefault(p => p.Id == b.PlaceId)?.DistrictId == district))
            .Select(b => b.ParticipantId).ToList();
    }

    /// <summary>A logged Happening as <paramref name="viewer"/> stands to it.</summary>
    public static Happening Relate(WorldActivity happening, Body? me, IReadOnlyList<Place> places, Guid viewer)
    {
        if (happening.TargetId is not { } at || places.FirstOrDefault(p => p.Id == at) is not { } place)
            return new Happening(happening.Summary, happening.Ts, null, null, HappeningNear.Anywhere);
        var where = PlacesGrain.AtName(place, viewer);
        var mine = me is null ? null : places.FirstOrDefault(p => p.Id == me.PlaceId);
        var near = me is { IsWalking: false } && me.PlaceId == at ? HappeningNear.Here
            : mine?.DistrictId is { } district && district == place.DistrictId ? HappeningNear.Nearby
            : HappeningNear.Away;
        return new Happening(happening.Summary, happening.Ts, at, where, near);
    }

    public static bool IsFresh(Happening h, DateTimeOffset now) => now.ToUnixTimeMilliseconds() - h.Ts < Fresh.TotalMilliseconds;

    /// <summary>The fresh one they saw, until they've used that channel since: then it's been told, or let go.</summary>
    public static Happening? Witnessed(IEnumerable<Happening>? happenings, IEnumerable<WorldActivity> recentOwn, string app, string action, DateTimeOffset now)
        => happenings?.FirstOrDefault(h => h.Near == HappeningNear.Here && IsFresh(h, now)
            && !recentOwn.Any(a => a.App == app && a.Action == action && a.Ts > h.Ts));

    /// <summary>One line of "Lately in the world", saying where it was from here.</summary>
    public static string Line(Happening h, DateTimeOffset now)
    {
        var ago = WorldStream.Ago(h.Ts, now);
        var where = h.Near switch
        {
            HappeningNear.Here => $", right here {h.Where}",
            HappeningNear.Nearby => $", {h.Where}, nearby",
            HappeningNear.Away => $", across town {h.Where}",
            _ => string.Empty,
        };
        return $"- {ago}{where}: {h.Text}";
    }
}
