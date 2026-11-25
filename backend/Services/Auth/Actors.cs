using System.Security.Claims;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.World;

namespace PartyTown.Services.Auth;

public sealed class Actors(IGrainFactory grains)
{
    public async Task<Guid?> FindPlayerAsync(ClaimsPrincipal user, Guid partyId)
        => user.ClerkUserId() is { } clerkId ? await grains.GetGrain<IWorldClockGrain>(partyId).FindPlayerAsync(clerkId) : null;

    public async Task<Guid> PlayerAsync(ClaimsPrincipal user, Guid partyId)
        => await FindPlayerAsync(user, partyId) ?? throw new NoPlayerException();

    public async Task<Viewer> ViewerAsync(ClaimsPrincipal user, Guid partyId)
        => user.ClerkUserId() is null ? Viewer.Anonymous : new Viewer(await FindPlayerAsync(user, partyId));

    public async Task<Guid> ActAsync(ClaimsPrincipal user, Guid partyId, Guid? claimed)
    {
        var own = await PlayerAsync(user, partyId);
        var who = claimed ?? own;
        if (!AccessRules.MayActAs(who, own, IsAdmin(user))) throw new ForbiddenActorException("That's not your Player.");
        if (who == own) await RefuseOnAutoAsync(partyId, own);
        return who;
    }

    public async Task<Guid> SpeakAsync(ClaimsPrincipal user, Guid chatGroupId, Guid? claimed)
    {
        var partyId = await RequireRoomAsync(user, chatGroupId);
        var own = await PlayerAsync(user, partyId);
        var who = claimed ?? own;
        var owned = who != own && await grains.GetGrain<IWorldClockGrain>(partyId).IsClaimedAsync(who);
        if (!AccessRules.MaySpeakAs(who, own, IsAdmin(user), owned)) throw new ForbiddenActorException("Someone else is that Player.");
        if (who == own) await RefuseOnAutoAsync(partyId, own);
        await RequireMemberAsync(partyId, chatGroupId, who);
        return who;
    }

    private async Task RequireMemberAsync(Guid partyId, Guid chatGroupId, Guid who)
    {
        var group = (await grains.GetGrain<IPartyGrain>(partyId).GetChatGroups()).FirstOrDefault(g => g.Id == chatGroupId);
        if (group?.Kind is not (ChatGroupKinds.Direct or ChatGroupKinds.Group)) return;
        if (!(await grains.GetGrain<IChatGroupGrain>(chatGroupId).GetParticipantIdsAsync()).Contains(who))
            throw new ForbiddenActorException("They're not in that conversation.");
    }

    public async Task<Guid> RequireRoomAsync(ClaimsPrincipal user, Guid chatGroupId)
    {
        var partyId = await grains.GetGrain<IChatGroupGrain>(chatGroupId).GetPartyIdAsync();
        var party = grains.GetGrain<IPartyGrain>(partyId);
        var group = (await party.GetChatGroups()).FirstOrDefault(g => g.Id == chatGroupId);
        if (group is null) return partyId;
        var members = AccessRules.PrivateMembers(group, await HumansAsync(partyId));
        return AccessRules.CanSeeRoom(await ViewerAsync(user, partyId), members)
            ? partyId
            : throw new ForbiddenActorException("That conversation is private.");
    }

    public Task<IReadOnlySet<Guid>> HumansAsync(Guid partyId) => grains.HumansAsync(partyId);

    private async Task RefuseOnAutoAsync(Guid partyId, Guid player)
    {
        if (await grains.IsOnAutoAsync(partyId, player)) throw new OnAutoException();
    }

    public static bool IsAdmin(ClaimsPrincipal user) => user.IsInRole(ClerkAuth.AdminRole);
}
