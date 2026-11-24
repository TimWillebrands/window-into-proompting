using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.World.Economy;

namespace PartyTown.World.Notifications;

public sealed class PlayerInbox(IGrainFactory grains)
{
    private const int DmLookback = 30;

    public async Task<NotificationFeed> ForAsync(Guid partyId, Guid playerId, DateTimeOffset now)
    {
        var since = (now - Inbox.Window).ToUnixTimeMilliseconds();
        var party = grains.GetGrain<IPartyGrain>(partyId);
        var info = await party.GetParty();
        var names = info.Participants.ToDictionary(p => p.Id, p => p.Name);
        var selfName = names.GetValueOrDefault(playerId, "");

        var boardTask = grains.GetGrain<IBoardGrain>(partyId).GetThreadsAsync();
        var gatheringsTask = grains.GetGrain<ICalendarGrain>(partyId).GetGatheringsAsync();
        var walletTask = grains.GetGrain<IWalletGrain>(WorldKeys.Life(partyId, playerId)).GetAsync();
        var readTask = Grain(partyId, playerId).GetAsync();
        var dms = (await party.GetChatGroups())
            .Where(g => g.Kind == ChatGroupKinds.Direct && g.MemberIds?.Contains(playerId) == true && g.MemberIds.Any(id => id != playerId))
            .ToList();
        var recent = await Task.WhenAll(dms.Select(dm => grains.GetGrain<IChatGroupGrain>(dm.Id).GetRecentMessagesAsync(DmLookback)));
        var groups = (await party.GetChatGroups())
            .Where(g => g.Kind == ChatGroupKinds.Group && g.MemberIds?.Contains(playerId) == true)
            .ToList();
        var groupRecent = await Task.WhenAll(groups.Select(g => grains.GetGrain<IChatGroupGrain>(g.Id).GetRecentMessagesAsync(DmLookback)));

        var all = new List<PlayerNotification>();
        all.AddRange(Inbox.FromBoard(await boardTask, playerId, selfName, since));
        all.AddRange(Inbox.FromGatherings(await gatheringsTask, playerId, since));
        all.AddRange(Inbox.FromGifts((await walletTask).NewGifts
            .Select(g => new GiftReceived(g.FromId, g.FromName, Catalog.Find(g.Key)?.Name ?? g.Key, g.At)), playerId, since));
        for (var i = 0; i < dms.Count; i++)
        {
            var other = dms[i].MemberIds!.First(id => id != playerId);
            if (Inbox.FromDm(new DmWaiting(dms[i].Id, other, names.GetValueOrDefault(other, "Someone"), recent[i]), playerId, since) is { } n)
                all.Add(n);
        }
        for (var i = 0; i < groups.Count; i++)
            if (Inbox.FromGroup(new GroupWaiting(groups[i].Id, groups[i].Name, groupRecent[i]), playerId, since, id => names.GetValueOrDefault(id, "Someone")) is { } n)
                all.Add(n);

        return Inbox.Feed(all, await readTask);
    }

    public async Task<NotificationFeed> MarkReadAsync(Guid partyId, Guid playerId, List<string>? ids, bool all, DateTimeOffset now)
    {
        await Grain(partyId, playerId).MarkReadAsync(ids ?? new List<string>(), all ? now.ToUnixTimeMilliseconds() : null);
        return await ForAsync(partyId, playerId, now);
    }

    public async Task<NotificationFeed> DismissAsync(Guid partyId, Guid playerId, List<string>? ids, bool all, DateTimeOffset now)
    {
        await Grain(partyId, playerId).DismissAsync(ids ?? new List<string>(), all ? now.ToUnixTimeMilliseconds() : null);
        return await ForAsync(partyId, playerId, now);
    }

    private INotificationsGrain Grain(Guid partyId, Guid playerId) => grains.GetGrain<INotificationsGrain>(WorldKeys.Life(partyId, playerId));
}
