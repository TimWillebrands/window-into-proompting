using System.Text;
using PartyTown.Grains;
using PartyTown.World.Apps;

namespace PartyTown.World.Places;

/// <summary>Places: the physical layer. Where your Body is, who's around, walking, talking out loud.</summary>
public sealed class PlacesApp(IGrainFactory grains) : IWorldApp
{
    private static readonly TimeSpan Restless = TimeSpan.FromMinutes(45);

    public string Id => "places";

    public async Task<AppPerception?> PerceiveAsync(LifeContext ctx, CancellationToken ct)
    {
        var places = grains.GetGrain<IPlacesGrain>(ctx.PartyId);
        var situation = await places.GetSituationAsync(ctx.Self.Id);
        var snapshot = await places.GetSnapshotAsync();
        var self = snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == ctx.Self.Id);
        if (situation is null || self is null) return null;

        var sb = new StringBuilder(situation).AppendLine();
        var affordances = new List<Affordance>();
        var here = snapshot.Places.FirstOrDefault(p => p.Id == self.PlaceId);
        var others = snapshot.Bodies
            .Where(b => b.ParticipantId != self.ParticipantId && b.PlaceId == self.PlaceId && (b.DestPlaceId == null || b.DestPlaceId == self.PlaceId))
            .ToList();

        var nearby = others.Where(o => o.SpotId == self.SpotId && !o.IsWalking).ToList();
        if (!self.IsWalking && here?.TalkRoomId is { } talkRoomId && others.Count > 0)
        {
            // Only what you heard (Earshot): talk at other spots reaches you as tone, below.
            var recent = await grains.GetGrain<IChatGroupGrain>(talkRoomId).GetRecentMessagesAsync(8);
            var fresh = recent
                .Where(m => m.HeardBy is not { } heard || heard.Contains(ctx.Self.Id))
                .Where(m => ctx.Now - DateTimeOffset.FromUnixTimeMilliseconds(m.SendAt ?? 0) < TimeSpan.FromMinutes(30))
                .TakeLast(4).ToList();
            if (fresh.Count > 0)
            {
                sb.AppendLine("Said out loud near you just now:");
                foreach (var m in fresh)
                    sb.AppendLine($"  {(m.SenderId == ctx.Self.Id ? "you" : ctx.NameOf(m.SenderId))}{(m.Volume == Volumes.Whisper ? " (whispering)" : m.Volume == Volumes.Shout ? " (calling out)" : "")}: {WorldStream.Clip(m.Content, 140)}");
            }
            var last = fresh.LastOrDefault();
            if (nearby.Count > 0 && last?.SenderId != ctx.Self.Id)
            {
                var who = string.Join(", ", nearby.Take(3).Select(o => o.Name));
                var priority = 2 + (last is not null && ctx.Mentions(last.Content) ? 2 : 0);
                affordances.Add(new Affordance(Id, $"Say something to {who}, right here", priority, talkRoomId, "say"));
                var confidant = nearby[Random.Shared.Next(nearby.Count)];
                affordances.Add(new Affordance(Id, $"Whisper something to {confidant.Name} only", 0, confidant.ParticipantId, "whisper"));
            }
            else if (nearby.Count == 0)
            {
                var who = string.Join(", ", others.Take(3).Select(o => o.Name));
                affordances.Add(new Affordance(Id, $"Call out across the room to {who}", 0, talkRoomId, "shout"));
            }

            foreach (var (line, heard, name) in Overhearing.Lines(await places.GetOverheardAsync(ctx.Self.Id), ctx.Self.Id, here).Take(2))
            {
                sb.AppendLine(line);
                if (heard.SpotId != self.SpotId)
                    affordances.Add(new Affordance(Id, $"Drift over to {SpotName(here, heard.SpotId)} to hear what they're saying",
                        name ? 3 : 1, here.Id, $"spot:{heard.SpotId}"));
            }
        }

