using System.Text;
using PartyTown.Grains;
using PartyTown.Model;

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
            mine.Add((room, await grain.GetRecentMessagesAsync(3)));
        }
        if (mine.Count == 0 && elsewhere.Count == 0) return null;

        // Gatherings this Persona said they'd attend that are happening now.
        var nowMs = ctx.Now.ToUnixTimeMilliseconds();
        var happeningNow = (await grains.GetGrain<ICalendarGrain>(ctx.PartyId).GetGatheringsAsync())
            .Where(g => g.Status == GatheringStatus.Started && nowMs - g.StartsAt < 45 * 60_000 && g.AttendeeIds.Contains(ctx.Self.Id))
            .ToDictionary(g => g.RoomId, g => g);

        var sb = new StringBuilder();
        var affordances = new List<Affordance>();
        foreach (var (room, recent) in mine.OrderByDescending(r => happeningNow.ContainsKey(r.Room.Id)).ThenByDescending(r => LastAt(r.Room, r.Recent)).Take(4))
        {
            if (happeningNow.TryGetValue(room.Id, out var gathering))
            {
                sb.AppendLine($"#{room.Name}: \"{gathering.Title}\" is happening there right now — you said you'd come.");
                affordances.Add(new Affordance(Id, $"Show up for \"{gathering.Title}\" in #{room.Name}", 5, room.Id, "say"));
            }
            var last = recent.LastOrDefault();
            var scenario = string.IsNullOrWhiteSpace(room.Scenario) ? "" : $" — {WorldStream.Clip(room.Scenario, 100)}";
            sb.AppendLine(last is null
                ? $"#{room.Name}{scenario}: empty, nobody has said anything yet."
                : $"#{room.Name}{scenario}: last active {WorldStream.Ago(last.SendAt ?? 0, ctx.Now)}");
            foreach (var m in recent)
                sb.AppendLine($"  {(m.SenderId == ctx.Self.Id ? "you" : ctx.NameOf(m.SenderId))}: {WorldStream.Clip(m.Content, 140)}");

            if (last?.SenderId == ctx.Self.Id || happeningNow.ContainsKey(room.Id)) continue;
            var priority = 1 + (last is not null && ctx.Mentions(last.Content) ? 2 : 0);
            affordances.Add(new Affordance(Id,
                last is null ? $"Start a conversation in #{room.Name}" : $"Say something in #{room.Name}",
                priority, room.Id, "say"));
        }

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
        if (affordance.Verb == "join")
        {
            var room = grains.GetGrain<IChatGroupGrain>(roomId);
            var members = (await room.GetParticipantIdsAsync()).ToHashSet();
            members.Add(ctx.Self.Id);
            await room.SetParticipantIdsAsync(members);
        }
        var line = await grains.GetGrain<IPersonaGrain>(ctx.Self.Id).InitiateAsync(roomId, intent, thought);
        if (string.IsNullOrWhiteSpace(line)) return null;
        var name = ctx.Threads.FirstOrDefault(t => t.Id == roomId)?.Name ?? "a room";
        return new ActOutcome(affordance.Verb == "join" ? "join" : "say", $"in #{name}: \"{WorldStream.Clip(line, 160)}\"", roomId);
    }
}

/// <summary>Messenger: synchronous 1:1. DM threads are Rooms with a two-person cast (ADR 0018).</summary>
public sealed class MessengerApp(IGrainFactory grains) : IWorldApp
{
    private static readonly TimeSpan FreshThread = TimeSpan.FromMinutes(30);

    public string Id => "messenger";

    public async Task<AppPerception?> PerceiveAsync(LifeContext ctx, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var affordances = new List<Affordance>();
        var recentlyTalked = new HashSet<Guid>();

        foreach (var dm in ctx.Threads.Where(t => t.Kind == ChatGroupKinds.Direct && t.MemberIds?.Contains(ctx.Self.Id) == true))
        {
            var otherId = dm.MemberIds!.First(id => id != ctx.Self.Id);
            var other = ctx.NameOf(otherId);
            var recent = await grains.GetGrain<IChatGroupGrain>(dm.Id).GetRecentMessagesAsync(3);
            var last = recent.LastOrDefault();
            if (last is null) continue;

            var lastAt = DateTimeOffset.FromUnixTimeMilliseconds(last.SendAt ?? 0);
            if (ctx.Now - lastAt < FreshThread) recentlyTalked.Add(otherId);

            sb.AppendLine($"Chat with {other}{(ctx.IsHuman(otherId) ? " (a real human)" : "")} (last message {WorldStream.Ago(last.SendAt ?? 0, ctx.Now)}):");
            foreach (var m in recent)
                sb.AppendLine($"  {(m.SenderId == ctx.Self.Id ? "you" : other)}: {WorldStream.Clip(m.Content, 140)}");

            // A waiting human pulls hard; persona threads already get live replies from the pipeline.
            if (last.SenderId != ctx.Self.Id)
                affordances.Add(new Affordance(Id, $"Reply to {other} in Messenger (they wrote {WorldStream.Ago(last.SendAt ?? 0, ctx.Now)})",
                    ctx.IsHuman(otherId) ? 4 : 1, dm.Id, "reply"));
            else
                sb.AppendLine($"  (you're waiting on {other})");
        }

        var newContacts = ctx.Others
            .Where(c => !recentlyTalked.Contains(c.Id))
            .OrderByDescending(c => ctx.IsHuman(c.Id))
            .ThenBy(_ => Random.Shared.Next())
            .Take(3);
        foreach (var c in newContacts)
            affordances.Add(new Affordance(Id, $"Send {c.Name} a private message", ctx.IsHuman(c.Id) ? 2 : 1, c.Id, "open"));

        if (sb.Length == 0) sb.Append("No private conversations going on.");
        return new AppPerception(Id, "Messenger (private 1:1 chats)", sb.ToString().TrimEnd(), affordances);
    }

    public async Task<ActOutcome?> ActAsync(LifeContext ctx, Affordance affordance, string intent, string? thought, CancellationToken ct)
    {
        if (affordance.TargetId is not { } target) return null;

        var dmId = target;
        if (affordance.Verb == "open")
            dmId = (await grains.GetGrain<IPartyGrain>(ctx.PartyId).GetOrCreateDirectChatAsync(ctx.Self.Id, target)).Id;

        var line = await grains.GetGrain<IPersonaGrain>(ctx.Self.Id).InitiateAsync(dmId, intent, thought);
        if (string.IsNullOrWhiteSpace(line)) return null;

        var dm = ctx.Threads.FirstOrDefault(t => t.Id == dmId);
        var other = affordance.Verb == "open"
            ? ctx.NameOf(target)
            : ctx.NameOf(dm?.MemberIds?.FirstOrDefault(id => id != ctx.Self.Id) ?? Guid.Empty);
        return new ActOutcome("dm", $"to {other}: \"{WorldStream.Clip(line, 160)}\"", dmId);
    }
}
