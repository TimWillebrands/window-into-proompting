using PartyTown.Grains;
using PartyTown.Model;

namespace PartyTown.World.Apps;

public sealed class ConversationRefused(string message, bool notFound = false) : Exception(message)
{
    public bool NotFound { get; } = notFound;
}

public static class Conversations
{
    public const int MaxMembers = 8;
    public const int MaxName = 60;

    public static string Names(IEnumerable<string> names)
    {
        var list = names.ToList();
        return list.Count switch
        {
            0 => "nobody",
            1 => list[0],
            <= 4 => $"{string.Join(", ", list.Take(list.Count - 1))} and {list[^1]}",
            _ => $"{string.Join(", ", list.Take(3))} and {list.Count - 3} others",
        };
    }

    public static string With(IEnumerable<Guid> members, Guid self, Func<Guid, string> nameOf)
        => $"the group chat with {Names(members.Where(id => id != self && id != Narrator.PersonaId).Select(nameOf))}";

    public static string DefaultName(IEnumerable<Guid> members, IReadOnlyDictionary<Guid, string> names)
        => WorldStream.Clip(Names(members.Where(id => id != Narrator.PersonaId).Select(id => names.GetValueOrDefault(id, "someone"))), MaxName);

    public static async Task<ChatGroupInfo> StartAsync(IGrainFactory grains, Guid partyId, Guid starter, IEnumerable<Guid> others, string? name = null)
    {
        var party = grains.GetGrain<IPartyGrain>(partyId);
        var names = (await party.GetParty()).Participants.ToDictionary(p => p.Id, p => p.Name);
        var invited = others.Where(id => id != starter && id != Guid.Empty).Distinct().ToList();
        if (invited.Any(id => id == Narrator.PersonaId || !names.ContainsKey(id))) throw new ConversationRefused("Not everyone named is in this World.");
        if (invited.Count < 2) throw new ConversationRefused("A group needs at least two other people; write to one person in a DM.");
        var members = new List<Guid> { starter };
        members.AddRange(invited);
        if (members.Count > MaxMembers) throw new ConversationRefused($"A group holds at most {MaxMembers} people.");
        var title = string.IsNullOrWhiteSpace(name) ? DefaultName(members, names) : WorldStream.Clip(name.Trim(), MaxName);
        return await party.CreateChatGroup(title, members, starter);
    }

    public static async Task<(ChatGroupInfo Group, bool Started)> InviteAsync(IGrainFactory grains, Guid partyId, Guid inviter, Guid conversationId, Guid invitee)
    {
        var party = grains.GetGrain<IPartyGrain>(partyId);
        var conversation = (await party.GetChatGroups()).FirstOrDefault(g => g.Id == conversationId)
            ?? throw new ConversationRefused("No such conversation.", notFound: true);
        var members = await MembersAsync(grains, conversation);
        if (!members.Contains(inviter)) throw new ConversationRefused("Only someone in a conversation can invite to it.");
        if (members.Contains(invitee)) throw new ConversationRefused("They're in it already.");
        switch (conversation.Kind)
        {
            case ChatGroupKinds.Direct:
                return (await StartAsync(grains, partyId, inviter, members.Append(invitee)), true);
            case ChatGroupKinds.Group:
                if ((await party.GetParty()).Participants.All(p => p.Id != invitee) || invitee == Narrator.PersonaId)
                    throw new ConversationRefused("They're not in this World.");
                if (members.Count >= MaxMembers) throw new ConversationRefused($"A group holds at most {MaxMembers} people.");
                var grown = await party.SetGroupMembersAsync(conversationId, members.Append(invitee).ToList());
                return (grown!, false);
            default:
                throw new ConversationRefused("Only Messenger conversations take invitations.");
        }
    }

    public static async Task<ChatGroupInfo> LeaveAsync(IGrainFactory grains, Guid partyId, Guid who, Guid conversationId)
    {
        var party = grains.GetGrain<IPartyGrain>(partyId);
        var conversation = (await party.GetChatGroups()).FirstOrDefault(g => g.Id == conversationId)
            ?? throw new ConversationRefused("No such conversation.", notFound: true);
        if (conversation.Kind != ChatGroupKinds.Group) throw new ConversationRefused("Only a group can be left.");
        var members = await MembersAsync(grains, conversation);
        if (!members.Contains(who)) throw new ConversationRefused("They're not in that group.");
        return (await party.SetGroupMembersAsync(conversationId, members.Where(id => id != who).ToList()))!;
    }

    public static async Task<List<Guid>> MembersAsync(IGrainFactory grains, ChatGroupInfo conversation)
        => conversation.Kind == ChatGroupKinds.Group || conversation.MemberIds is null
            ? (await grains.GetGrain<IChatGroupGrain>(conversation.Id).GetParticipantIdsAsync()).ToList()
            : conversation.MemberIds.ToList();
}