        if (!self.IsWalking)
        {
            foreach (var o in others.Where(o => o.SpotId != self.SpotId).OrderBy(_ => Random.Shared.Next()).Take(2))
                affordances.Add(new Affordance(Id, $"Go over to {o.Name} at {SpotName(here, o.SpotId)}", 1, o.ParticipantId, "join"));
            if (here?.Spots.Where(s => s.Id != self.SpotId).OrderBy(_ => Random.Shared.Next()).FirstOrDefault() is { } spot)
                affordances.Add(new Affordance(Id, $"Move over to {spot.Name}", 0, here.Id, $"spot:{spot.Id}"));

            // Care is capped: only the one thing here that needs it most is offered (spec: decay feeds talk, not housework).
            var nowMs = ctx.Now.ToUnixTimeMilliseconds();
            if (here?.Furni.Select(f => (Furni: f, Care: FurniRules.CareFor(f, nowMs)))
                    .Where(x => x.Care is not null)
                    .OrderBy(x => FurniRules.Condition(x.Furni, nowMs))
                    .FirstOrDefault() is { Care: { } care } worst)
                affordances.Add(new Affordance(Id, care.Label, care.Priority, here.Id, $"care:{worst.Furni.Id}"));
            if (here?.Furni.Select(f => (Furni: f, Use: FurniRules.UseOf(f, nowMs)))
                    .Where(x => x.Use is not null)
                    .OrderBy(_ => Random.Shared.Next())
                    .FirstOrDefault() is { Use: { } use } pick)
                affordances.Add(new Affordance(Id, use.Label, use.Priority, here.Id, $"use:{pick.Furni.Id}"));
        }

