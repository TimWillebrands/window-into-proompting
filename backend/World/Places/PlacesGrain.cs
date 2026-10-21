using System.Text;
using Orleans.Concurrency;
using Orleans.Streams;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Streaming;
using PartyTown.Services.SystemOne;

namespace PartyTown.World.Places;

/// <summary>
/// The Places registry and body index of one Party (key = Party id). Single writer of "which Place
/// is Body X in", so one-body-one-place has one owner. Plain persisted state, not event-sourced.
/// </summary>
public sealed class PlacesGrain(
    [PersistentState(stateName: "places", storageName: "parties")]
    IPersistentState<PlacesState> state,
    TimeProvider time,
    ISystemOneJudge judge,
    WorldMemory memory,
    ILogger<PlacesGrain> logger)
    : Grain, IPlacesGrain
{
    private static readonly TimeSpan WithinPlace = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan BetweenPlaces = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ActivityWearsOff = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan BusyWithFurni = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan MishapMin = TimeSpan.FromHours(3);
    private static readonly TimeSpan MishapMax = TimeSpan.FromHours(8);
    private IGrainTimer? _arrivals;
    private readonly Dictionary<Guid, ChatGroupInfo> _rooms = new();
    private readonly Dictionary<Guid, (int Bed, int Wake, long At)> _sleepWindows = new();
    private long _lastSleepCheck;
    private static readonly TimeSpan SleepCheckEvery = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan WindowFresh = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan GroggyFor = TimeSpan.FromMinutes(30);
    private readonly Dictionary<Guid, Voice> _voices = new();
    private readonly List<Overheard> _overheard = new();
    private static readonly TimeSpan VoiceHolds = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan OverheardFades = TimeSpan.FromMinutes(10);

    private Guid PartyId => this.GetPrimaryKey();

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        if (state.State.Places.Count == 0)
        {
            state.State.Places = PlaceSeeds.Public();
            await state.WriteStateAsync(cancellationToken);
        }
        BackfillPrepositions();
        foreach (var place in state.State.Places) EnsureFurni(place);
        foreach (var b in state.State.Bodies.Values.Where(b => b.ArrivedAt == 0))
            b.ArrivedAt = Now() - (long)TimeSpan.FromHours(1).TotalMilliseconds;
        if (state.State.Bodies.Values.Any(b => b.IsWalking)) WatchArrivals();
        await FollowMessagesAsync();
    }

    /// <summary>Every message anyone sends shows on their Body: pipeline replies and humans typing included.</summary>
    private async Task FollowMessagesAsync()
    {
        var stream = this.GetStreamProvider(PartyStreamIds.Provider).GetStream<PartyStreamEvent>(
            PartyStreamIds.PartyEventsNamespace, PartyStreamIds.PartyEventId(PartyId));
        var handles = await stream.GetAllSubscriptionHandles();
        if (handles.Count == 0)
            await stream.SubscribeAsync((e, _) => OnPartyEventAsync(e));
        else
            foreach (var h in handles) await h.ResumeAsync((e, _) => OnPartyEventAsync(e));
    }

    private async Task OnPartyEventAsync(PartyStreamEvent e)
    {
        if (e.Type != "message" || e.Message is not { } m || string.IsNullOrWhiteSpace(m.Content)) return;
        if (!state.State.Bodies.ContainsKey(m.SenderId)) return;
        if (!_rooms.TryGetValue(e.ChatGroupId, out var room))
        {
            foreach (var g in await GrainFactory.GetGrain<IPartyGrain>(PartyId).GetChatGroups()) _rooms[g.Id] = g;
            if (!_rooms.TryGetValue(e.ChatGroupId, out room)) return;
        }
        var kind = room.Kind;
        if (kind == ChatGroupKinds.Direct && state.State.Bodies[m.SenderId].IsHuman)
            foreach (var other in room.MemberIds?.Where(id => id != m.SenderId) ?? Enumerable.Empty<Guid>())
                await WakeGroggyAsync(other);
        if (kind == ChatGroupKinds.Place)
        {
            NoteOverheard(e.ChatGroupId, m);
            return;
        }
        var app = kind switch
        {
            ChatGroupKinds.Direct => "messenger",
            ChatGroupKinds.Room => "rooms",
            _ => null,
        };
        if (app is not null) await NoteActivityAsync(m.SenderId, app);
    }

    public async Task<List<Guid>> GetAsleepAsync()
    {
        await SettleAsync();
        await SleepCycleAsync();
        return state.State.Bodies.Values.Where(b => b.Asleep).Select(b => b.ParticipantId).ToList();
    }

    public Task<bool> IsAsleepAsync(Guid participantId)
        => Task.FromResult(state.State.Bodies.TryGetValue(participantId, out var b) && b.Asleep);

    /// <summary>Night walks Personas home to bed and asleep (Consolidation runs then); morning wakes them. Humans sleep on their own.</summary>
    private async Task SleepCycleAsync()
    {
        var now = Now();
        if (now - _lastSleepCheck < SleepCheckEvery.TotalMilliseconds) return;
        _lastSleepCheck = now;
        var local = time.GetLocalNow();
        var minute = local.Hour * 60 + local.Minute;
        foreach (var body in state.State.Bodies.Values.Where(b => !b.IsHuman).ToList())
        {
            var (bed, wake) = WindowFor(body.ParticipantId, now);
            var night = Sleep.IsNight(minute, bed, wake);
            if (body.Asleep)
            {
                if (!night) await WakeAsync(body, "just got up", TimeSpan.FromMinutes(20));
                continue;
            }
            if (!night || body.WokenUntil > now) continue;
            var home = state.State.Places.FirstOrDefault(p => p.Kind == PlaceKinds.Home && p.OwnerId == body.ParticipantId);
            if (home is null) continue;
            if (!body.IsWalking && body.PlaceId == home.Id && body.SpotId == "bed")
            {
                body.Asleep = true;
                body.Posture = Posture.Lying;
                body.Activity = "asleep";
                body.ActivityUntil = null;
                await state.WriteStateAsync();
                await PublishAsync(body);
                _ = RestAsync(body.ParticipantId);
            }
            else if (body.DestPlaceId != home.Id || body.DestSpotId != "bed")
            {
                await MoveAsync(body.ParticipantId, home.Id, "bed");
            }
        }
    }

    private (int Bed, int Wake) WindowFor(Guid id, long now)
    {
        if (_sleepWindows.TryGetValue(id, out var w) && now - w.At < WindowFresh.TotalMilliseconds) return (w.Bed, w.Wake);
        // Detached: the Self grain may need an LLM call for today's day, and it calls back into us for the Situation.
        _ = RefreshWindowAsync(id);
        return _sleepWindows.TryGetValue(id, out var stale) ? (stale.Bed, stale.Wake) : Sleep.Window(id);
    }

    private async Task RefreshWindowAsync(Guid id)
    {
        try
        {
            var day = await GrainFactory.GetGrain<Self.IPersonaSelfGrain>(WorldKeys.Life(PartyId, id)).GetDayWindowAsync();
            var (bed, wake) = Sleep.Window(id, day.FirstBeatMinute, day.LastBeatMinute);
            _sleepWindows[id] = (bed, wake, Now());
        }
        catch (Exception ex)
        {
            _sleepWindows[id] = (Sleep.Window(id).Bed, Sleep.Window(id).Wake, Now());
            logger.LogDebug(ex, "Day window unavailable for {Persona}; using chronotype", id);
        }
    }

    private async Task WakeAsync(Body body, string how, TimeSpan showFor)
    {
        body.Asleep = false;
        body.Posture = Posture.Sitting;
        body.Activity = how;
        body.ActivityUntil = Now() + (long)showFor.TotalMilliseconds;
        await state.WriteStateAsync();
        await PublishAsync(body);
    }

    /// <summary>A human's message wakes a sleeper; they stay up a while, groggy.</summary>
    private async Task WakeGroggyAsync(Guid participantId)
    {
        if (!state.State.Bodies.TryGetValue(participantId, out var body) || !body.Asleep) return;
        body.WokenUntil = Now() + (long)GroggyFor.TotalMilliseconds;
        await WakeAsync(body, "woken by a message, groggy", GroggyFor);
    }

    private async Task RestAsync(Guid personaId)
    {
        try
        {
            var cast = await GrainFactory.GetGrain<IPartyGrain>(PartyId).GetCastAsync();
            if (cast.FirstOrDefault(c => c.Id == personaId) is { } self)
                await memory.RestAsync(PartyId, self, cast, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Sleep consolidation failed for {Persona}", personaId);
        }
    }

    public Task SetVoiceAsync(Guid participantId, string volume, Guid? to)
    {
        _voices[participantId] = new Voice(volume, to, Now() + (long)VoiceHolds.TotalMilliseconds);
        return Task.CompletedTask;
    }

    public Task<EarshotResult?> EarshotAsync(Guid talkRoomId, Guid speakerId)
    {
        var place = state.State.Places.FirstOrDefault(p => p.TalkRoomId == talkRoomId);
        if (place is null || !state.State.Bodies.TryGetValue(speakerId, out var speaker) || speaker.IsWalking)
            return Task.FromResult<EarshotResult?>(null);
        var voice = _voices.Remove(speakerId, out var v) && v.Until > Now() ? v : new Voice(Volumes.Talk, null, 0);
        var occupants = Occupants(place.Id).ToList();
        return Task.FromResult<EarshotResult?>(new EarshotResult(voice.Volume, Earshot.Hearers(occupants, speaker, voice.Volume, voice.To)));
    }

    public Task<List<Overheard>> GetOverheardAsync(Guid participantId)
    {
        var now = Now();
        _overheard.RemoveAll(o => now - o.At > OverheardFades.TotalMilliseconds);
        var place = state.State.Bodies.TryGetValue(participantId, out var b) && !b.IsWalking ? b.PlaceId : Guid.Empty;
        return Task.FromResult(_overheard.Where(o => o.PlaceId == place && o.Listeners.Contains(participantId)).ToList());
    }

    /// <summary>Those in the Place but out of Earshot notice the talk; System One reads tone and their name, never the words.</summary>
    private void NoteOverheard(Guid talkRoomId, ChatMessage m)
    {
        if (m.HeardBy is not { } heard) return;
        var place = state.State.Places.FirstOrDefault(p => p.TalkRoomId == talkRoomId);
        if (place is null || !state.State.Bodies.TryGetValue(m.SenderId, out var speaker)) return;
        var listeners = Occupants(place.Id).Where(b => !heard.Contains(b.ParticipantId)).ToList();
        if (listeners.Count == 0) return;
        var record = new Overheard
        {
            PlaceId = place.Id, SpeakerId = m.SenderId, SpeakerName = speaker.Name.Split(' ')[0], SpotId = speaker.SpotId,
            Volume = m.Volume ?? Volumes.Talk, At = Now(),
            Listeners = listeners.Select(l => l.ParticipantId).ToList(),
            WithNames = heard.Where(id => id != m.SenderId && state.State.Bodies.ContainsKey(id))
                .Select(id => state.State.Bodies[id].Name.Split(' ')[0]).ToList(),
            NameHeardBy = listeners.Where(l => Mentions(m.Content, l.Name)).Select(l => l.ParticipantId).ToList(),
        };
        _overheard.Add(record);
        if (_overheard.Count > 40) _overheard.RemoveAt(0);
        if (judge.Enabled) _ = ReadToneAsync(record, m.Content ?? string.Empty, listeners);
    }

    private async Task ReadToneAsync(Overheard record, string content, List<Body> listeners)
    {
        try
        {
            var questions = new Dictionary<string, SystemOneQuestion>
            {
                ["tone"] = new ChoiceQuestion("How does this line sound to someone across the room who can't make out the words?",
                    Overhearing.Tones.ToDictionary(t => t, t => (string?)null)),
            };
            for (var i = 0; i < listeners.Count; i++)
                questions[$"name{i}"] = new NoulQuestion($"The speaker says or refers to {listeners[i].Name.Split(' ')[0]} by name.");
            var answers = await judge.AskAsync($"{record.SpeakerName} says: \"{WorldStream.Clip(content, 400)}\"", questions, CancellationToken.None);
            record.Tone = answers.SampleChoice("tone", 0.5) ?? record.Tone;
            record.NameHeardBy = listeners.Where((l, i) => answers.Noul($"name{i}", record.NameHeardBy.Contains(l.ParticipantId) ? 1 : 0) > 0.6)
                .Select(l => l.ParticipantId).ToList();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Tone read failed; keeping the plain fallback");
        }
    }

    private static bool Mentions(string? content, string name)
        => !string.IsNullOrEmpty(content) && content.Contains(name.Split(' ')[0], StringComparison.OrdinalIgnoreCase);

    public async Task<PlacesSnapshot> GetSnapshotAsync()
    {
        await SyncCastAsync();
        await SettleAsync();
        await SleepCycleAsync();
        return new PlacesSnapshot
        {
            Places = state.State.Places.ToList(),
            Bodies = state.State.Bodies.Values.OrderBy(b => b.Name).ToList(),
            Now = Now(),
            Furni = state.State.Places.SelectMany(p => p.Furni.Select(f => View(p, f, Now()))).ToList(),
        };
    }

    public async Task<string?> GetSituationAsync(Guid participantId)
    {
        await SyncCastAsync();
        await SettleAsync();
        return state.State.Bodies.TryGetValue(participantId, out var body) ? Situation(body) : null;
    }

    public async Task<Body?> MoveAsync(Guid participantId, Guid placeId, string? spotId)
    {
        await SyncCastAsync();
        await SettleAsync();
        if (!state.State.Bodies.TryGetValue(participantId, out var body)) return null;
        var place = state.State.Places.FirstOrDefault(p => p.Id == placeId);
        if (place is null || !MayEnter(place, participantId)) return null;
        var spot = PickSpot(place, spotId, participantId);
        if (spot is null) return null;
        if (body.PlaceId == placeId && body.SpotId == spot.Id && !body.IsWalking) return body;

        var leaving = body.IsWalking ? body.DestPlaceId : body.PlaceId;
        var now = Now();
        body.Asleep = false;
        body.DestPlaceId = placeId;
        body.DestSpotId = spot.Id;
        body.DepartedAt = now;
        body.ArriveAt = now + (long)(body.PlaceId == placeId ? WithinPlace : BetweenPlaces).TotalMilliseconds;
        body.Posture = Posture.Walking;
        await state.WriteStateAsync();

        if (leaving is { } left && left != placeId) await SyncTalkRoomAsync(left);
        WatchArrivals();
        await PublishAsync(body);
        return body;
    }

    public async Task<Body?> JoinAsync(Guid participantId, Guid otherId)
    {
        await SyncCastAsync();
        await SettleAsync();
        if (!state.State.Bodies.TryGetValue(otherId, out var other)) return null;
        return await MoveAsync(participantId, other.DestPlaceId ?? other.PlaceId, other.DestSpotId ?? other.SpotId);
    }

    public Task<Guid?> GetPlaceOfAsync(Guid participantId)
        => Task.FromResult(state.State.Bodies.TryGetValue(participantId, out var b) && !b.IsWalking ? b.PlaceId : (Guid?)null);

    public Task<Guid?> GetPlaceOfTalkRoomAsync(Guid talkRoomId)
        => Task.FromResult(state.State.Places.FirstOrDefault(p => p.TalkRoomId == talkRoomId)?.Id);

    public async Task NoteActivityAsync(Guid participantId, string app)
    {
        if (!state.State.Bodies.TryGetValue(participantId, out var body)) return;
        var atHome = Find(body.PlaceId)?.OwnerId == participantId && !body.IsWalking;
        var activity = DeviceUse(app, atHome);
        if (activity is null) return;
        body.Activity = activity;
        body.ActivityUntil = Now() + (long)ActivityWearsOff.TotalMilliseconds;
        await state.WriteStateAsync();
        await PublishAsync(body);
    }

    /// <summary>Online Apps are used on a device, and that shows on the Body. Phone unless at home.</summary>
    internal static string? DeviceUse(string app, bool atHome) => app switch
    {
        "messenger" => "on their phone, texting",
        "rooms" => "on their phone, in a group chat",
        "board" => atHome ? "at their laptop, on the Board" : "on their phone, scrolling the Board",
        "calendar" => "on their phone, checking the calendar",
        "diary" => "writing in their diary",
        _ => null,
    };

    public async Task<string?> ActOnFurniAsync(Guid participantId, Guid placeId, string furniId, bool care, string? intent)
    {
        await SettleAsync();
        if (!state.State.Bodies.TryGetValue(participantId, out var body) || body.IsWalking || body.PlaceId != placeId) return null;
        var place = Find(placeId);
        var furni = place?.Furni.FirstOrDefault(f => f.Id == furniId);
        if (place is null || furni is null) return null;
        var now = Now();
        string? done;
        if (care)
            done = FurniRules.CareFor(furni, now) is null ? null : FurniRules.Tend(furni, now);
        else
            done = FurniRules.Use(furni, place.Furni.FirstOrDefault(f => f.Kind == FurniKinds.Cups), now, body.Name.Split(' ')[0], intent);
        if (done is null) return null;

        // Doing something with it means being next to it.
        if (body.SpotId != furni.SpotId && PickSpot(place, furni.SpotId, participantId)?.Id == furni.SpotId)
        {
            body.SpotId = furni.SpotId;
            body.Posture = place.Spots.FirstOrDefault(s => s.Id == furni.SpotId)?.Seat == true ? Posture.Sitting : Posture.Standing;
        }
        body.Activity = FurniRules.BusyWith(furni, care);
        body.ActivityUntil = now + (long)BusyWithFurni.TotalMilliseconds;
        await state.WriteStateAsync();
        await PublishAsync(body);
        await PublishPlaceAsync(place);
        return $"{done} {AtName(place, Guid.Empty)}";
    }

    /// <summary>Rule-based mishaps: now and then a machine somewhere public breaks, announced as a Happening.</summary>
    private void MaybeMishap(long now)
    {
        if (state.State.NextMishapAt == 0)
        {
            state.State.NextMishapAt = now + NextMishapDelay();
            return;
        }
        if (now < state.State.NextMishapAt) return;
        state.State.NextMishapAt = now + NextMishapDelay();
        var candidates = state.State.Places.Where(p => p.Kind == PlaceKinds.Public)
            .SelectMany(p => p.Furni.Where(FurniRules.Breakable).Select(f => (Place: p, Furni: f))).ToList();
        if (candidates.Count == 0) return;
        var (place, furni) = candidates[Random.Shared.Next(candidates.Count)];
        furni.Broken = true;
        var text = furni.Kind == FurniKinds.Lamp
            ? $"{Capital(furni.Name)} at {place.Name} has started flickering."
            : $"{Capital(furni.Name)} at {place.Name} broke down with a sad hiss.";
        _ = GrainFactory.GetGrain<IWorldClockGrain>(PartyId).AnnounceAsync(text);
        _ = PublishPlaceAsync(place);
    }

    private static FurniView View(Place place, Furni f, long now) => new()
    {
        PlaceId = place.Id, FurniId = f.Id, Kind = f.Kind, Name = f.Name, SpotId = f.SpotId,
        Condition = Math.Round(FurniRules.Condition(f, now), 2),
        Notable = FurniRules.Notable(f, now),
        CareLabel = FurniRules.CareFor(f, now)?.Label,
        UseLabel = FurniRules.UseOf(f, now)?.Label,
        On = FurniRules.IsOn(f, now),
    };

    private static long NextMishapDelay()
        => (long)(MishapMin.TotalMilliseconds + Random.Shared.NextDouble() * (MishapMax - MishapMin).TotalMilliseconds);

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private void EnsureFurni(Place place)
    {
        if (place.Furni.Count == 0) place.Furni = FurniRules.SeedFor(place, Now());
    }

    /// <summary>Spots stored before <see cref="Spot.At"/> existed get it from the seed templates.</summary>
    private void BackfillPrepositions()
    {
        var template = PlaceSeeds.Public().Concat(new[] { PlaceSeeds.Home(Guid.Empty, "x") })
            .SelectMany(p => p.Spots).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().At);
        foreach (var spot in state.State.Places.SelectMany(p => p.Spots))
            if (spot.At == "at" && template.TryGetValue(spot.Id, out var at)) spot.At = at;
    }

    /// <summary>One Body per Participant (Narrator excluded); a Home for each, created on demand.</summary>
    private async Task SyncCastAsync()
    {
        var cast = await GrainFactory.GetGrain<IPartyGrain>(PartyId).GetCastAsync();
        var present = cast.Where(c => c.Id != Narrator.PersonaId).ToList();
        var changed = false;
        foreach (var c in present)
        {
            var home = state.State.Places.FirstOrDefault(p => p.Kind == PlaceKinds.Home && p.OwnerId == c.Id);
            if (home is null)
            {
                home = PlaceSeeds.Home(c.Id, c.Name);
                EnsureFurni(home);
                state.State.Places.Add(home);
                changed = true;
            }
            if (state.State.Bodies.TryGetValue(c.Id, out var body))
            {
                if (body.Name != c.Name || body.IsHuman != (c.DefaultDriver == DriverKind.User))
                {
                    body.Name = c.Name;
                    body.IsHuman = c.DefaultDriver == DriverKind.User;
                    changed = true;
                }
                continue;
            }
            var sofa = home.Spots[0];
            state.State.Bodies[c.Id] = new Body
            {
                ParticipantId = c.Id, Name = c.Name, PlaceId = home.Id, SpotId = sofa.Id,
                Posture = sofa.Seat ? Posture.Sitting : Posture.Standing,
                IsHuman = c.DefaultDriver == DriverKind.User,
                ArrivedAt = Now(),
            };
            changed = true;
        }
        var gone = state.State.Bodies.Keys.Where(id => present.All(c => c.Id != id)).ToList();
        foreach (var id in gone) state.State.Bodies.Remove(id);
        if (!changed && gone.Count == 0) return;
        await state.WriteStateAsync();
        foreach (var placeId in state.State.Places.Where(p => p.TalkRoomId is not null).Select(p => p.Id).ToList())
            await SyncTalkRoomAsync(placeId);
    }

    /// <summary>Bodies whose walk is over arrive: posture from the spot, Place talk cast follows.</summary>
    private async Task SettleAsync()
    {
        var now = Now();
        foreach (var b in state.State.Bodies.Values.Where(b => b.ActivityUntil <= now))
        {
            b.Activity = null;
            b.ActivityUntil = null;
        }
        var mishapDue = state.State.NextMishapAt;
        MaybeMishap(now);
        var arrived = state.State.Bodies.Values.Where(b => b.IsWalking && b.ArriveAt <= now).ToList();
        if (arrived.Count == 0)
        {
            if (mishapDue != state.State.NextMishapAt) await state.WriteStateAsync();
            return;
        }
        foreach (var body in arrived)
        {
            var changedPlace = body.DestPlaceId != body.PlaceId;
            body.PlaceId = body.DestPlaceId!.Value;
            body.SpotId = body.DestSpotId ?? body.SpotId;
            var spot = Find(body.PlaceId)?.Spots.FirstOrDefault(s => s.Id == body.SpotId);
            body.Posture = spot?.Seat == true ? Posture.Sitting : Posture.Standing;
            if (changedPlace) body.ArrivedAt = now;
            body.DestPlaceId = null;
            body.DestSpotId = null;
            body.DepartedAt = null;
            body.ArriveAt = null;
        }
        await state.WriteStateAsync();
        foreach (var placeId in arrived.Select(b => b.PlaceId).Distinct())
            await SyncTalkRoomAsync(placeId);
        foreach (var body in arrived) await PublishAsync(body);
    }

    private void WatchArrivals()
    {
        _arrivals ??= this.RegisterGrainTimer(async () =>
        {
            try { await SettleAsync(); }
            catch (Exception ex) { logger.LogWarning(ex, "Places arrival settle failed"); }
            if (!state.State.Bodies.Values.Any(b => b.IsWalking))
            {
                _arrivals?.Dispose();
                _arrivals = null;
            }
        }, new GrainTimerCreationOptions(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)) { KeepAlive = true });
    }

    /// <summary>The Place talk Room's cast is whoever is there (not on their way out).</summary>
    private async Task SyncTalkRoomAsync(Guid placeId)
    {
        var place = Find(placeId);
        if (place is null) return;
        var occupants = Occupants(placeId).Select(b => b.ParticipantId).ToHashSet();
        if (place.TalkRoomId is null)
        {
            if (occupants.Count == 0) return;
            var room = await GrainFactory.GetGrain<IPartyGrain>(PartyId).CreatePlaceRoomAsync(TalkName(place));
            place.TalkRoomId = room.Id;
            await state.WriteStateAsync();
        }
        var grain = GrainFactory.GetGrain<IChatGroupGrain>(place.TalkRoomId.Value);
        var current = await grain.GetParticipantIdsAsync();
        if (!current.SetEquals(occupants)) await grain.SetParticipantIdsAsync(occupants);
    }

    private IEnumerable<Body> Occupants(Guid placeId)
        => state.State.Bodies.Values.Where(b => b.PlaceId == placeId && (b.DestPlaceId is null || b.DestPlaceId == placeId));

    private static bool MayEnter(Place place, Guid participantId)
        => place.Kind != PlaceKinds.Home || place.OwnerId == participantId;

    private Spot? PickSpot(Place place, string? spotId, Guid participantId)
    {
        int Taken(Spot s) => state.State.Bodies.Values.Count(b => b.ParticipantId != participantId
            && (b.DestPlaceId ?? b.PlaceId) == place.Id && (b.DestSpotId ?? b.SpotId) == s.Id);
        var wanted = place.Spots.FirstOrDefault(s => s.Id == spotId);
        if (wanted is not null && Taken(wanted) < wanted.Capacity) return wanted;
        return place.Spots.Where(s => Taken(s) < s.Capacity).OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
    }

    private string Situation(Body body)
    {
        if (body.IsWalking)
        {
            var dest = Find(body.DestPlaceId!.Value);
            var mins = Math.Max(0, (body.ArriveAt!.Value - Now()) / 60_000.0);
            return body.DestPlaceId == body.PlaceId
                ? $"You're walking over to {SpotName(dest, body.DestSpotId)} at {WhereName(dest, body.ParticipantId)}."
                : $"You're on your way to {WhereName(dest, body.ParticipantId)}, {(mins < 0.5 ? "almost there" : $"about {Math.Ceiling(mins)} min away")}.";
        }
        var place = Find(body.PlaceId);
        if (body.Asleep) return $"You're asleep in bed {AtName(place, body.ParticipantId)}.";
        var sb = new StringBuilder($"You're {AtName(place, body.ParticipantId)}, {PostureAt(place, body)}");
        if (body.Activity is { Length: > 0 } activity) sb.Append($", {Yours(activity)}");
        sb.Append('.');
        var others = Occupants(body.PlaceId).Where(b => b.ParticipantId != body.ParticipantId).ToList();
        if (others.Count == 0)
            sb.Append(" Nobody else is here.");
        else
            sb.Append(" Here: ").Append(string.Join(", ", others.Select(o => o.SpotId == body.SpotId
                ? $"{o.Name} (right next to you{Doing(o)})"
                : $"{o.Name} ({PostureAt(place, o)}{Doing(o)})"))).Append('.');
        var notable = place?.Furni.Select(f => FurniRules.Notable(f, Now())).OfType<string>().Take(2).ToList();
        if (notable is { Count: > 0 }) sb.Append(" Around you: ").Append(string.Join("; ", notable)).Append('.');
        return sb.ToString();
    }

    private static string Yours(string activity) => activity.Replace("their", "your");

    private static string Doing(Body b) => b.Activity is { Length: > 0 } a ? $", {a}" : string.Empty;

    private Place? Find(Guid placeId) => state.State.Places.FirstOrDefault(p => p.Id == placeId);

    private static string SpotName(Place? place, string? spotId)
        => place?.Spots.FirstOrDefault(s => s.Id == spotId)?.Name ?? "somewhere";

    private static string PostureAt(Place? place, Body body)
    {
        var spot = place?.Spots.FirstOrDefault(s => s.Id == body.SpotId);
        var name = spot?.Name ?? "somewhere";
        return body.Posture switch
        {
            Posture.Lying => $"lying {spot?.At ?? "on"} {name}",
            Posture.Sitting => $"sitting {spot?.At ?? "at"} {name}",
            _ when spot?.At == "in" => $"in {name}",
            _ => $"standing by {name}",
        };
    }

    internal static string WhereName(Place? place, Guid viewerId)
        => place is null ? "somewhere" : place.OwnerId == viewerId ? "your place" : place.Name;

    internal static string AtName(Place? place, Guid viewerId)
        => place is null ? "somewhere" : place.OwnerId == viewerId ? "at home" : $"at {place.Name}";

    private static string TalkName(Place place)
        => place.Name.StartsWith("the ", StringComparison.Ordinal) ? place.Name[4..] : place.Name;

    private Task PublishPlaceAsync(Place place)
        => this.GetStreamProvider(PartyStreamIds.Provider).PublishWorldAsync(PartyId, "place", place);

    private Task PublishAsync(Body body)
        => this.GetStreamProvider(Services.Streaming.PartyStreamIds.Provider).PublishWorldAsync(PartyId, "body", body);

    private long Now() => time.GetUtcNow().ToUnixTimeMilliseconds();
}

