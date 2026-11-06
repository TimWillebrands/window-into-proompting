using System.Security.Claims;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.World;

namespace PartyTown.Services.Auth;

/// <summary>Turns a verified Clerk identity into the Participant a request may act as.</summary>
public sealed class Actors(IGrainFactory grains)
{
    /// <summary>The caller's Player in this World, if they've made one.</summary>
    public async Task<Guid?> FindPlayerAsync(ClaimsPrincipal user, Guid partyId)
        => user.ClerkUserId() is { } clerkId ? await grains.GetGrain<IWorldClockGrain>(partyId).FindPlayerAsync(clerkId) : null;

    public async Task<Guid> PlayerAsync(ClaimsPrincipal user, Guid partyId)
        => await FindPlayerAsync(user, partyId) ?? throw new NoPlayerException();

    public async Task<Viewer> ViewerAsync(ClaimsPrincipal user, Guid partyId)
        => user.ClerkUserId() is null ? Viewer.Anonymous : new Viewer(await FindPlayerAsync(user, partyId));

    /// <summary>The Participant for a World action; a null claim means the caller's own Player.</summary>
    public async Task<Guid> ActAsync(ClaimsPrincipal user, Guid partyId, Guid? claimed)
    {
        var own = await PlayerAsync(user, partyId);
        var who = claimed ?? own;
        if (!AccessRules.MayActAs(who, own, IsAdmin(user))) throw new ForbiddenActorException("That's not your Player.");
        if (who == own) await RefuseOnAutoAsync(partyId, own);
        return who;
    }

    /// <summary>The sender for a Room message, checked against the party the Room really belongs to.</summary>
    public async Task<Guid> SpeakAsync(ClaimsPrincipal user, Guid chatGroupId, Guid? claimed)
    {
        var partyId = await RequireRoomAsync(user, chatGroupId);
        var own = await PlayerAsync(user, partyId);
        var who = claimed ?? own;
        var owned = who != own && await grains.GetGrain<IWorldClockGrain>(partyId).IsClaimedAsync(who);
        if (!AccessRules.MaySpeakAs(who, own, IsAdmin(user), owned)) throw new ForbiddenActorException("Someone else is that Player.");
        if (who == own) await RefuseOnAutoAsync(partyId, own);
        return who;
    }

    /// <summary>Refuses a private DM to non-members; the route's party id is client input, the Room's isn't.</summary>
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

    /// <summary>The agent is playing them: a human's own hand would talk over it.</summary>
    private async Task RefuseOnAutoAsync(Guid partyId, Guid player)
    {
        if (await grains.IsOnAutoAsync(partyId, player)) throw new OnAutoException();
    }

    public static bool IsAdmin(ClaimsPrincipal user) => user.IsInRole(ClerkAuth.AdminRole);
}
