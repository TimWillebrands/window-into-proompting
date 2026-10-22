using PartyTown.World.Places;

namespace PartyTown.World.Work;

[GenerateSerializer]
public enum PlaceRole
{
    Newcomer = 0,
    Regular = 1,
    Staff = 2,
    Owner = 3,
}

/// <summary>Standing in a Place (ADR 0026, after Pageau's centre and margin): derived each time, never stored.</summary>
public static class Roles
{
    public const int RegularDays = 3;
    /// <summary>Someone who came in this recently is someone staff greet.</summary>
    public static readonly TimeSpan JustCameIn = TimeSpan.FromMinutes(10);

    public static PlaceRole Of(Place place, Guid who, Occupation? job, IReadOnlyDictionary<Guid, int> visitDays)
    {
        if (place.OwnerId == who || place.MadeBy == who) return PlaceRole.Owner;
        if (job is not null && (job.PlaceId ?? job.LocaleId) == place.Id) return PlaceRole.Staff;
        return visitDays.GetValueOrDefault(place.Id) >= RegularDays ? PlaceRole.Regular : PlaceRole.Newcomer;
    }

    /// <summary>One plain line for "Right now"; null when there's nothing to say (your own Home, a Locale, first-timers).</summary>
    public static string? Line(Place place, PlaceRole role) => (place.Kind, role) switch
    {
        (PlaceKinds.Home, _) or (PlaceKinds.Locale, _) => null,
        (_, PlaceRole.Owner) => $"{Cap(place.Name)} is yours: you made it, people are your guests here.",
        (_, PlaceRole.Staff) => $"You work at {place.Name}.",
        (_, PlaceRole.Regular) => $"You're a regular at {place.Name}.",
        _ => null,
    };

    /// <summary>Who just came into the Place staff are working in: greet-worthy, not staff, not you.</summary>
    public static IEnumerable<Body> NewArrivals(PlacesSnapshot places, Guid placeId, Guid self, long now)
        => places.Bodies.Where(b => b.ParticipantId != self && b.PlaceId == placeId && !b.IsWalking && b.AwayAs is null
            && now - b.ArrivedAt <= JustCameIn.TotalMilliseconds);

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
