using System.Text;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.World.Self;

namespace PartyTown.World.Apps;

/// <summary>D'Accord Rooms: synchronous group talk. Initiative opens a line; the pipeline does the rest.</summary>
public sealed class RoomsApp(IGrainFactory grains) : IWorldApp
{
    public string Id => "rooms";

    public async Task<AppPerception?> PerceiveAsync(LifeContext ctx, CancellationToken ct)
    {
        var mine = new List<(ChatGroupInfo Room, List<ChatMessage> Recent)>();
        var elsewhere = new List<(ChatGroupInfo Room, List<ChatMessage> Recent, List<Guid> Members)>();
        foreach (var room in ctx.Threads.Where(t => t.Kind == ChatGroupKinds.Room))
        {
            var grain = grains.GetGrain<IChatGroupGrain>(room.Id);
            var members = await grain.GetParticipantIdsAsync();
            var others = members.Where(id => id != ctx.Self.Id && id != Narrator.PersonaId).ToList();
            if (others.Count == 0) continue;
            if (!members.Contains(ctx.Self.Id))
            {
                elsewhere.Add((room, await grain.GetRecentMessagesAsync(2), others));
                continue;
            }
            // A Room where someone else drives this Persona isn't theirs to speak in.
            if ((await grain.GetDriverOverridesAsync()).TryGetValue(ctx.Self.Id, out var driver) && driver != DriverKind.LLM) continue;
            mine.Add((room, await grain.GetRecentMessagesAsync(ChatAttention.MaxHeld + ChatAttention.ReadBack)));
        }
        if (mine.Count == 0 && elsewhere.Count == 0) return null;

        // Gatherings this Persona said they'd attend that are happening now.
        var nowMs = ctx.Now.ToUnixTimeMilliseconds();
        var happeningNow = (await grains.GetGrain<ICalendarGrain>(ctx.PartyId).GetGatheringsAsync())
            .Where(g => g.Status == GatheringStatus.Started && nowMs - g.StartsAt < 45 * 60_000 && g.AttendeeIds.Contains(ctx.Self.Id))
            .ToDictionary(g => g.RoomId, g => g);

        var sb = new StringBuilder();
        var affordances = new List<Affordance>();
        var scrollable = new List<(ChatGroupInfo Room, bool Unread)>();
        foreach (var (room, recent) in mine.OrderByDescending(r => happeningNow.ContainsKey(r.Room.Id)).ThenByDescending(r => LastAt(r.Room, r.Recent)).Take(4))
        {
            // Once you've spoken there since it started, you've shown up — it's an ordinary room again.
            if (happeningNow.TryGetValue(room.Id, out var gathering)
                && recent.Any(m => m.SenderId == ctx.Self.Id && (m.SendAt ?? 0) >= gathering.StartsAt))
                happeningNow.Remove(room.Id);
            if (happeningNow.TryGetValue(room.Id, out gathering))
            {
                sb.AppendLine($"#{room.Name}: \"{gathering.Title}\" is happening there right now — you said you'd come.");
                affordances.Add(new Affordance(Id, $"Show up for \"{gathering.Title}\" in #{room.Name}", 5, room.Id, "say"));
            }
            var last = recent.LastOrDefault();
            var scenario = string.IsNullOrWhiteSpace(room.Scenario) ? "" : $" — {WorldStream.Clip(room.Scenario, 100)}";
            var glance = ChatAttention.Glance(recent, ctx.Self.Id, ctx.Now, ctx.ReadFrom(room.Id));
            if (last is null)
                sb.AppendLine($"#{room.Name}{scenario}: empty, nobody has said anything yet.");
            else if (glance.Held.Count > 0)
            {
                // In the middle of it, or just caught up: the lines are in mind.
                sb.AppendLine($"#{room.Name}{scenario} ({(glance.InExchange ? "you're in this conversation" : "you just caught up on it")}, last active {WorldStream.Ago(last.SendAt ?? 0, ctx.Now)}):");
                foreach (var m in glance.Held.TakeLast(glance.InExchange ? 5 : 12))
                    sb.AppendLine($"  {(m.SenderId == ctx.Self.Id ? "you" : ctx.NameOf(m.SenderId))}: {WorldStream.Clip(m.Content, 140)}");
            }
            else
            {
                // Not open: a badge, and a preview only where someone named you.
                sb.AppendLine(glance.Unread == 0
                    ? $"#{room.Name}{scenario}: you spoke last, {WorldStream.Ago(last.SendAt ?? 0, ctx.Now)}"
                    : $"#{room.Name}{scenario}: {glance.Unread} new message{(glance.Unread == 1 ? "" : "s")} since you last spoke, latest {WorldStream.Ago(last.SendAt ?? 0, ctx.Now)}");
                if (recent.TakeLast(glance.Unread).LastOrDefault(m => m.SenderId != ctx.Self.Id && ctx.Mentions(m.Content)) is { } mention)
                    sb.AppendLine($"  {ctx.NameOf(mention.SenderId)} mentioned you: {WorldStream.Clip(mention.Content, 120)}");
            }
            if (glance.MoreAbove && ctx.ReadFrom(room.Id) is null && last is not null) scrollable.Add((room, glance.Unread > 0));

            if (last?.SenderId == ctx.Self.Id || happeningNow.ContainsKey(room.Id)) continue;
            // A circling or fizzling room (thermostat, ADR 0021) pulls less: people drift elsewhere.
            var stale = last is not null && (await grains.GetGrain<Self.IConversationThermostatGrain>(room.Id).GetReadAsync()).Stale;
            if (stale) sb.AppendLine("  (this conversation has run its course: going round in circles or just re-confirming)");
            var priority = 1 + (last is not null && ctx.Mentions(last.Content) ? 2 : 0) - (stale ? 2 : 0);
            affordances.Add(new Affordance(Id,
                last is null ? $"Start a conversation in #{room.Name}" : $"Say something in #{room.Name}",
                priority, room.Id, "say"));
        }

        if (scrollable.OrderByDescending(r => r.Unread).ThenBy(_ => Random.Shared.Next()).FirstOrDefault() is { Room: not null } scroll)
            affordances.Add(new Affordance(Id, $"Catch up on what's been said in #{scroll.Room.Name}", scroll.Unread ? 1 : 0, scroll.Room.Id, "scroll", Look: true));

        // Places you're not in yet: the arena extends past your current rooms.
        foreach (var (room, recent, others) in elsewhere.OrderByDescending(r => LastAt(r.Room, r.Recent)).Take(2))
        {
            var who = string.Join(", ", others.Take(3).Select(ctx.NameOf));
            var last = recent.LastOrDefault();
            sb.AppendLine($"#{room.Name} (you're not in it): {who} hang out there{(last is null ? "" : $", last active {WorldStream.Ago(last.SendAt ?? 0, ctx.Now)}")}");
            var lively = last is not null && ctx.Now - DateTimeOffset.FromUnixTimeMilliseconds(last.SendAt ?? 0) < TimeSpan.FromMinutes(30);
            affordances.Add(new Affordance(Id, $"Drop into #{room.Name} and join {who}", lively ? 1 : 0, room.Id, "join"));
        }

        return new AppPerception(Id, "Chat rooms (D'Accord)", sb.ToString().TrimEnd(), affordances);
    }

    private static long LastAt(ChatGroupInfo room, List<ChatMessage> recent) => recent.LastOrDefault()?.SendAt ?? room.CreatedAt;

    public async Task<ActOutcome?> ActAsync(LifeContext ctx, Affordance affordance, string intent, string? thought, CancellationToken ct)
    {
        if (affordance.TargetId is not { } roomId) return null;
        if (affordance.Verb == "scroll")
        {
            var history = await grains.GetGrain<IChatGroupGrain>(roomId).GetRecentMessagesAsync(ChatAttention.MaxHeld + ChatAttention.ReadBack * 2);
            await ctx.LookAsync(grains, roomId, ChatAttention.ScrollTarget(history, ctx.ReadFrom(roomId)) ?? history.FirstOrDefault()?.MessageId);
            return ActOutcome.Seen($"caught up on #{ctx.Threads.FirstOrDefault(t => t.Id == roomId)?.Name ?? "a room"}", roomId);
        }
        if (affordance.Verb == "join")
        {
            await grains.GetGrain<IChatGroupGrain>(roomId).JoinAsync(new List<Guid> { ctx.Self.Id });
        }
        var line = await grains.GetGrain<IPersonaGrain>(ctx.Self.Id).InitiateAsync(roomId, intent, thought);
        if (string.IsNullOrWhiteSpace(line)) return null;
        var name = ctx.Threads.FirstOrDefault(t => t.Id == roomId)?.Name ?? "a room";
        return new ActOutcome(affordance.Verb == "join" ? "join" : "say", $"in #{name}: \"{WorldStream.Clip(line, 160)}\"", roomId);
    }
}

/// <summary>Messenger: synchronous 1:1. DM threads are Rooms with a two-person cast (ADR 0018).</summary>
/// <summary>Texting manners: one follow-up after a while, then wait.</summary>
public static class MessengerManners
{
    public static readonly TimeSpan FollowUpAfter = TimeSpan.FromHours(3);

