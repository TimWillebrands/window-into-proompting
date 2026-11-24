using System.Text;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.World.Self;

namespace PartyTown.World.Apps;

public static class MessengerManners
{
    public static readonly TimeSpan FollowUpAfter = TimeSpan.FromHours(3);

    public static bool MayFollowUp(int unanswered, TimeSpan since) => unanswered == 1 && since >= FollowUpAfter;

    public static bool Chase(bool settled, bool owed) => owed || !settled;
}

public static class GroupManners
{
    public static readonly TimeSpan LiveFor = TimeSpan.FromDays(2);

    public static bool StartedToday(IEnumerable<ChatGroupInfo> threads, Guid self, DateTimeOffset now)
        => threads.Any(t => t.Kind == ChatGroupKinds.Group && t.CreatedBy == self
            && DateTimeOffset.FromUnixTimeMilliseconds(t.CreatedAt).ToLocalTime().Date == now.ToLocalTime().Date);

    public static bool Live(long lastAt, DateTimeOffset now) => now - DateTimeOffset.FromUnixTimeMilliseconds(lastAt) < LiveFor;

    public static bool Share(IEnumerable<(IReadOnlyCollection<Guid> Members, long LastAt)> groups, IReadOnlyCollection<Guid> people, DateTimeOffset now)
        => groups.Any(g => Live(g.LastAt, now) && people.All(g.Members.Contains));
}

public sealed class MessengerApp(IGrainFactory grains) : IWorldApp
{
    private static readonly TimeSpan FreshThread = TimeSpan.FromMinutes(30);

    public string Id => "messenger";

