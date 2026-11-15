using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.World.Places;

namespace PartyTown.World;

public sealed class PlayerRemoval(IGrainFactory grains)
{
    public async Task<bool> RemoveAsync(Guid partyId, Guid playerId)
    {
        var party = grains.GetGrain<IPartyGrain>(partyId);
        if (!(await grains.HumansAsync(partyId)).Contains(playerId)) return false;

        foreach (var group in await party.GetChatGroups())
        {
            if (group.Kind == ChatGroupKinds.Direct && group.MemberIds?.Contains(playerId) == true)
                await party.RemoveChatGroup(group.Id);
            else
                await RemoveMessagesAsync(group.Id, playerId);
        }

        if (await party.RemoveParticipantAsync(playerId) is null) return false;
        await grains.GetGrain<IPlacesGrain>(partyId).RemoveParticipantAsync(playerId);
        await grains.GetGrain<Relations.IRelationsGrain>(partyId).LeaveAsync(playerId);
        await grains.GetGrain<IBoardGrain>(partyId).RemoveParticipantAsync(playerId);
        await LeaveGatheringsAsync(partyId, playerId);
        await grains.GetGrain<IWorldClockGrain>(partyId).ForgetPlayerAsync(playerId);
        await grains.GetGrain<IPersonaRootGrain>(Guid.Empty).RemovePersona(playerId);
        return true;
    }

    private async Task LeaveGatheringsAsync(Guid partyId, Guid playerId)
    {
        var calendar = grains.GetGrain<ICalendarGrain>(partyId);
        foreach (var gathering in await calendar.GetGatheringsAsync())
        {
            if (gathering.HostId == playerId) await calendar.CancelAsync(gathering.Id);
            else if (gathering.AttendeeIds.Contains(playerId)) await calendar.RsvpAsync(gathering.Id, playerId, false);
        }
    }

    private async Task RemoveMessagesAsync(Guid chatGroupId, Guid playerId)
    {
        var room = grains.GetGrain<IChatGroupGrain>(chatGroupId);
        var theirs = (await room.GetMessagesAsync()).Where(m => m.SenderId == playerId).OrderByDescending(m => m.MessageId);
        foreach (var message in theirs) await room.DeleteMessageAsync(message.MessageId);
    }
}
