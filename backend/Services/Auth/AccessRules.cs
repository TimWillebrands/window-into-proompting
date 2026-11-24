using PartyTown.Model;
using PartyTown.World;

namespace PartyTown.Services.Auth;

public sealed record Viewer(Guid? PlayerId, bool SignedIn = true)
{
    public static readonly Viewer Anonymous = new(null, false);
}

public static class AccessRules
{
    public const string MessengerApp = "messenger";
    public const string DiaryApp = "diary";

    public static bool MayActAs(Guid claimed, Guid own, bool isAdmin) => claimed == own || isAdmin;

    public static bool MaySpeakAs(Guid claimed, Guid own, bool isAdmin, bool ownedByHuman)
        => MayActAs(claimed, own, isAdmin) || !ownedByHuman;

    public static IReadOnlySet<Guid> Humans(IEnumerable<PartyParticipant> cast, IEnumerable<Guid> claimed)
        => cast.Where(p => p.Driver == DriverKind.User).Select(p => p.Id).Concat(claimed).ToHashSet();

    public static bool AtTheKeyboard(Guid id, IReadOnlyCollection<PartyParticipant> cast, IEnumerable<Guid> claimed)
        => Humans(cast, claimed).Contains(id) && !cast.Any(p => p.Id == id && p.Driver == DriverKind.LLM);

    public static IReadOnlyCollection<Guid>? PrivateMembers(ChatGroupInfo group, IReadOnlySet<Guid> humans)
        => group.Kind is ChatGroupKinds.Direct or ChatGroupKinds.Group && group.MemberIds is { } members && members.Any(humans.Contains) ? members : null;

    public static bool CanSeeRoom(Viewer viewer, IReadOnlyCollection<Guid>? privateMembers)
        => privateMembers is null || (viewer.PlayerId is { } id && privateMembers.Contains(id));

    public static bool CanSeeActivity(Viewer viewer, WorldActivity activity, IReadOnlySet<Guid> humans) => activity.App switch
    {
        DiaryApp => viewer.SignedIn,
        MessengerApp when activity.Among is { } among && among.Any(humans.Contains)
            => viewer.PlayerId is { } id && among.Contains(id),
        MessengerApp when humans.Contains(activity.PersonaId) || activity.WithPersonaId is { } with && humans.Contains(with)
            => viewer.PlayerId is { } id && (id == activity.PersonaId || id == activity.WithPersonaId),
        _ => true,
    };

    public const int MaxPlayerName = 40;

    public static string? CleanPlayerName(string? name)
    {
        var clean = string.Join(' ', (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length is > 0 and <= MaxPlayerName && !clean.Any(char.IsControl) ? clean : null;
    }
}