[Alias("PartyTown.World.Places.IPlacesGrain")]
public interface IPlacesGrain : IGrainWithGuidKey
{
    [Alias("GetSnapshotAsync")] Task<PlacesSnapshot> GetSnapshotAsync();

    /// <summary>One line: where the Participant is and who's around. Null when they have no Body.</summary>
    [Alias("GetSituationAsync")] Task<string?> GetSituationAsync(Guid participantId);

    /// <summary>Start walking to a spot (null = any free one). Null when not allowed or full.</summary>
    [Alias("MoveAsync")] Task<Body?> MoveAsync(Guid participantId, Guid placeId, string? spotId);

    /// <summary>Walk over to another Body's spot.</summary>
    [Alias("JoinAsync")] Task<Body?> JoinAsync(Guid participantId, Guid otherId);

    /// <summary>The Place a Body is settled in; null while walking or without a Body.</summary>
    [Alias("GetPlaceOfAsync")] Task<Guid?> GetPlaceOfAsync(Guid participantId);

    /// <summary>Which Place a Place talk Room belongs to; null for any other Room.</summary>
    [Alias("GetPlaceOfTalkRoomAsync")] Task<Guid?> GetPlaceOfTalkRoomAsync(Guid talkRoomId);

    /// <summary>Use or care for a furni where the Body is. Returns the activity line, null when not possible.</summary>
    [Alias("ActOnFurniAsync")] Task<string?> ActOnFurniAsync(Guid participantId, Guid placeId, string furniId, bool care, string? intent);