    public static bool MayFollowUp(int unanswered, TimeSpan since) => unanswered == 1 && since >= FollowUpAfter;
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
        var nearby = await CoPresentAsync(ctx);

        var scrollable = new List<(ChatGroupInfo Dm, string Other, bool Unread)>();
        foreach (var dm in ctx.Threads.Where(t => t.Kind == ChatGroupKinds.Direct && t.MemberIds?.Contains(ctx.Self.Id) == true))
        {
            var otherId = dm.MemberIds!.First(id => id != ctx.Self.Id);
            var other = ctx.NameOf(otherId);
            var recent = await grains.GetGrain<IChatGroupGrain>(dm.Id).GetRecentMessagesAsync(ChatAttention.MaxHeld + ChatAttention.ReadBack);
            var glance = ChatAttention.Glance(recent, ctx.Self.Id, ctx.Now, ctx.ReadFrom(dm.Id));
            if (glance.Latest is not { } last) continue;

            var lastAt = DateTimeOffset.FromUnixTimeMilliseconds(last.SendAt ?? 0);
            if (ctx.Now - lastAt < FreshThread) recentlyTalked.Add(otherId);

            // Only a chat you're in the middle of, or just scrolled through, is in mind; the rest is a notification.
            var who = $"{other}{(ctx.IsHuman(otherId) ? " (a real human)" : "")}";
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

            // A waiting human pulls hard; persona threads already get live replies from the pipeline.
            // Someone in the same Place gets talked to out loud, not texted (Places co-presence rule).
            if (last.SenderId != ctx.Self.Id)
            {
                // A persona chat that has run its course doesn't need the last word (ADR 0021).
                var done = !ctx.IsHuman(otherId) && (await grains.GetGrain<Self.IConversationThermostatGrain>(dm.Id).GetReadAsync()).Stale;
                if (done) sb.AppendLine("  (this chat has run its course; it's fine to leave it here)");
                affordances.Add(new Affordance(Id, $"Reply to {other} in Messenger (they wrote {WorldStream.Ago(last.SendAt ?? 0, ctx.Now)})",
                    nearby.Contains(otherId) || done ? 0 : ctx.IsHuman(otherId) ? 4 : 1, dm.Id, "reply"));
            }
            else
            {
                // Unanswered: people double-text at most once, and only after a while (ADR 0021).
                waitingOn.Add(otherId);
                var unanswered = recent.AsEnumerable().Reverse().TakeWhile(m => m.SenderId == ctx.Self.Id).Count();
                sb.AppendLine($"  (you're waiting on {other}{(unanswered >= 2 ? "; you've already followed up, so leave it with them" : "")})");
                if (MessengerManners.MayFollowUp(unanswered, ctx.Now - lastAt) && !nearby.Contains(otherId))
                    affordances.Add(new Affordance(Id, $"Follow up with {other} (no answer to your message from {WorldStream.Ago(last.SendAt ?? 0, ctx.Now)})", 0, otherId, "open"));
            }
        }

        // One chat worth scrolling back through: the one with something new in it, else any.
        if (scrollable.OrderByDescending(c => c.Unread).ThenBy(_ => Random.Shared.Next()).FirstOrDefault() is { Dm: not null } scroll)
            affordances.Add(new Affordance(Id, $"Scroll back through your chat with {scroll.Other} to see what you talked about before",
                0, scroll.Dm.Id, "scroll", Look: true));

        var newContacts = ctx.Others
            .Where(c => !recentlyTalked.Contains(c.Id) && !nearby.Contains(c.Id) && !waitingOn.Contains(c.Id))
            .OrderByDescending(c => ctx.IsHuman(c.Id))
            .ThenBy(_ => Random.Shared.Next())
            .Take(3);
        foreach (var c in newContacts)
            affordances.Add(new Affordance(Id, $"Send {c.Name} a private message", ctx.IsHuman(c.Id) ? 2 : 1, c.Id, "open"));

        if (sb.Length == 0) sb.Append("No private conversations going on.");
        return new AppPerception(Id, "Messenger (private 1:1 chats)", sb.ToString().TrimEnd(), affordances);
    }

    private async Task<HashSet<Guid>> CoPresentAsync(LifeContext ctx)
    {
        var bodies = (await grains.GetGrain<Places.IPlacesGrain>(ctx.PartyId).GetSnapshotAsync()).Bodies;
        var self = bodies.FirstOrDefault(b => b.ParticipantId == ctx.Self.Id);
        if (self is null || self.IsWalking) return new HashSet<Guid>();
        return bodies.Where(b => b.ParticipantId != ctx.Self.Id && !b.IsWalking && b.PlaceId == self.PlaceId)
            .Select(b => b.ParticipantId).ToHashSet();
    }

    public async Task<ActOutcome?> ActAsync(LifeContext ctx, Affordance affordance, string intent, string? thought, CancellationToken ct)
    {
        if (affordance.TargetId is not { } target) return null;
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
}
