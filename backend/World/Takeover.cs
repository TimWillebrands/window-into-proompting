using PartyTown.Model;
using PartyTown.Services.Auth;

namespace PartyTown.World;

/// <summary>Playing as a Persona already here (ADR 0059): who's free to take.</summary>
public static class Takeover
{
    /// <summary>LLM-driven, unowned, and in no DM with a human, whose private words the taker would read.</summary>
    public static List<PartyParticipant> Free(IEnumerable<PartyParticipant> cast, IEnumerable<Guid> claimed, IEnumerable<ChatGroupInfo> groups)
    {
        var people = cast.ToList();
        var humans = AccessRules.Humans(people, claimed);
        var confided = groups.Where(g => AccessRules.PrivateMembers(g, humans) is not null).SelectMany(g => g.MemberIds!).ToHashSet();
        return people.Where(p => p.Driver == DriverKind.LLM && p.Id != Narrator.PersonaId && !humans.Contains(p.Id) && !confided.Contains(p.Id)).ToList();
    }
}