    /// <summary>Who's asleep right now (runs the night/morning cycle first). The World clock skips them.</summary>
    [Alias("GetAsleepAsync")] Task<List<Guid>> GetAsleepAsync();

    /// <summary>Read-only; safe to call from grains Places itself calls (Self).</summary>
    [AlwaysInterleave, Alias("IsAsleepAsync")] Task<bool> IsAsleepAsync(Guid participantId);

    /// <summary>The Participant's next utterance goes at this volume (to one addressee for a whisper). Holds 2 min.</summary>
    [Alias("SetVoiceAsync")] Task SetVoiceAsync(Guid participantId, string volume, Guid? to);

    /// <summary>Who hears an utterance in a Place talk Room right now; null when not a Place or no settled Body. Read-only.</summary>
    [AlwaysInterleave, Alias("EarshotAsync")] Task<EarshotResult?> EarshotAsync(Guid talkRoomId, Guid speakerId);

    /// <summary>Talk this Participant noticed but couldn't make out, in the Place they're in.</summary>
    [Alias("GetOverheardAsync")] Task<List<Overheard>> GetOverheardAsync(Guid participantId);

    /// <summary>Someone used an online App; their Body shows the device for a few minutes.</summary>
    [Alias("NoteActivityAsync")] Task NoteActivityAsync(Guid participantId, string app);
}