    public async Task<AppPerception?> PerceiveAsync(LifeContext ctx, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var affordances = new List<Affordance>();
        var recentlyTalked = new HashSet<Guid>();
        var waitingOn = new HashSet<Guid>();
        var (nearby, afk) = await CoPresentAsync(ctx);

        var scrollable = new List<(ChatGroupInfo Dm, string Other, bool Unread)>();
        var groups = await GroupsOfAsync(ctx);
        foreach (var dm in ctx.Threads.Where(t => t.Kind == ChatGroupKinds.Direct && t.MemberIds?.Contains(ctx.Self.Id) == true))
        {
            var otherId = dm.MemberIds!.First(id => id != ctx.Self.Id);
            var other = ctx.NameOf(otherId);
            var recent = await grains.GetGrain<IChatGroupGrain>(dm.Id).GetRecentMessagesAsync(ChatAttention.MaxHeld + ChatAttention.ReadBack);
            var glance = ChatAttention.Glance(recent, ctx.Self.Id, ctx.Now, ctx.ReadFrom(dm.Id));
            if (glance.Latest is not { } last) continue;

            var lastAt = DateTimeOffset.FromUnixTimeMilliseconds(last.SendAt ?? 0);
            if (ctx.Now - lastAt < FreshThread) recentlyTalked.Add(otherId);

            var who = $"{other}{(ctx.IsHuman(otherId) ? " (a real human)" : "")}{(afk.Contains(otherId) ? " (AFK: away from the keyboard, won't see it for a while)" : "")}";
            if (glance.Held.Count > 0)
            {
                sb.AppendLine($"Chat with {who} ({(glance.InExchange ? "you're in the middle of it" : "you just scrolled back through it")}, last message {WorldStream.Ago(last.SendAt ?? 0, ctx.Now)}):");
                foreach (var m in glance.Held.TakeLast(glance.InExchange ? 6 : 14))
                    sb.AppendLine($"  {(m.SenderId == ctx.Self.Id ? "you" : other)}: {WorldStream.Clip(m.Content, 140)}");
            }
            else if (last.SenderId != ctx.Self.Id)
                sb.AppendLine($"{who}: {glance.Unread} new message{(glance.Unread == 1 ? "" : "s")}, latest {WorldStream.Ago(last.SendAt ?? 0, ctx.Now)}: \"{WorldStream.Clip(last.Content, 100)}\"");
            else
                sb.AppendLine($"Chat with {who}: you wrote last, {WorldStream.Ago(last.SendAt ?? 0, ctx.Now)}: \"{WorldStream.Clip(last.Content, 80)}\"");
            if (glance.MoreAbove && ctx.ReadFrom(dm.Id) is null) scrollable.Add((dm, other, last.SenderId != ctx.Self.Id));

            if (last.SenderId != ctx.Self.Id && await grains.GetGrain<IChatGroupGrain>(dm.Id).IsAnsweringAsync(ctx.Self.Id))
                sb.AppendLine($"  (you're already answering {other})");
            else if (last.SenderId != ctx.Self.Id)
            {
                var done = !ctx.IsHuman(otherId) && (await grains.GetGrain<Self.IConversationThermostatGrain>(dm.Id).GetReadAsync()).Stale;
                if (done) sb.AppendLine("  (this chat has run its course; it's fine to leave it here)");
                affordances.Add(new Affordance(Id, $"Reply to {other} in Messenger (they wrote {WorldStream.Ago(last.SendAt ?? 0, ctx.Now)})",
                    nearby.Contains(otherId) || done ? 0 : ctx.IsHuman(otherId) && !afk.Contains(otherId) ? 4 : 1, dm.Id, "reply"));
            }
            else
            {
                waitingOn.Add(otherId);
                var unanswered = recent.AsEnumerable().Reverse().TakeWhile(m => m.SenderId == ctx.Self.Id).Count();
                sb.AppendLine($"  (you're waiting on {other}{(unanswered >= 2 ? "; you've already followed up, so leave it with them" : "")})");
                if (MessengerManners.MayFollowUp(unanswered, ctx.Now - lastAt) && !nearby.Contains(otherId) && !afk.Contains(otherId))
                {
                    var settled = !ctx.IsHuman(otherId) && (await grains.GetGrain<Self.IConversationThermostatGrain>(dm.Id).GetReadAsync()).Stale;
                    if (MessengerManners.Chase(settled, ctx.Owes(otherId)))
                        affordances.Add(new Affordance(Id, $"Follow up with {other} (no answer to your message from {WorldStream.Ago(last.SendAt ?? 0, ctx.Now)})", 0, otherId, "open"));
                    else
                        sb.AppendLine($"  (it ended on a settled note; there's nothing to chase {other} about)");
                }
            }
        }

        if (scrollable.OrderByDescending(c => c.Unread).ThenBy(_ => Random.Shared.Next()).FirstOrDefault() is { Dm: not null } scroll)
            affordances.Add(new Affordance(Id, $"Scroll back through your chat with {scroll.Other} to see what you talked about before",
                0, scroll.Dm.Id, "scroll", Look: true));

        var stale = await PerceiveGroupsAsync(ctx, groups, sb, affordances);
        var known = await Acquaintances.KnownAsync(grains, ctx.PartyId, ctx.Self.Id);
        var newContacts = ctx.Others
            .Where(c => known.Contains(c.Id) && !recentlyTalked.Contains(c.Id) && !nearby.Contains(c.Id) && !waitingOn.Contains(c.Id) && !afk.Contains(c.Id))
            .OrderByDescending(c => ctx.IsHuman(c.Id))
            .ThenByDescending(c => ctx.WantAbout(c.Id) is not null)
            .ThenBy(_ => Random.Shared.Next())
            .Take(3);
        var openers = (await grains.GetGrain<Self.IPersonaSelfGrain>(WorldKeys.Life(ctx.PartyId, ctx.Self.Id)).GetStateAsync()).Openers;
        foreach (var c in newContacts)
            affordances.Add(new Affordance(Id, Self.Hooks.Message(c.Name, Self.Hooks.Pick(openers)), ctx.IsHuman(c.Id) ? 2 : 1, c.Id, "open"));

        var people = ctx.Others.Where(c => known.Contains(c.Id) && !afk.Contains(c.Id)).ToList();
        if (await StartingAsync(ctx, groups, people) is { } start) affordances.Add(start);
        if (await InvitingAsync(ctx, groups, stale, people) is { } invite) affordances.Add(invite);

        if (sb.Length == 0) sb.Append("No private conversations going on.");
        return new AppPerception(Id, "Messenger (private chats: one-to-one and in groups)", sb.ToString().TrimEnd(), affordances);
    }

    private sealed record Group(ChatGroupInfo Info, List<Guid> Members, List<Guid> Others, List<ChatMessage> Recent)
    {
        public long LastAt => Recent.LastOrDefault()?.SendAt ?? Info.CreatedAt;
    }

    private async Task<List<Group>> GroupsOfAsync(LifeContext ctx)
    {
        var mine = new List<Group>();
        foreach (var group in ctx.Threads.Where(t => t.Kind == ChatGroupKinds.Group))
        {
            var grain = grains.GetGrain<IChatGroupGrain>(group.Id);
            var members = (await grain.GetParticipantIdsAsync()).ToList();
            if (!members.Contains(ctx.Self.Id)) continue;
            var others = members.Where(id => id != ctx.Self.Id && id != Narrator.PersonaId).ToList();
            if (others.Count == 0) continue;
            mine.Add(new Group(group, members, others, await grain.GetRecentMessagesAsync(ChatAttention.MaxHeld + ChatAttention.ReadBack)));
        }
        return mine;
    }

    private string Label(LifeContext ctx, Group g) => Conversations.With(g.Members, ctx.Self.Id, ctx.NameOf);

    private async Task<HashSet<Guid>> PerceiveGroupsAsync(LifeContext ctx, List<Group> groups, StringBuilder sb, List<Affordance> affordances)
    {
        var stale = new HashSet<Guid>();
        var scrollable = new List<(Group Group, bool Unread)>();
        foreach (var g in groups.OrderByDescending(g => g.LastAt).Take(4))
        {
            var room = g.Info;
            var label = Label(ctx, g);
            var heading = char.ToUpperInvariant(label[0]) + label[1..];
            var last = g.Recent.LastOrDefault();
            var glance = ChatAttention.Glance(g.Recent, ctx.Self.Id, ctx.Now, ctx.ReadFrom(room.Id));
            if (last is null)
                sb.AppendLine($"{heading}: empty, nobody has said anything yet.");
            else if (glance.Held.Count > 0)
            {
                sb.AppendLine($"{heading} ({(glance.InExchange ? "you're in this conversation" : "you just caught up on it")}, last active {WorldStream.Ago(last.SendAt ?? 0, ctx.Now)}):");
                foreach (var m in glance.Held.TakeLast(glance.InExchange ? 5 : 12))
                    sb.AppendLine($"  {(m.SenderId == ctx.Self.Id ? "you" : ctx.NameOf(m.SenderId))}: {WorldStream.Clip(m.Content, 140)}");
            }
            else
            {
                sb.AppendLine(glance.Unread == 0
                    ? $"{heading}: you spoke last, {WorldStream.Ago(last.SendAt ?? 0, ctx.Now)}"
                    : $"{heading}: {glance.Unread} new message{(glance.Unread == 1 ? "" : "s")} since you last spoke, latest {WorldStream.Ago(last.SendAt ?? 0, ctx.Now)}");
                if (g.Recent.TakeLast(glance.Unread).LastOrDefault(m => m.SenderId != ctx.Self.Id && ctx.Mentions(m.Content)) is { } mention)
                    sb.AppendLine($"  {ctx.NameOf(mention.SenderId)} mentioned you: {WorldStream.Clip(mention.Content, 120)}");
            }
            if (glance.MoreAbove && ctx.ReadFrom(room.Id) is null && last is not null) scrollable.Add((g, glance.Unread > 0));

            var settled = last is not null && (await grains.GetGrain<Self.IConversationThermostatGrain>(room.Id).GetReadAsync()).Stale;
            if (settled)
            {
                stale.Add(room.Id);
                sb.AppendLine("  (this conversation has run its course: going round in circles or just re-confirming)");
                affordances.Add(new Affordance(Id, $"Leave {label} (it has run its course)", 0, room.Id, "leave"));
            }
            if (last?.SenderId == ctx.Self.Id) continue;
            if (await grains.GetGrain<IChatGroupGrain>(room.Id).IsAnsweringAsync(ctx.Self.Id)) continue;
            var priority = 1 + (last is not null && ctx.Mentions(last.Content) ? 2 : 0) - (settled ? 2 : 0);
            affordances.Add(new Affordance(Id,
                last is null ? $"Start the conversation in {label}" : $"Say something in {label}",
                priority, room.Id, "say"));
        }

        if (scrollable.OrderByDescending(r => r.Unread).ThenBy(_ => Random.Shared.Next()).FirstOrDefault() is { Group: not null } scroll)
            affordances.Add(new Affordance(Id, $"Catch up on what's been said in {Label(ctx, scroll.Group)}", scroll.Unread ? 1 : 0, scroll.Group.Info.Id, "scroll", Look: true));
        return stale;
    }

    private async Task<Affordance?> StartingAsync(LifeContext ctx, List<Group> groups, List<CastMember> people)
    {
        if (people.Count < 2 || GroupManners.StartedToday(ctx.Threads, ctx.Self.Id, ctx.Now)) return null;
        var shared = groups.Select(g => ((IReadOnlyCollection<Guid>)g.Members, g.LastAt)).ToList();
        foreach (var first in people.OrderBy(_ => Random.Shared.Next()).Take(3))
        {
            var theirs = await Acquaintances.KnownAsync(grains, ctx.PartyId, first.Id);
            var second = people.Where(p => p.Id != first.Id)
                .OrderByDescending(p => theirs.Contains(p.Id))
                .ThenBy(_ => Random.Shared.Next())
                .FirstOrDefault(p => !GroupManners.Share(shared, new[] { ctx.Self.Id, first.Id, p.Id }, ctx.Now));
            if (second is null) continue;
            return new Affordance(Id, $"Start a group chat with {first.Name} and {second.Name}", 0, first.Id, "start",
                With: new List<Guid> { first.Id, second.Id });
        }
        return null;
    }

    private async Task<Affordance?> InvitingAsync(LifeContext ctx, List<Group> groups, HashSet<Guid> stale, List<CastMember> people)
    {
        var group = groups
            .Where(g => !stale.Contains(g.Info.Id) && g.Members.Count < Conversations.MaxMembers && GroupManners.Live(g.LastAt, ctx.Now))
            .OrderByDescending(g => g.LastAt)
            .FirstOrDefault();
        if (group is null) return null;
        foreach (var candidate in people.Where(p => !group.Members.Contains(p.Id)).OrderBy(_ => Random.Shared.Next()).Take(3))
        {
            var theirs = await Acquaintances.KnownAsync(grains, ctx.PartyId, candidate.Id);
            var mutual = group.Others.FirstOrDefault(theirs.Contains);
            if (mutual == Guid.Empty) continue;
            return new Affordance(Id, $"Invite {candidate.Name} into {Label(ctx, group)} ({candidate.Name} knows {ctx.NameOf(mutual)})", 0,
                group.Info.Id, "invite", With: new List<Guid> { candidate.Id });
        }
        return null;
    }

    private async Task<(HashSet<Guid> Nearby, HashSet<Guid> Afk)> CoPresentAsync(LifeContext ctx)
    {
        var bodies = (await grains.GetGrain<Places.IPlacesGrain>(ctx.PartyId).GetSnapshotAsync()).Bodies;
        var afk = bodies.Where(b => b.Afk).Select(b => b.ParticipantId).ToHashSet();
        var self = bodies.FirstOrDefault(b => b.ParticipantId == ctx.Self.Id);
        if (self is null || self.IsWalking) return (new HashSet<Guid>(), afk);
        return (bodies.Where(b => b.ParticipantId != ctx.Self.Id && !b.IsWalking && b.PlaceId == self.PlaceId)
            .Select(b => b.ParticipantId).ToHashSet(), afk);
    }

    public async Task<ActOutcome?> ActAsync(LifeContext ctx, Affordance affordance, string intent, string? thought, CancellationToken ct)
    {
        if (affordance.TargetId is not { } target) return null;
        if (affordance.Verb is "start" or "invite" or "leave" or "say"
            || ctx.Threads.FirstOrDefault(t => t.Id == target) is { Kind: ChatGroupKinds.Group })
            return await ActInGroupAsync(ctx, affordance, target, intent, thought);
        if (affordance.Verb == "scroll")
        {
            var history = await grains.GetGrain<IChatGroupGrain>(target).GetRecentMessagesAsync(ChatAttention.MaxHeld + ChatAttention.ReadBack * 2);
            await ctx.LookAsync(grains, target, ChatAttention.ScrollTarget(history, ctx.ReadFrom(target)) ?? history.FirstOrDefault()?.MessageId);
            var with = ctx.Threads.FirstOrDefault(t => t.Id == target)?.MemberIds?.FirstOrDefault(id => id != ctx.Self.Id);
            return ActOutcome.Seen($"scrolled back through the chat with {(with is { } w ? ctx.NameOf(w) : "someone")}", target, with);
        }

        var dmId = target;
        if (affordance.Verb == "open")
            dmId = (await grains.GetGrain<IPartyGrain>(ctx.PartyId).GetOrCreateDirectChatAsync(ctx.Self.Id, target)).Id;

        var line = await grains.GetGrain<IPersonaGrain>(ctx.Self.Id).InitiateAsync(dmId, intent, thought);
        if (string.IsNullOrWhiteSpace(line)) return null;

        var dm = ctx.Threads.FirstOrDefault(t => t.Id == dmId);
        var otherId = affordance.Verb == "open" ? target : dm?.MemberIds?.FirstOrDefault(id => id != ctx.Self.Id) ?? Guid.Empty;
        return new ActOutcome("dm", $"to {ctx.NameOf(otherId)}: \"{WorldStream.Clip(line, 160)}\"", dmId, otherId);
    }

    private async Task<ActOutcome?> ActInGroupAsync(LifeContext ctx, Affordance affordance, Guid target, string intent, string? thought)
    {
        var self = ctx.Self.Id;
        var persona = grains.GetGrain<IPersonaGrain>(self);
        string Said(string? line) => string.IsNullOrWhiteSpace(line) ? string.Empty : $": \"{WorldStream.Clip(line, 160)}\"";
        async Task<string?> OpenWithAsync(Guid groupId)
        {
            try { return await persona.InitiateAsync(groupId, intent, thought); }
            catch (Exception ex) when (ex is not OperationCanceledException) { return null; }
        }
        try
        {
            switch (affordance.Verb)
            {
                case "start":
                {
                    if (affordance.With is not { Count: >= 2 } with) return null;
                    var group = await Conversations.StartAsync(grains, ctx.PartyId, self, with);
                    var line = await OpenWithAsync(group.Id);
                    return new ActOutcome("group", $"started a group chat with {Conversations.Names(with.Select(ctx.NameOf))}{Said(line)}", group.Id, Among: group.MemberIds);
                }
                case "invite":
                {
                    if (affordance.With is not [var invitee]) return null;
                    var before = ctx.Threads.FirstOrDefault(t => t.Id == target);
                    var label = before is null ? "a group chat" : Conversations.With(await Conversations.MembersAsync(grains, before), self, ctx.NameOf);
                    var (group, _) = await Conversations.InviteAsync(grains, ctx.PartyId, self, target, invitee);
                    var line = await OpenWithAsync(group.Id);
                    return new ActOutcome("invite", $"invited {ctx.NameOf(invitee)} into {label}{Said(line)}", group.Id, invitee, group.MemberIds);
                }
                case "leave":
                {
                    var before = ctx.Threads.FirstOrDefault(t => t.Id == target);
                    if (before is null) return null;
                    var members = await Conversations.MembersAsync(grains, before);
                    await Conversations.LeaveAsync(grains, ctx.PartyId, self, target);
                    return new ActOutcome("leave", $"left {Conversations.With(members, self, ctx.NameOf)}", target, Among: members);
                }
                case "scroll":
                {
                    var room = ctx.Threads.FirstOrDefault(t => t.Id == target);
                    var members = room is null ? new List<Guid>() : await Conversations.MembersAsync(grains, room);
                    var history = await grains.GetGrain<IChatGroupGrain>(target).GetRecentMessagesAsync(ChatAttention.MaxHeld + ChatAttention.ReadBack * 2);
                    await ctx.LookAsync(grains, target, ChatAttention.ScrollTarget(history, ctx.ReadFrom(target)) ?? history.FirstOrDefault()?.MessageId);
                    return ActOutcome.Seen($"caught up on {Conversations.With(members, self, ctx.NameOf)}", target);
                }
                default:
                {
                    var room = ctx.Threads.FirstOrDefault(t => t.Id == target);
                    var members = room is null ? new List<Guid>() : await Conversations.MembersAsync(grains, room);
                    if (!members.Contains(self)) return null;
                    var line = await persona.InitiateAsync(target, intent, thought);
                    if (string.IsNullOrWhiteSpace(line)) return null;
                    return new ActOutcome("say", $"in {Conversations.With(members, self, ctx.NameOf)}{Said(line)}", target, Among: members);
                }
            }
        }
        catch (ConversationRefused)
        {
            return null;
        }
    }
}