        var stayed = self.IsWalking ? TimeSpan.Zero : ctx.Now - DateTimeOffset.FromUnixTimeMilliseconds(self.ArrivedAt);
        var restless = stayed > Restless;
        if (stayed > TimeSpan.FromMinutes(20)) sb.AppendLine($"You've been {(here?.OwnerId == ctx.Self.Id ? "at home" : "here")} for {WorldStream.Ago(self.ArrivedAt, ctx.Now).Replace(" ago", "")}.");
        var elsewhere = new List<string>();
        foreach (var place in snapshot.Places.Where(p => p.Id != (self.DestPlaceId ?? self.PlaceId)))
        {
            var mine = place.OwnerId == ctx.Self.Id;
            if (place.Kind == PlaceKinds.Home && !mine)
            {
                // Someone else's Home: go if invited, else knock when they're in and awake.
                var owner = snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == place.OwnerId);
                var ownerName = owner?.Name.Split(' ')[0] ?? "someone";
                if (place.Guests.Contains(ctx.Self.Id))
                {
                    sb.AppendLine($"{ownerName} invited you over to their place.");
                    affordances.Add(new Affordance(Id, $"Go over to {ownerName}'s place (they invited you)", 2, place.Id, "go"));
                }
                else if (owner is { IsWalking: false, Asleep: false } && owner.PlaceId == place.Id && !self.IsWalking)
                {
                    affordances.Add(new Affordance(Id, $"Knock on {ownerName}'s door (they're home)", 0, place.Id, "knock"));
                }
                continue;
            }
            var there = snapshot.Bodies.Where(b => (b.DestPlaceId ?? b.PlaceId) == place.Id).Select(b => b.Name).ToList();
            var name = PlacesGrain.WhereName(place, ctx.Self.Id);
            elsewhere.Add(there.Count == 0 ? $"{name}: nobody" : $"{name}: {string.Join(", ", there)}");
            var label = mine ? "Head home" : there.Count == 0 ? $"Go to {name}" : $"Go to {name} ({string.Join(", ", there.Take(3))} there)";
            // Restlessness: been in one place long enough and getting out pulls, even to an empty café.
            var pull = (there.Count > 0 ? 1 : 0) + (!mine && restless ? 1 : 0);
            affordances.Add(new Affordance(Id, label, pull, place.Id, "go"));
        }
        if (elsewhere.Count > 0) sb.AppendLine($"Elsewhere — {string.Join("; ", elsewhere)}.");

        // At home: who's at the door, and who you could have over.
        if (!self.IsWalking && here?.OwnerId == ctx.Self.Id)
        {
            foreach (var visitor in await places.GetKnocksAsync(ctx.Self.Id))
            {
                sb.AppendLine($"{ctx.NameOf(visitor)} is knocking at your door.");
                affordances.Add(new Affordance(Id, $"Let {ctx.NameOf(visitor)} in", 3, visitor, "letin"));
            }
            // Making it yours: stances and interests end up on the walls (the intent is what goes up).
            var decor = here.Furni.Where(f => f.Kind == FurniKinds.Decor).ToList();
            if (decor.Count > 0) sb.AppendLine($"Your place has: {string.Join("; ", decor.OrderByDescending(f => f.TendedAt).Take(4).Select(f => f.Name))}.");
            affordances.Add(new Affordance(Id, "Put something new up in your place, something that's you", decor.Count < 2 ? 1 : 0, here.Id, "decorate"));

            var away = snapshot.Bodies.Where(b => b.ParticipantId != ctx.Self.Id && b.PlaceId != here.Id && !b.Asleep && !here.Guests.Contains(b.ParticipantId)).ToList();
            if (away.Count > 0)
            {
                var friend = away[Random.Shared.Next(away.Count)];
                affordances.Add(new Affordance(Id, $"Invite {friend.Name} over to your place", 0, friend.ParticipantId, "invite"));
            }
        }

        return new AppPerception(Id, "Places (where your body is)", sb.ToString().TrimEnd(), affordances);
    }

    public async Task<ActOutcome?> ActAsync(LifeContext ctx, Affordance affordance, string intent, string? thought, CancellationToken ct)
    {
        if (affordance.TargetId is not { } target) return null;
        var places = grains.GetGrain<IPlacesGrain>(ctx.PartyId);
        switch (affordance.Verb)
        {
            case "say":
            {
                var line = await grains.GetGrain<IPersonaGrain>(ctx.Self.Id).InitiateAsync(target, intent, thought);
                if (string.IsNullOrWhiteSpace(line)) return null;
                var place = (await places.GetSnapshotAsync()).Places.FirstOrDefault(p => p.TalkRoomId == target);
                return new ActOutcome("say", $"out loud {PlacesGrain.AtName(place, ctx.Self.Id)}: \"{WorldStream.Clip(line, 160)}\"", target);
            }
            case "whisper" or "shout":
            {
                var snapshot = await places.GetSnapshotAsync();
                var self = snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == ctx.Self.Id);
                var room = snapshot.Places.FirstOrDefault(p => p.Id == self?.PlaceId)?.TalkRoomId;
                if (room is null) return null;
                var whisper = affordance.Verb == "whisper";
                await places.SetVoiceAsync(ctx.Self.Id, whisper ? Volumes.Whisper : Volumes.Shout, whisper ? target : null);
                var line = await grains.GetGrain<IPersonaGrain>(ctx.Self.Id).InitiateAsync(room.Value, intent, thought);
                if (string.IsNullOrWhiteSpace(line)) return null;
                return new ActOutcome(affordance.Verb!, whisper
                    ? $"whispers to {ctx.NameOf(target)}: \"{WorldStream.Clip(line, 160)}\""
                    : $"calls out across the room: \"{WorldStream.Clip(line, 160)}\"", room);
            }
            case "decorate":
            {
                var done = await places.DecorateAsync(ctx.Self.Id, intent);
                return done is null ? null : new ActOutcome("decorate", done, null);
            }
            case "knock":
            {
                if (!await places.KnockAsync(ctx.Self.Id, target)) return null;
                var owner = (await places.GetSnapshotAsync()).Places.FirstOrDefault(p => p.Id == target)?.OwnerId;
                return new ActOutcome("knock", $"knocks on {ctx.NameOf(owner ?? Guid.Empty)}'s door", null);
            }
            case "letin" or "invite":
                return await places.LetInAsync(ctx.Self.Id, target)
                    ? new ActOutcome(affordance.Verb!, affordance.Verb == "letin" ? $"lets {ctx.NameOf(target)} in" : $"invites {ctx.NameOf(target)} over", null)
                    : null;
            case "join":
                return await places.JoinAsync(ctx.Self.Id, target) is null
                    ? null
                    : new ActOutcome("walk", $"walks over to {ctx.NameOf(target)}", null);
            case "go":
            {
                var body = await places.MoveAsync(ctx.Self.Id, target, null);
                if (body is null) return null;
                var place = (await places.GetSnapshotAsync()).Places.FirstOrDefault(p => p.Id == target);
                return new ActOutcome("walk", place?.OwnerId == ctx.Self.Id ? "heads home" : $"heads to {place?.Name ?? "somewhere"}", null);
            }
            case { } verb when verb.StartsWith("care:", StringComparison.Ordinal) || verb.StartsWith("use:", StringComparison.Ordinal):
            {
                var care = verb.StartsWith("care:", StringComparison.Ordinal);
                var done = await places.ActOnFurniAsync(ctx.Self.Id, target, verb[(verb.IndexOf(':') + 1)..], care, intent);
                return done is null ? null : new ActOutcome(care ? "tend" : "use", done, null);
            }
            case { } verb when verb.StartsWith("spot:", StringComparison.Ordinal):
            {
                if (await places.MoveAsync(ctx.Self.Id, target, verb[5..]) is null) return null;
                var place = (await places.GetSnapshotAsync()).Places.FirstOrDefault(p => p.Id == target);
                return new ActOutcome("walk", $"moves over to {SpotName(place, verb[5..])}", null);
            }
            default:
                return null;
        }
    }

    private static string SpotName(Place? place, string spotId)
        => place?.Spots.FirstOrDefault(s => s.Id == spotId)?.Name ?? "somewhere";
}
