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

        if (!self.IsWalking && here?.TalkRoomId is { } talkRoomId && others.Count > 0)
        {
            var recent = await grains.GetGrain<IChatGroupGrain>(talkRoomId).GetRecentMessagesAsync(4);
            var fresh = recent.Where(m => ctx.Now - DateTimeOffset.FromUnixTimeMilliseconds(m.SendAt ?? 0) < TimeSpan.FromMinutes(30)).ToList();
            if (fresh.Count > 0)
            {
                sb.AppendLine("Said out loud here just now:");
                foreach (var m in fresh)
                    sb.AppendLine($"  {(m.SenderId == ctx.Self.Id ? "you" : ctx.NameOf(m.SenderId))}: {WorldStream.Clip(m.Content, 140)}");
            }
            var last = fresh.LastOrDefault();
            if (last?.SenderId != ctx.Self.Id)
            {
                var who = string.Join(", ", others.Take(3).Select(o => o.Name));
                var priority = 2 + (last is not null && ctx.Mentions(last.Content) ? 2 : 0);
                affordances.Add(new Affordance(Id, $"Say something out loud to {who}, right here", priority, talkRoomId, "say"));
            }
        }

        if (!self.IsWalking)
        {
            foreach (var o in others.Where(o => o.SpotId != self.SpotId).OrderBy(_ => Random.Shared.Next()).Take(2))
                affordances.Add(new Affordance(Id, $"Go over to {o.Name} at {SpotName(here, o.SpotId)}", 1, o.ParticipantId, "join"));
            if (here?.Spots.Where(s => s.Id != self.SpotId).OrderBy(_ => Random.Shared.Next()).FirstOrDefault() is { } spot)
                affordances.Add(new Affordance(Id, $"Move over to {spot.Name}", 0, here.Id, $"spot:{spot.Id}"));
        }

        var stayed = self.IsWalking ? TimeSpan.Zero : ctx.Now - DateTimeOffset.FromUnixTimeMilliseconds(self.ArrivedAt);
        var restless = stayed > Restless;
        if (stayed > TimeSpan.FromMinutes(20)) sb.AppendLine($"You've been {(here?.OwnerId == ctx.Self.Id ? "at home" : "here")} for {WorldStream.Ago(self.ArrivedAt, ctx.Now).Replace(" ago", "")}.");
        var elsewhere = new List<string>();
        foreach (var place in snapshot.Places.Where(p => p.Id != (self.DestPlaceId ?? self.PlaceId)))
        {
            var mine = place.OwnerId == ctx.Self.Id;
            if (place.Kind == PlaceKinds.Home && !mine) continue;
            var there = snapshot.Bodies.Where(b => (b.DestPlaceId ?? b.PlaceId) == place.Id).Select(b => b.Name).ToList();
            var name = PlacesGrain.WhereName(place, ctx.Self.Id);
            elsewhere.Add(there.Count == 0 ? $"{name}: nobody" : $"{name}: {string.Join(", ", there)}");
            var label = mine ? "Head home" : there.Count == 0 ? $"Go to {name}" : $"Go to {name} ({string.Join(", ", there.Take(3))} there)";
            // Restlessness: been in one place long enough and getting out pulls, even to an empty café.
            var pull = (there.Count > 0 ? 1 : 0) + (!mine && restless ? 1 : 0);
            affordances.Add(new Affordance(Id, label, pull, place.Id, "go"));
        }
        if (elsewhere.Count > 0) sb.AppendLine($"Elsewhere — {string.Join("; ", elsewhere)}.");

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
