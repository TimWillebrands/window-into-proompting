using PartyTown.Model;
using PartyTown.Services.Auth;

namespace PartyTown.World;

public static class Takeover
{
    public static List<PartyParticipant> Free(IEnumerable<PartyParticipant> cast, IEnumerable<Guid> claimed, IEnumerable<ChatGroupInfo> groups)
    {
        var people = cast.ToList();
        var humans = AccessRules.Humans(people, claimed);
        var confided = groups.Where(g => AccessRules.PrivateMembers(g, humans) is not null).SelectMany(g => g.MemberIds!).ToHashSet();
        return people.Where(p => p.Driver == DriverKind.LLM && p.Id != Narrator.PersonaId && !humans.Contains(p.Id) && !confided.Contains(p.Id)).ToList();
    }
}
