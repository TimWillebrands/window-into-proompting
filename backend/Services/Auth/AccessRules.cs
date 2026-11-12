using PartyTown.Model;
using PartyTown.World;

namespace PartyTown.Services.Auth;

/// <summary>Who is looking: a visitor, or a signed-in human, who may not have made a Player yet.</summary>
public sealed record Viewer(Guid? PlayerId, bool SignedIn = true)
{
    public static readonly Viewer Anonymous = new(null, false);
}

/// <summary>Pure access decisions; callers supply the facts.</summary>
public static class AccessRules
{
    public const string MessengerApp = "messenger";
    public const string DiaryApp = "diary";

    /// <summary>World actions: your own Player, or anyone for an admin.</summary>
    public static bool MayActAs(Guid claimed, Guid own, bool isAdmin) => claimed == own || isAdmin;

    /// <summary>Room speech: also any Persona no human owns, which keeps Driver overrides working.</summary>
    public static bool MaySpeakAs(Guid claimed, Guid own, bool isAdmin, bool ownedByHuman)
        => MayActAs(claimed, own, isAdmin) || !ownedByHuman;

    /// <summary>Humans for privacy: ownership outlasts the Driver, so a Player on auto stays one (ADR 0037).</summary>
    public static IReadOnlySet<Guid> Humans(IEnumerable<PartyParticipant> cast, IEnumerable<Guid> claimed)
        => cast.Where(p => p.Driver == DriverKind.User).Select(p => p.Id).Concat(claimed).ToHashSet();

    /// <summary>A human's own hand: one of <see cref="Humans"/>, and not on auto, where the agent speaks for them.</summary>
    public static bool AtTheKeyboard(Guid id, IReadOnlyCollection<PartyParticipant> cast, IEnumerable<Guid> claimed)
        => Humans(cast, claimed).Contains(id) && !cast.Any(p => p.Id == id && p.Driver == DriverKind.LLM);

    /// <summary>Members of a DM with a human in it; null for anything public, Persona-only DMs included.</summary>
    public static IReadOnlyCollection<Guid>? PrivateMembers(ChatGroupInfo group, IReadOnlySet<Guid> humans)
        => group.Kind == ChatGroupKinds.Direct && group.MemberIds is { } members && members.Any(humans.Contains) ? members : null;

    public static bool CanSeeRoom(Viewer viewer, IReadOnlyCollection<Guid>? privateMembers)
        => privateMembers is null || (viewer.PlayerId is { } id && privateMembers.Contains(id));

    /// <summary>Activity summaries quote the text, so they follow the same rules as what they quote.</summary>
    public static bool CanSeeActivity(Viewer viewer, WorldActivity activity, IReadOnlySet<Guid> humans) => activity.App switch
    {
        DiaryApp => viewer.SignedIn,
        MessengerApp when humans.Contains(activity.PersonaId) || activity.WithPersonaId is { } with && humans.Contains(with)
            => viewer.PlayerId is { } id && (id == activity.PersonaId || id == activity.WithPersonaId),
        _ => true,
    };

    public const int MaxPlayerName = 40;

    /// <summary>A character name fit to use, or null.</summary>
    public static string? CleanPlayerName(string? name)
    {
        var clean = string.Join(' ', (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length is > 0 and <= MaxPlayerName && !clean.Any(char.IsControl) ? clean : null;
    }
}
