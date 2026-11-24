using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Auth;

namespace PartyTown.World;

public static class Takeover
{
    public static List<PartyParticipant> Free(IEnumerable<PartyParticipant> cast, IEnumerable<Guid> claimed, IEnumerable<ChatGroupInfo> groups, IReadOnlySet<Guid>? humansWroteIn = null)
    {
        var people = cast.ToList();
        var humans = AccessRules.Humans(people, claimed);
        var confided = groups
            .Where(g => AccessRules.PrivateMembers(g, humans) is not null && (g.Kind != ChatGroupKinds.Group || humansWroteIn?.Contains(g.Id) == true))
            .SelectMany(g => g.MemberIds!)
            .ToHashSet();
        return people.Where(p => p.Driver == DriverKind.LLM && p.Id != Narrator.PersonaId && !humans.Contains(p.Id) && !confided.Contains(p.Id)).ToList();
    }

    public static async Task<List<PartyParticipant>> FreeAsync(IGrainFactory grains, IEnumerable<PartyParticipant> cast, IEnumerable<Guid> claimed, IEnumerable<ChatGroupInfo> groups)
    {
        var people = cast.ToList();
        var claims = claimed.ToList();
        var all = groups.ToList();
        var humans = AccessRules.Humans(people, claims);
        var wroteIn = new HashSet<Guid>();
        foreach (var group in all.Where(g => g.Kind == ChatGroupKinds.Group && AccessRules.PrivateMembers(g, humans) is not null))
            if (await grains.GetGrain<IChatGroupGrain>(group.Id).AnySaidByAsync(humans.ToList()))
                wroteIn.Add(group.Id);
        return Free(people, claims, all, wroteIn);
    }
}
