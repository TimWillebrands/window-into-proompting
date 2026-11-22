using System.Text;
using PartyTown.Grains;
using PartyTown.World.Apps;

namespace PartyTown.World.Places;

public sealed class PlacesApp(IGrainFactory grains, WorldMemory? memory = null) : IWorldApp
{
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
        var reserves = self.IsWalking || self.Asleep ? Self.Reserves.Unfelt : ctx.Reserves ?? Self.Reserves.Unfelt;
        var hunger = reserves.Hunger;
        var tired = reserves.Tiredness;
        int? purse = null;
        async Task<int> BalanceAsync() => purse ??= (await WalletOf(ctx).GetAsync()).Balance;
        Fare? fare = null;
        string? featured = null;
        if (!self.IsWalking && here is not null && hunger != Self.Hunger.None)
        {
            var fares = Sustenance.Of(here, ctx.Self.Id);
            fare = Sustenance.Best(fares, fares.Any(f => f.Cost > 0) ? await BalanceAsync() : 0);
            var since = hunger >= Self.Hunger.Hungry ? Self.Bio.Since(reserves, ctx.Now.ToLocalTime()) : null;
            var thing = fare is not null && fare.Key.StartsWith(Sustenance.ThingPrefix, StringComparison.Ordinal)
                ? here.Furni.FirstOrDefault(f => f.Id == fare.Key[Sustenance.ThingPrefix.Length..] && Features.Fresh(f, ctx.Self.Id, ctx.Now.ToUnixTimeMilliseconds()))
                : null;
            if (thing is not null)
            {
                featured = thing.Id;
                var label = thing.Do! + (thing.SpotId == self.SpotId ? string.Empty : $", over at {SpotName(here, thing.SpotId)}") + (since is null ? string.Empty : $" ({since})");
                affordances.Add(new Affordance(Id, label, Self.Bio.Pull(hunger), here.Id, $"do:{thing.Id}", Meets: Self.Bio.Food));
            }
            else if (fare is not null)
                affordances.Add(new Affordance(Id, Sustenance.Label(fare, ctx.Now.ToLocalTime(), Guests(fare, here, others, ctx.Self.Id).Select(g => g.Name).ToList(), since),
                    Self.Bio.Pull(hunger), here.Id, $"eat:{fare.Key}", Meets: Self.Bio.Food));
        }
        else if (!self.IsWalking && here is not null && here.LivesIn(ctx.Self.Id)
                 && others.FirstOrDefault(o => o is { IsWalking: false, Asleep: false } && Bearing.Shown(o, ctx.Now.ToUnixTimeMilliseconds()) == Bearing.Famished) is { } famished
                 && Sustenance.Of(here, ctx.Self.Id).FirstOrDefault(f => f.Key == Sustenance.Cook) is { } cooking)
            affordances.Add(new Affordance(Id, $"Make something to eat for {famished.Name.Split(' ')[0]} (they look like they haven't eaten all day)", 2, here.Id, $"eat:{cooking.Key}"));
        if (!self.IsWalking && here?.TalkRoomId is { } talkRoomId && others.Count > 0)
        {
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
                var priority = 2 + (last is not null && ctx.Mentions(last.Content) ? 3 : 0);
                affordances.Add(new Affordance(Id, await TalkLabelAsync(ctx, nearby, last), priority, talkRoomId, "say"));
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
                if (heard.PlaceId != here.Id)
                    affordances.Add(new Affordance(Id, $"Go see what's going on at {heard.PlaceName}", name ? 3 : 1, heard.PlaceId, "go"));
                else if (heard.SpotId != self.SpotId)
                    affordances.Add(new Affordance(Id, $"Drift over to {SpotName(here, heard.SpotId)} to hear what they're saying",
                        name ? 3 : 1, here.Id, $"spot:{heard.SpotId}"));
            }
        }

        if (!self.IsWalking && ctx.Happenings?.FirstOrDefault(h => h is { Near: HappeningNear.Nearby, PlaceId: not null } && HappeningRules.IsFresh(h, ctx.Now)) is { } news
            && snapshot.Places.FirstOrDefault(p => p.Id == news.PlaceId) is { } scene && !scene.IsClosed())
            affordances.Add(new Affordance(Id, $"Go see what's going on {news.Where} ({Self.Hooks.Like(news.Text)})", 1, scene.Id, "go"));

        if (!self.IsWalking)
        {
            if (!JustWalked(ctx))
            {
                if (others.Where(o => o.SpotId != self.SpotId && !o.IsWalking).OrderBy(_ => Random.Shared.Next()).FirstOrDefault() is { } o)
                    affordances.Add(new Affordance(Id, $"Go over to {o.Name} at {SpotName(here, o.SpotId)}{(o.Activity is { Length: > 0 } doing ? $" ({doing})" : string.Empty)}",
                        nearby.Count == 0 ? 2 : 0, o.ParticipantId, "join"));
                if (nearby.Count == 0 && here?.Spots.Where(s => s.Id != self.SpotId).OrderBy(_ => Random.Shared.Next()).FirstOrDefault() is { } spot)
                    affordances.Add(new Affordance(Id, $"Move over to {spot.Name}{PoseHint(spot)}", 0, here.Id, $"spot:{spot.Id}"));
            }
            if (self.Pose is null && here?.Spots.FirstOrDefault(s => s.Id == self.SpotId) is { Poses.Count: > 0 } mine)
            {
                var pose = mine.Poses[Random.Shared.Next(mine.Poses.Count)];
                affordances.Add(new Affordance(Id, $"Get comfortable: {Poses.Describe(pose, mine, null)}", 0, here.Id, $"pose:{pose}"));
            }

            var nowMs = ctx.Now.ToUnixTimeMilliseconds();
            if (here?.Furni.Select(f => (Furni: f, Care: FurniRules.CareFor(f, nowMs)))
                    .Where(x => x.Care is not null)
                    .OrderBy(x => FurniRules.Condition(x.Furni, nowMs))
                    .FirstOrDefault() is { Care: { } care } worst)
                affordances.Add(new Affordance(Id, care.Label, care.Priority, here.Id, $"care:{worst.Furni.Id}"));
            var open = (here?.Furni ?? new()).Where(f => f.Id != featured && Features.Fresh(f, ctx.Self.Id, nowMs)).OrderBy(_ => Random.Shared.Next()).ToList();
            foreach (var (f, close) in open.Where(f => f.SpotId == self.SpotId).Take(1).Select(f => (f, true))
                         .Concat(open.Where(f => f.SpotId != self.SpotId).Take(1).Select(f => (f, false))))
            {
                var mark = Features.Marks(f, ctx.Self.Id, nowMs).FirstOrDefault();
                var label = f.Do! + (close ? string.Empty : $", over at {SpotName(here, f.SpotId)}")
                    + (mark is null ? string.Empty : $" ({mark.ByName.Split(' ')[0]} left: \"{WorldStream.Clip(mark.Text, 70)}\")");
                affordances.Add(new Affordance(Id, label, close ? 1 : 0, here!.Id, $"do:{f.Id}"));
            }
            var rests = Self.Bio.Helps(reserves, ctx.Now.ToLocalTime());
            var perks = rests && !reserves.Perked && !reserves.Ailing && tired == Self.Tiredness.Flagging;
            if (here?.Furni.Where(f => f.Do is null).Select(f => (Furni: f, Use: FurniRules.UseOf(f, nowMs)))
                    .Where(x => x.Use is not null)
                    .OrderByDescending(x => perks && x.Furni.Kind == FurniKinds.Coffee)
                    .ThenBy(_ => Random.Shared.Next())
                    .FirstOrDefault() is { Use: { } use } pick)
            {
                var lifts = perks && pick.Furni.Kind == FurniKinds.Coffee;
                affordances.Add(new Affordance(Id, use.Label, lifts ? Math.Max(use.Priority, Self.Bio.Pull(tired)) : use.Priority, here.Id, $"use:{pick.Furni.Id}",
                    Meets: lifts ? Self.Bio.Rest : null));
            }
            if (rests && !reserves.Napped && here?.LivesIn(ctx.Self.Id) == true && LieSpot(here, self) is { } couch)
                affordances.Add(new Affordance(Id, $"Lie down for a bit {couch.At} {couch.Name}", Self.Bio.Pull(tired), here.Id, "nap", Meets: Self.Bio.Rest));
            var spent = tired == Self.Tiredness.Spent;
            if (!self.Asleep && here?.LivesIn(ctx.Self.Id) == true && (spent || reserves is { Felt: true, Ailing: true }) && Late(ctx.Now.ToLocalTime()))
                affordances.Add(new Affordance(Id, "Turn in early", spent ? Self.Bio.Seated : 3, here.Id, "bed", Meets: Self.Bio.Rest));
        }

        var stayed = self.IsWalking ? TimeSpan.Zero : ctx.Now - DateTimeOffset.FromUnixTimeMilliseconds(self.ArrivedAt);
        var awayHere = !self.IsWalking && here is { Kind: PlaceKinds.Locale };
        var lived = await SelfOf(ctx).GetStateAsync();
        var feltAt = DateTimeOffset.Now;
        var valence = Self.SelfMath.Valence(lived, feltAt);
        var stay = ArenaFit.Restless(ctx.Fit is { } fit && fit.PlaceId == self.PlaceId ? fit.Better : null);
        if (here is not null && !here.LivesIn(ctx.Self.Id)) stay = Self.Belonging.Stay(stay, Self.Belonging.Warmth(lived.Places, here.Id, feltAt));
        var restless = stayed > stay && !awayHere && tired < Self.Tiredness.Flagging;
        if (awayHere) sb.AppendLine($"Heading off now means you're no longer {self.AwayAs ?? "away"}.");
        else if (stayed > TimeSpan.FromMinutes(20)) sb.AppendLine($"You've been {(here?.LivesIn(ctx.Self.Id) == true ? "at home" : "here")} for {WorldStream.Ago(self.ArrivedAt, ctx.Now).Replace(" ago", "")}.");
        var nowMsG = ctx.Now.ToUnixTimeMilliseconds();
        foreach (var g in (await grains.GetGrain<ICalendarGrain>(ctx.PartyId).GetGatheringsAsync())
                     .Where(g => g.PlaceId is not null && g.Status == GatheringStatus.Started && nowMsG - g.StartsAt < 45 * 60_000 && g.AttendeeIds.Contains(ctx.Self.Id)))
        {
            if ((self.DestPlaceId ?? self.PlaceId) == g.PlaceId)
            {
                sb.AppendLine($"\"{g.Title}\" is happening {(self.IsWalking ? "where you're headed" : "here")}; you said you'd come.");
                for (var i = 0; i < affordances.Count; i++)
                    if (affordances[i].Verb == "say") affordances[i] = affordances[i] with { Priority = affordances[i].Priority + 2 };
            }
            else
            {
                affordances.Add(new Affordance(Id, $"Head to {g.RoomName} for \"{g.Title}\" (you said you'd come)", 5, g.PlaceId, "go"));
            }
        }

        var stale = !self.IsWalking && here?.TalkRoomId is { } roomForRead
            && (await grains.GetGrain<Self.IConversationThermostatGrain>(roomForRead).GetReadAsync()).Stale;
        if (stale)
        {
            sb.AppendLine("The talk here keeps going round in circles; it might be time to be somewhere else.");
            for (var i = 0; i < affordances.Count; i++)
                if (affordances[i].Verb == "say") affordances[i] = affordances[i] with { Priority = affordances[i].Priority - 1 };
        }

        var elsewhere = new List<string>();
        var myDistrict = here?.DistrictId ?? snapshot.Places.FirstOrDefault(p => p.Id == self.CameFrom)?.DistrictId ?? Districts.Centre;
        var further = new List<(Place Place, List<string> There)>();
        var peopledOut = !self.IsWalking && reserves.Battery < Self.SelfMath.PeopledOut && others.Any(o => !o.IsWalking && !o.Asleep);
        var runDown = Self.Belonging.RunDown(valence, reserves);
        var well = Self.Belonging.Well(valence, reserves);
        var visited = well ? await places.GetVisitDaysAsync(ctx.Self.Id) : null;
        var unknown = new List<int>();
        foreach (var place in snapshot.Places.Where(p => p.Id != (self.DestPlaceId ?? self.PlaceId)))
        {
            if (place.Kind == PlaceKinds.Locale || place.Dormant || place.Vacated) continue;
            var mine = place.LivesIn(ctx.Self.Id);
            var keyed = place.Keyholders.Contains(ctx.Self.Id);
            if (place.IsClosed() && !place.Hosts().Contains(ctx.Self.Id) && !keyed && !place.Allowed.Contains(ctx.Self.Id))
            {
                var hosts = snapshot.Bodies.Where(b => place.Hosts().Contains(b.ParticipantId)).ToList();
                var owner = hosts.FirstOrDefault(b => b is { IsWalking: false, Asleep: false } && b.PlaceId == place.Id) ?? hosts.FirstOrDefault();
                var ownerName = owner?.Name.Split(' ')[0] ?? "someone";
                if (place.Guests.Contains(ctx.Self.Id))
                {
                    var theirs = place.Kind == PlaceKinds.Home ? $"{ownerName}'s place" : place.Name;
                    sb.AppendLine($"{ownerName} invited you over to {(place.Kind == PlaceKinds.Home ? "their place" : place.Name)}.");
                    affordances.Add(new Affordance(Id, $"Go over to {theirs} (they invited you)", 2, place.Id, "go"));
                }
                else if (owner is { IsWalking: false, Asleep: false } && owner.PlaceId == place.Id && !self.IsWalking)
                {
                    affordances.Add(new Affordance(Id, place.IsWorkplaceDoor()
                        ? $"Knock at {place.Name}, {PlaceAccess.Words(place.Access)} ({ownerName}'s in)"
                        : $"Knock on {ownerName}'s door (they're {(place.Kind == PlaceKinds.Home ? "home" : "in")})", 0, place.Id, "knock"));
                }
                else if (!self.IsWalking && owner is not null && owner.PlaceId != place.Id && await CloseToAsync(ctx, owner.ParticipantId))
                {
                    var door = place.Kind == PlaceKinds.Home ? $"{ownerName}'s place" : place.Name;
                    sb.AppendLine($"{door} is locked; {ownerName} is out.");
                    affordances.Add(new Affordance(Id, $"Go by {door} anyway (it's locked)", 0, place.Id, "knock"));
                }
                continue;
            }
            var there = snapshot.Bodies.Where(b => (b.DestPlaceId ?? b.PlaceId) == place.Id).Select(b => b.Name).ToList();
            if (place.DistrictId is { } district && district != myDistrict && !mine)
            {
                further.Add((place, there));
                continue;
            }
            var name = PlacesGrain.WhereName(place, ctx.Self.Id);
            elsewhere.Add(there.Count == 0 ? $"{name}: nobody" : $"{name}: {string.Join(", ", there)}");
            var fed = fare is null && hunger >= Self.Hunger.Hungry && !self.IsWalking
                ? Sustenance.Best(Sustenance.Of(place, ctx.Self.Id), await BalanceAsync())
                : null;
            var todo = there.Count > 0 || mine || fed is not null ? null
                : place.Furni.Where(f => Features.Fresh(f, ctx.Self.Id, ctx.Now.ToUnixTimeMilliseconds())).OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
            var onBreak = fed is not null && ctx.OnShift;
            var label = mine ? onBreak ? "Go home for your break" : "Head home"
                : there.Count == 0 ? $"{(onBreak ? "Take your break at" : "Go to")} {name}{(todo is null ? string.Empty : $" (to {Features.Lower(todo.Do!)})")}"
                : $"{(onBreak ? "Take your break at" : "Go to")} {name} ({string.Join(", ", there.Take(3))} there)";
            if (keyed) label += " (you have a key)";
            var pull = (there.Count > 0 ? 1 : 0) + (!mine && restless ? 1 : 0) + (stale ? 2 : 0) - (awayHere ? 1 : 0);
            string? meets = null;
            var bodily = false;
            if (fed is not null)
            {
                label += $" ({Sustenance.Why(fed)})";
                pull += Self.Bio.Pull(hunger) - 1;
                meets = Self.Bio.Food;
                bodily = true;
            }
            if (mine && tired == Self.Tiredness.Spent && !self.IsWalking)
            {
                label += " (you're dead on your feet)";
                pull += Self.Bio.Pull(tired) - 1;
                meets ??= Self.Bio.Rest;
                bodily = true;
            }
            else if (mine && reserves is { Felt: true, Ailing: true } && !self.IsWalking)
            {
                label += " (you're not well)";
                pull += 2;
                meets ??= Self.Bio.Rest;
                bodily = true;
            }
            else if (mine && peopledOut)
            {
                label += " (you're peopled out)";
                pull += 1;
                bodily = true;
            }
            var warmth = mine ? 0 : Self.Belonging.Warmth(lived.Places, place.Id, feltAt);
            if (Self.Belonging.IsPutOff(warmth)) pull -= 1;
            else if (!bodily && !mine && Self.Belonging.IsHaunt(warmth))
            {
                label += Self.Belonging.YoursHook;
                if (runDown) pull += 1;
            }
            else if (!bodily && visited is not null && !mine && place.Kind == PlaceKinds.Public && !visited.ContainsKey(place.Id) && !lived.Places.ContainsKey(place.Id))
                unknown.Add(affordances.Count);
            affordances.Add(new Affordance(Id, label, pull, place.Id, "go", Meets: meets));
        }
        if (unknown.Count > 0)
        {
            var i = unknown[Random.Shared.Next(unknown.Count)];
            affordances[i] = affordances[i] with { Label = affordances[i].Label + Self.Belonging.NewHook, Priority = affordances[i].Priority + 1 };
        }
        if (elsewhere.Count > 0) sb.AppendLine($"Elsewhere — {string.Join("; ", elsewhere)}.");
        var (furtherLine, trams) = FurtherAway(Id, further, snapshot.Districts);
        if (furtherLine is not null) sb.AppendLine(furtherLine);
        affordances.AddRange(trams.Select(t => awayHere ? t with { Priority = t.Priority - 1 } : t));
        var offscreen = snapshot.Bodies.Where(b => b.ParticipantId != ctx.Self.Id && b.AwayAs is not null && !b.IsWalking).Select(b => $"{b.Name} ({b.AwayAs})").ToList();
        if (offscreen.Count > 0) sb.AppendLine($"Away, offscreen: {string.Join(", ", offscreen)}.");

        if (!self.IsWalking && here?.Hosts().Contains(ctx.Self.Id) == true)
        {
            foreach (var visitor in await places.GetKnocksAsync(ctx.Self.Id))
            {
                sb.AppendLine(here.IsWorkplaceDoor()
                    ? $"{ctx.NameOf(visitor)} is at the door of {here.Name}, wanting in ({PlaceAccess.Words(here.Access)})."
                    : $"{ctx.NameOf(visitor)} is knocking at your door.");
                affordances.Add(new Affordance(Id, $"Let {ctx.NameOf(visitor)} in", 3, visitor, "letin"));
            }
            if (here.LivesIn(ctx.Self.Id))
            {
                var decor = here.Furni.Where(f => f.Kind == FurniKinds.Decor).ToList();
                if (decor.Count > 0) sb.AppendLine($"Your place has: {string.Join("; ", decor.OrderByDescending(f => f.TendedAt).Take(4).Select(f => f.Name))}.");
                affordances.Add(new Affordance(Id, "Put something new up in your place, something that's you", decor.Count < 2 ? 1 : 0, here.Id, "decorate"));
            }

            var away = snapshot.Bodies.Where(b => b.ParticipantId != ctx.Self.Id && b.PlaceId != here.Id && !b.Asleep && !here.Guests.Contains(b.ParticipantId) && !here.Hosts().Contains(b.ParticipantId)).ToList();
            if (away.Count > 0 && here.Keyed())
            {
                var friend = await WhoToInviteAsync(ctx, away);
                affordances.Add(new Affordance(Id, $"Invite {friend.Name} over to {PlacesGrain.WhereName(here, ctx.Self.Id)}", 0, friend.ParticipantId, "invite"));
            }
        }

        return new AppPerception(Id, "Places (where your body is)", sb.ToString().TrimEnd(), affordances);
    }

    private async Task<string> TalkLabelAsync(LifeContext ctx, List<Body> nearby, PartyTown.Model.ChatMessage? last)
    {
        if (last is { Content.Length: > 0 }) return Self.Hooks.PickUp(ctx.NameOf(last.SenderId), last.Content);
        var who = string.Join(", ", nearby.Take(3).Select(o => o.Name));
        if (HappeningRules.Witnessed(ctx.Happenings, ctx.RecentOwn, Id, "say", ctx.Now) is { } saw) return Self.Hooks.Saw(who, saw.Text);
        var openers = (await grains.GetGrain<Self.IPersonaSelfGrain>(WorldKeys.Life(ctx.PartyId, ctx.Self.Id)).GetStateAsync()).Openers;
        if (Self.Hooks.Pick(openers) is { } story) return Self.Hooks.Tell(who, story);
        var nowMs = ctx.Now.ToUnixTimeMilliseconds();
        if (nearby.FirstOrDefault(o => !o.Asleep && Bearing.Shown(o, nowMs) is not null) is { } worn) return Self.Hooks.AllRight(worn.Name, Bearing.Shown(worn, nowMs)!);
        return nearby.FirstOrDefault(o => o.Activity is { Length: > 0 }) is { } busy
            ? Self.Hooks.Ask(busy.Name, busy.Activity)
            : Self.Hooks.Ask(who, null);
    }

    private async Task<Body> WhoToInviteAsync(LifeContext ctx, List<Body> away)
    {
        var book = await grains.GetGrain<Relations.IRelationsGrain>(ctx.PartyId).GetAsync();
        if (away.FirstOrDefault(b => Relations.RelationRules.Partners(book, ctx.Self.Id, b.ParticipantId)) is { } partner) return partner;
        var closest = await grains.GetGrain<Self.IPersonaTiesGrain>(WorldKeys.Life(ctx.PartyId, ctx.Self.Id)).GetClosestAsync();
        return away.FirstOrDefault(b => b.ParticipantId == closest) ?? away[Random.Shared.Next(away.Count)];
    }

    private async Task<bool> CloseToAsync(LifeContext ctx, Guid other)
    {
        var book = await grains.GetGrain<Relations.IRelationsGrain>(ctx.PartyId).GetAsync();
        var mine = await grains.GetGrain<Self.IPersonaTiesGrain>(WorldKeys.Life(ctx.PartyId, ctx.Self.Id)).GetTieAsync(other);
        var theirs = await grains.GetGrain<Self.IPersonaTiesGrain>(WorldKeys.Life(ctx.PartyId, other)).GetTieAsync(ctx.Self.Id);
        return Relations.KeyRules.Close(book, ctx.Self.Id, other, mine, theirs);
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
                var door = (await places.GetSnapshotAsync()).Places.FirstOrDefault(p => p.Id == target);
                var host = door?.Hosts().FirstOrDefault() ?? Guid.Empty;
                if (await places.KnockAsync(ctx.Self.Id, target)) return new ActOutcome("knock", $"knocks on {ctx.NameOf(host)}'s door", null);
                return door?.IsClosed() == true ? new ActOutcome("knock", $"goes by {door.Name} and finds it locked", null) : null;
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
                var all = (await places.GetSnapshotAsync()).Places;
                var place = all.FirstOrDefault(p => p.Id == target);
                var from = all.FirstOrDefault(p => p.Id == body.PlaceId);
                await SelfOf(ctx).ExertAsync(from is not null && place is not null && Districts.Apart(from, place) ? Self.Bio.Tram : Self.Bio.Walk);
                return new ActOutcome("walk", place?.LivesIn(ctx.Self.Id) == true ? "heads home" : $"heads to {place?.Name ?? "somewhere"}", null);
            }
            case { } verb when verb.StartsWith("eat:", StringComparison.Ordinal):
                return await EatAsync(ctx, places, target, verb[4..]);
            case "nap":
                return await NapAsync(ctx, places, target);
            case "bed":
                return await places.TurnInAsync(ctx.Self.Id) ? new ActOutcome("bed", "turns in early", null) : null;
            case { } verb when verb.StartsWith("do:", StringComparison.Ordinal):
            {
                var done = await Doing.DoAsync(grains, ctx.PartyId, ctx.Self.Id, WorldLlm.Identity(ctx.Self), target, verb[3..], thought ?? intent, null, ctx.Now);
                if (done is null) return null;
                var relief = (await places.GetSnapshotAsync()).Places.FirstOrDefault(p => p.Id == target) is { } at
                    && at.Furni.FirstOrDefault(f => f.Id == verb[3..]) is { } thing && Sustenance.Feeds(at, thing) is { } feed
                    ? Self.Bio.Relief(await SelfOf(ctx).EatAsync(feed.Amount), feed.Amount)
                    : 0.5;
                if (done.Take.Length == 0 && relief > 0.5) await SelfOf(ctx).FeelAtAsync(target, done.Place, relief, relief);
                if (done.Take.Length > 0)
                {
                    var self = grains.GetGrain<Self.IPersonaSelfGrain>(WorldKeys.Life(ctx.PartyId, ctx.Self.Id));
                    await self.AddOpenerAsync($"{done.Take} ({done.Thing}, {done.Place})");
                    var took = Math.Max(0.6, relief);
                    await self.FeelAtAsync(target, done.Place, took, took);
                    if (memory is not null)
                        _ = memory.CaptureAsync(new Services.Memory.Moment(ctx.PartyId, Services.Memory.MomentSources.Places, Guid.NewGuid(),
                            new List<Services.Memory.MomentLine> { new(ctx.Self.Id, $"[{ctx.Self.Name} {done.Summary}]", ctx.Now.ToUnixTimeMilliseconds()) }, 0,
                            new List<PartyTown.Model.ParticipantView> { ctx.ViewOf(ctx.Self.Id) }, target), CancellationToken.None);
                }
                return new ActOutcome("do", done.Summary, null);
            }
            case { } verb when verb.StartsWith("care:", StringComparison.Ordinal) || verb.StartsWith("use:", StringComparison.Ordinal):
            {
                var care = verb.StartsWith("care:", StringComparison.Ordinal);
                var furniId = verb[(verb.IndexOf(':') + 1)..];
                var done = await places.ActOnFurniAsync(ctx.Self.Id, target, furniId, care, intent);
                if (done is not null && !care && (await places.GetSnapshotAsync()).Furni.Any(f => f.PlaceId == target && f.FurniId == furniId && f.Kind == FurniKinds.Coffee))
                    await SelfOf(ctx).PerkUpAsync();
                return done is null ? null : new ActOutcome(care ? "tend" : "use", done, null);
            }
            case { } verb when verb.StartsWith("spot:", StringComparison.Ordinal):
            {
                if (await places.MoveAsync(ctx.Self.Id, target, verb[5..]) is null) return null;
                var place = (await places.GetSnapshotAsync()).Places.FirstOrDefault(p => p.Id == target);
                var spot = place?.Spots.FirstOrDefault(s => s.Id == verb[5..]);
                var pose = spot is null ? null : Poses.FromIntent(intent, Poses.Allowed(spot, company: false));
                var posed = pose is null ? null : await places.SetPoseAsync(ctx.Self.Id, pose, null);
                return new ActOutcome("walk", $"moves over to {SpotName(place, verb[5..])}{(posed is null ? string.Empty : $", {posed}")}", null);
            }
            case { } verb when verb.StartsWith("pose:", StringComparison.Ordinal):
            {
                var posed = await places.SetPoseAsync(ctx.Self.Id, verb[5..], null);
                return posed is null ? null : new ActOutcome("pose", posed, null);
            }
            default:
                return null;
        }
    }

    private async Task<ActOutcome?> EatAsync(LifeContext ctx, IPlacesGrain places, Guid placeId, string key)
    {
        var snapshot = await places.GetSnapshotAsync();
        var place = snapshot.Places.FirstOrDefault(p => p.Id == placeId);
        var self = snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == ctx.Self.Id);
        if (place is null || self is null || self.IsWalking || self.PlaceId != placeId) return null;
        if (Sustenance.Of(place, ctx.Self.Id).FirstOrDefault(f => f.Key == key) is not { } fare) return null;
        var wallet = WalletOf(ctx);
        if (fare.Cost > 0 && (await wallet.GetAsync()).Balance < fare.Cost) return null;
        var local = ctx.Now.ToLocalTime();
        var guests = Guests(fare, place, snapshot.Bodies.Where(b => b.ParticipantId != ctx.Self.Id && b.PlaceId == placeId), ctx.Self.Id);
        var names = guests.Select(g => g.Name).ToList();
        if (fare.Cost > 0 && !await wallet.SpendAsync(fare.Cost, $"{Sustenance.Meal(local)} {place.At} {place.Name}")) return null;
        if (!await places.EatAsync(ctx.Self.Id, placeId, fare.SpotId, Sustenance.Busy(fare, local)))
        {
            if (fare.Cost > 0) await wallet.EarnAsync(fare.Cost, $"{Sustenance.Meal(local)} {place.At} {place.Name}, not had after all");
            return null;
        }
        var me = SelfOf(ctx);
        var before = await me.EatAsync(fare.Feeds);
        var relief = Self.Bio.Relief(before, fare.Feeds);
        await me.FeelAtAsync(place.Id, fare.Cooked && place.LivesIn(ctx.Self.Id) ? "your kitchen" : place.Name, relief, relief);
        var cook = ctx.Self.Name.Split(' ')[0];
        foreach (var guest in guests.Where(g => !g.IsHuman))
        {
            await grains.GetGrain<Self.IPersonaSelfGrain>(WorldKeys.Life(ctx.PartyId, guest.ParticipantId)).EatAsync(fare.Feeds);
            await places.SetActivityAsync(guest.ParticipantId, "eating", Self.Bio.MealTakes);
            await places.EaseAsync(guest.ParticipantId, Bearing.Famished);
            await grains.GetGrain<IWorldClockGrain>(ctx.PartyId).RecordAsync(new WorldActivity
            {
                PersonaId = guest.ParticipantId, PersonaName = guest.Name, App = Id, Action = "eat",
                Summary = $"eats what {cook} made {PlacesGrain.AtName(place, guest.ParticipantId)}", WithPersonaId = ctx.Self.Id,
            });
        }
        var forThem = (ctx.Reserves ?? Self.Reserves.Unfelt).Hunger == Self.Hunger.None;
        return new ActOutcome("eat", Sustenance.Summary(fare, place, ctx.Self.Id, local, names, forThem), null, guests.FirstOrDefault()?.ParticipantId);
    }

    private async Task<ActOutcome?> NapAsync(LifeContext ctx, IPlacesGrain places, Guid placeId)
    {
        var snapshot = await places.GetSnapshotAsync();
        var home = snapshot.Places.FirstOrDefault(p => p.Id == placeId);
        var self = snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == ctx.Self.Id);
        if (home is null || self is null || self.IsWalking || self.PlaceId != placeId || LieSpot(home, self) is not { } couch) return null;
        if (!await SelfOf(ctx).LieDownAsync()) return null;
        if (self.SpotId != couch.Id) await places.MoveAsync(ctx.Self.Id, placeId, couch.Id);
        await places.SetPoseAsync(ctx.Self.Id, Poses.Lie, null);
        await places.SetActivityAsync(ctx.Self.Id, "having a lie-down", Self.Bio.NapTakes);
        await places.EaseAsync(ctx.Self.Id, Bearing.Spent);
        return new ActOutcome("nap", $"lies down for a bit {couch.At} {couch.Name}", null);
    }

    private Self.IPersonaSelfGrain SelfOf(LifeContext ctx) => grains.GetGrain<Self.IPersonaSelfGrain>(WorldKeys.Life(ctx.PartyId, ctx.Self.Id));

    private Economy.IWalletGrain WalletOf(LifeContext ctx) => grains.GetGrain<Economy.IWalletGrain>(WorldKeys.Life(ctx.PartyId, ctx.Self.Id));

    internal static List<Body> Guests(Fare fare, Place place, IEnumerable<Body> others, Guid who)
        => fare.Cooked && place.LivesIn(who) ? others.Where(o => !o.IsWalking && !o.Asleep).ToList() : new List<Body>();

    internal static bool Late(DateTimeOffset local) => local.Hour is >= 20 or < 5;

    internal static Spot? LieSpot(Place home, Body self)
    {
        var lying = home.Spots.Where(s => s.Poses.Contains(Poses.Lie)).ToList();
        return lying.FirstOrDefault(s => s.Id == self.SpotId) ?? lying.FirstOrDefault(s => s.Id != "bed") ?? lying.FirstOrDefault();
    }

    internal static (string? Line, List<Affordance> Go) FurtherAway(string appId, IEnumerable<(Place Place, List<string> There)> further, IReadOnlyList<District> districts)
    {
        var parts = new List<string>();
        var go = new List<Affordance>();
        foreach (var g in further.GroupBy(f => f.Place.DistrictId))
        {
            var name = districts.FirstOrDefault(d => d.Id == g.Key)?.Name ?? g.Key ?? "elsewhere";
            var busy = g.Where(f => f.There.Count > 0).OrderByDescending(f => f.There.Count).ToList();
            var shown = busy.Take(2).Select(f => $"{f.Place.Name}: {string.Join(", ", f.There.Take(3))}").ToList();
            var count = g.Count();
            parts.Add($"{name}: {(shown.Count == 0 ? "nobody" : string.Join(", ", shown))}{(count > shown.Count ? $" ({count} places)" : string.Empty)}");
            var target = busy.FirstOrDefault().Place ?? g.First().Place;
            var who = busy.FirstOrDefault().There is { Count: > 0 } people ? $", {string.Join(", ", people.Take(3))} there" : string.Empty;
            go.Add(new Affordance(appId, $"Take the tram to {name} ({target.Name}{who})", busy.Count > 0 ? 1 : 0, target.Id, "go"));
        }
        return (parts.Count == 0 ? null : $"Further away, a tram ride — {string.Join("; ", parts)}.", go);
    }

    internal static string PoseHint(Spot spot)
        => spot.Poses.Count == 0 ? string.Empty : $" (to {string.Join(" or ", spot.Poses.Take(2).Select(p => p.Replace('-', ' ')))})";

    internal static bool JustWalked(LifeContext ctx)
        => ctx.RecentOwn.Take(2).Any(a => a.App == "places" && a.Action == "walk"
            && ctx.Now - DateTimeOffset.FromUnixTimeMilliseconds(a.Ts) < TimeSpan.FromMinutes(15));

    private static string SpotName(Place? place, string spotId)
        => place?.Spots.FirstOrDefault(s => s.Id == spotId)?.Name ?? "somewhere";
}
