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
