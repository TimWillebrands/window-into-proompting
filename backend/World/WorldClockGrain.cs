using Orleans.Concurrency;
using PartyTown.Services.Memory;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.SystemOne;
using PartyTown.Services.Streaming;

namespace PartyTown.World;

/// <summary>
/// A Party's World clock (ADR 0018). Pulsed by <see cref="WorldPulseService"/>; on each due tick
/// hands the turn to one idle Persona's Life and has <see cref="LullCaptureGrain"/> sweep for lulls.
/// Owns the activity log and the hourly action budget. Worlds start paused.
/// </summary>
public sealed class WorldClockGrain(
    [PersistentState(stateName: "worldclock", storageName: "parties")]
    IPersistentState<WorldClockState> state,
    WorldMemory memory,
    IWorldLlm llm,
    Work.Workplaces workplaces,
    Work.Responsibilities responsibilities,
    ILogger<WorldClockGrain> logger,
    Work.Companies.Desks? desks = null,
    Work.Practices.Sessions? sessions = null)
    : Grain, IWorldClockGrain
{
    private const int MaxActivities = 300;
    private static readonly TimeSpan LifeTimeout = TimeSpan.FromMinutes(3);
    private const string AmbientShape = """{"happening": "<one or two sentences>"}""";

    private readonly Dictionary<Guid, DateTimeOffset> _busy = new();
    /// <summary>The kinds of Happening last let loose; per activation, so a deploy may repeat one.</summary>
    private readonly Queue<int> _kindsLately = new();
    /// <summary>Same for own Happenings (ADR 0055).</summary>
    private readonly Queue<int> _ownKindsLately = new();

    private Guid PartyId => this.GetPrimaryKey();

    public Task<WorldStatus> GetStatusAsync() => Task.FromResult(Status());

    public async Task<WorldStatus> UpdateAsync(bool? running, int? tickSeconds, int? maxActionsPerHour, int? ambientMinutes = null)
    {
        if (ambientMinutes is { } am) state.State.AmbientMinutes = Math.Clamp(am, 0, 24 * 60);
        if (running is { } r) state.State.Running = r;
        if (tickSeconds is { } t) state.State.TickSeconds = Math.Clamp(t, 5, 3600);
        if (maxActionsPerHour is { } m) state.State.MaxActionsPerHour = Math.Clamp(m, 0, 2000);
        await state.WriteStateAsync();
        await PublishAsync("clock", Status());
        return Status();
    }

    public async Task PulseAsync()
    {
        if (!state.State.Running) return;
        var now = DateTimeOffset.UtcNow;
        await SurfaceDueAsync(now);
        if (!ClockPolicy.TickDue(state.State.LastTickAt, state.State.TickSeconds, now))
            return;
        state.State.LastTickAt = now.ToUnixTimeMilliseconds();

        await CaptureLullsAsync();
        await StartDueGatheringsAsync(now);
        await StepShiftsAsync(now);
        // Detached: a slow Market or a Session's LLM calls must never hold up the tick.
        if (desks is not null) _ = desks.RefreshAsync(PartyId, now);
        if (sessions is not null) _ = sessions.StepAsync(PartyId, now);
        if (!ClockPolicy.WithinBudget(state.State.ActionTimes, state.State.MaxActionsPerHour, now)) return;
        (var ambientDue, state.State.LastAmbientAt) = ClockPolicy.Ambient(state.State.LastAmbientAt, state.State.AmbientMinutes, now);
        if (ambientDue)
        {
            state.State.ActionTimes.Add(now.ToUnixTimeMilliseconds());
            _ = AmbientHappeningAsync();
        }
        // Life brings its own news (ADR 0055); the one ambient switch turns it off too.
        (var ownDue, state.State.LastOwnAt) = ClockPolicy.Ambient(state.State.LastOwnAt, state.State.AmbientMinutes > 0 ? Self.OwnHappenings.EveryMinutes : 0, now);
        if (ownDue)
        {
            state.State.ActionTimes.Add(now.ToUnixTimeMilliseconds());
            _ = OwnHappeningAsync();
        }

        var personaId = await PickPersonaAsync(now);
        if (personaId is null) return;
        StartLife(personaId.Value, null);
        await state.WriteStateAsync();
        await PublishAsync("clock", Status());
    }

    public async Task<bool> TickNowAsync(Guid? personaId, string? nudge)
    {
        var now = DateTimeOffset.UtcNow;
        var target = personaId ?? await PickPersonaAsync(now, forced: true);
        if (target is null || IsBusy(target.Value, now)) return false;
        StartLife(target.Value, nudge);
        await state.WriteStateAsync();
        await PublishAsync("clock", Status());
        return true;
    }

    public Task<List<WorldActivity>> GetActivitiesAsync(int limit, Guid? personaId)
        => Task.FromResult(state.State.Activities
            .Where(a => personaId is null || a.PersonaId == personaId)
            .OrderByDescending(a => a.Ts)
            .Take(Math.Clamp(limit, 1, MaxActivities))
            .ToList());

    public async Task RecordAsync(WorldActivity activity)
    {
        state.State.Activities.Add(activity);
        if (state.State.Activities.Count > MaxActivities)
            state.State.Activities.RemoveRange(0, state.State.Activities.Count - MaxActivities);
        await state.WriteStateAsync();
        await PublishAsync("activity", activity);
        if (activity.PersonaId != Guid.Empty)
            await GrainFactory.GetGrain<Places.IPlacesGrain>(PartyId).NoteActivityAsync(activity.PersonaId, activity.App);
    }

    public async Task RemindAsync(Guid personaId, long? at)
    {
        if (at is { } due) state.State.Reminders[personaId] = due;
        else if (!state.State.Reminders.Remove(personaId)) return;
        await state.WriteStateAsync();
    }

    public async Task<bool> TrySpendAsync(bool forHuman)
    {
        var now = DateTimeOffset.UtcNow;
        if (!forHuman && !ClockPolicy.WithinBudget(state.State.ActionTimes, state.State.MaxActionsPerHour, now)) return false;
        state.State.ActionTimes.Add(now.ToUnixTimeMilliseconds());
        await state.WriteStateAsync();
        return true;
    }

    /// <summary>Detached: Reflex calls back here, and this grain must never wait on it.</summary>
    private async Task SurfaceDueAsync(DateTimeOffset now)
    {
        var ms = now.ToUnixTimeMilliseconds();
        var due = state.State.Reminders.Where(r => r.Value <= ms).Select(r => r.Key).ToList();
        if (due.Count == 0) return;
        foreach (var id in due)
        {
            state.State.Reminders.Remove(id);
            _ = SurfaceAsync(id);
        }
        await state.WriteStateAsync();
    }

    private async Task SurfaceAsync(Guid personaId)
    {
        try { await GrainFactory.GetGrain<Reflex.IPersonaReflexGrain>(WorldKeys.Life(PartyId, personaId)).SurfaceAsync(); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Surfacing failed for {PersonaId}; trying again shortly", personaId);
            await RemindAsync(personaId, DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds());
        }
    }

    public async Task NotifyAsync(List<Guid> personaIds)
    {
        if (personaIds.Count == 0) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var id in personaIds) state.State.NotifiedAt[id] = now;
        await state.WriteStateAsync();
    }

    public async Task<WorldActivity> AnnounceAsync(string text, Guid? placeId = null)
    {
        var happening = new WorldActivity
        {
            PersonaId = Narrator.PersonaId, PersonaName = Narrator.DisplayName,
            App = "world", Action = "happening", Summary = WorldStream.Clip(text, 600), TargetId = placeId,
        };
        await RecordAsync(happening);

        // At a Place, it's news there and in its District (ADR 0043); anywhere else, everyone lived through it.
        var cast = await GrainFactory.GetGrain<IPartyGrain>(PartyId).GetCastAsync();
        var around = cast.Select(c => c.Id).ToHashSet();
        if (placeId is { } at)
        {
            try
            {
                var places = GrainFactory.GetGrain<Places.IPlacesGrain>(PartyId);
                await places.NoteHappeningAsync(at, happening.Summary);
                var snapshot = await places.GetSnapshotAsync();
                around = HappeningRules.Around(snapshot.Places, snapshot.Bodies, at).ToHashSet();
                var personas = cast.Where(c => c.DefaultDriver == DriverKind.LLM).Select(c => c.Id).ToHashSet();
                await NotifyAsync(HappeningRules.Witnesses(snapshot.Bodies, at).Where(personas.Contains).ToList());
            }
            catch (Exception ex) { logger.LogWarning(ex, "Placing a happening failed"); }
        }
        var present = cast.Where(c => around.Contains(c.Id)).Select(c => new ParticipantView(c.Id, c.Name, c.DefaultDriver)).ToList();
        if (present.Count == 0) return happening;
        var line = new MomentLine(Narrator.PersonaId, $"[Happening in the world] {happening.Summary}", happening.Ts);
        _ = memory.CaptureAsync(
            new Moment(PartyId, MomentSources.World, happening.Id, new List<MomentLine> { line }, 0, present, placeId),
            CancellationToken.None);
        return happening;
    }

    public Task<Guid?> FindPlayerAsync(string clerkUserId)
        => Task.FromResult<Guid?>(state.State.PlayersByClerkId.TryGetValue(clerkUserId, out var id) ? id : null);

    public Task<bool> IsClaimedAsync(Guid participantId)
        => Task.FromResult(state.State.PlayersByClerkId.ContainsValue(participantId));

    public Task<List<Guid>> GetClaimedAsync() => Task.FromResult(state.State.PlayersByClerkId.Values.ToList());

    public Task<Dictionary<string, Guid>> GetPlayersAsync() => Task.FromResult(new Dictionary<string, Guid>(state.State.PlayersByClerkId));

    public async Task AdoptPlayersAsync(Dictionary<string, Guid> players)
    {
        foreach (var (clerkId, id) in players) state.State.PlayersByClerkId.TryAdd(clerkId, id);
        state.State.PlayerId ??= state.State.PlayersByClerkId.Values.Cast<Guid?>().FirstOrDefault();
        await state.WriteStateAsync();
    }

    /// <summary>Makes this Clerk user's character; null when someone in the cast already has that name.</summary>
    public async Task<Guid?> ClaimPlayerAsync(string clerkUserId, string name)
    {
        if (state.State.PlayersByClerkId.TryGetValue(clerkUserId, out var existing)) return existing;

        var party = GrainFactory.GetGrain<IPartyGrain>(PartyId);
        var info = await party.GetParty();
        if (info.Participants.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) return null;

        var humans = info.Participants.Where(p => p.Driver == DriverKind.User && p.Id != Narrator.PersonaId).ToList();
        var id = Guid.NewGuid();
        await GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty).AddPersona(
            new Persona(id, name, PlayerAuto.HumanSheet, "A real person, not a character."));
        await party.AddParticipantsAsync(new List<PartyParticipant> { new() { Id = id, Name = name, Driver = DriverKind.User } });

        state.State.PlayersByClerkId[clerkUserId] = id;
        if (state.State.PlayerId is not { } current || (humans.All(h => h.Id != current) && !state.State.PlayersByClerkId.ContainsValue(current))) state.State.PlayerId = id;
        await state.WriteStateAsync();
        return id;
    }

    /// <summary>Drops a removed Player's ownership and everything the clock kept about them.</summary>
    public async Task ForgetPlayerAsync(Guid participantId)
    {
        foreach (var owner in state.State.PlayersByClerkId.Where(kv => kv.Value == participantId).Select(kv => kv.Key).ToList())
            state.State.PlayersByClerkId.Remove(owner);
        state.State.Activities.RemoveAll(a => a.PersonaId == participantId || a.WithPersonaId == participantId);
        state.State.LastActedAt.Remove(participantId);
        state.State.NotifiedAt.Remove(participantId);
        if (state.State.PlayerId == participantId)
            state.State.PlayerId = state.State.PlayersByClerkId.Values.Cast<Guid?>().FirstOrDefault();
        await state.WriteStateAsync();
    }

    /// <summary>Hands the lull sweep to <see cref="LullCaptureGrain"/>; cursors once kept here move over on first sweep.</summary>
    private async Task CaptureLullsAsync()
    {
        var capture = GrainFactory.GetGrain<ILullCaptureGrain>(PartyId);
        if (state.State.CaptureCursors.Count > 0)
        {
            await capture.AdoptCursorsAsync(state.State.CaptureCursors);
            state.State.CaptureCursors.Clear();
            await state.WriteStateAsync();
        }
        await capture.SweepAsync();
    }

    private void StartLife(Guid personaId, string? nudge)
    {
        var now = DateTimeOffset.UtcNow;
        _busy[personaId] = now;
        state.State.LastActedAt[personaId] = now.ToUnixTimeMilliseconds();
        state.State.ActionTimes.Add(now.ToUnixTimeMilliseconds());

        // Detached: the Life turn can run for a minute; its continuation interleaves back here.
        _ = RunLifeAsync(personaId, nudge);
    }

    private async Task RunLifeAsync(Guid personaId, string? nudge)
    {
        WorldActivity activity;
        try
        {
            activity = await GrainFactory.GetGrain<IPersonaLifeGrain>(WorldKeys.Life(PartyId, personaId)).LiveAsync(nudge);
        }
        catch (Exception ex)
        {
            // Players see this as an error in Activity, so it is one.
            logger.LogError(ex, "Life turn failed for persona {PersonaId}", personaId);
            var info = await GrainFactory.GetGrain<IPartyGrain>(PartyId).GetParty();
            activity = new WorldActivity
            {
                PersonaId = personaId, App = "life", Action = "error", Summary = WorldStream.Clip(ex.Message, 160),
                PersonaName = info.Participants.FirstOrDefault(p => p.Id == personaId)?.Name ?? string.Empty,
            };
        }
        finally
        {
            _busy.Remove(personaId);
        }
        await RecordAsync(activity);
    }

    private async Task<double> AvailabilityAsync(Guid personaId)
    {
        try { return await GrainFactory.GetGrain<Self.IPersonaSelfGrain>(WorldKeys.Life(PartyId, personaId)).GetAvailabilityAsync(); }
        catch { return 1; }
    }

    /// <param name="forced">A turn asked for by hand always lands on someone.</param>
    private async Task<Guid?> PickPersonaAsync(DateTimeOffset now, bool forced = false)
    {
        var cast = await GrainFactory.GetGrain<IPartyGrain>(PartyId).GetCastAsync();
        var asleep = (await GrainFactory.GetGrain<Places.IPlacesGrain>(PartyId).GetAsleepAsync()).ToHashSet();
        var personas = cast.Where(c => c.DefaultDriver == DriverKind.LLM && c.Id != Narrator.PersonaId).ToList();
        var candidates = personas.Where(c => !IsBusy(c.Id, now) && !asleep.Contains(c.Id)).ToList();
        if (candidates.Count == 0) return null;
        // Tempo is your own (ADR 0050): the sleepers keep their share of the ticks, so the one still up isn't handed everyone's turns.
        var resting = forced ? 0 : ClockPolicy.Resting(personas.Where(c => asleep.Contains(c.Id)).Select(c => c.Chattiness));

        var free = await Task.WhenAll(candidates.Select(c => AvailabilityAsync(c.Id)));
        var weighed = candidates.Select((c, i) =>
        {
            long? last = state.State.LastActedAt.TryGetValue(c.Id, out var l) ? l : null;
            long? notified = state.State.NotifiedAt.TryGetValue(c.Id, out var n) ? n : null;
            return new LifeCandidate(c.Id, c.Chattiness, last, free[i], ClockPolicy.Notified(notified, last));
        }).ToList();
        return ClockPolicy.Pick(weighed, ClockPolicy.LifeWeights(weighed, state.State.TickSeconds, cast.Count, now), Random.Shared.NextDouble(), resting);
    }

    /// <summary>A planned Gathering came due: attendees join its Room and get a Life turn pointed at it.</summary>
    private async Task StartDueGatheringsAsync(DateTimeOffset now)
    {
        var due = await GrainFactory.GetGrain<ICalendarGrain>(PartyId).StartDueAsync(now.ToUnixTimeMilliseconds());
        if (due.Count == 0) return;
        foreach (var g in due)
        {
            if (g.PlaceId is { } placeId)
            {
                await StartAtPlaceAsync(g, placeId, now);
                continue;
            }
            var room = GrainFactory.GetGrain<IChatGroupGrain>(g.RoomId);
            var members = await room.JoinAsync(g.AttendeeIds.ToList());
            var attendees = g.AttendeeIds.Where(members.Contains).ToList();
            var roomCast = await room.GetCastAsync();

            await RecordAsync(new WorldActivity
            {
                PersonaId = g.HostId, PersonaName = g.HostName, App = "calendar", Action = "start", TargetId = g.RoomId,
                Summary = $"\"{g.Title}\" is starting in #{g.RoomName} ({attendees.Count} going)",
            });

            foreach (var id in attendees.Where(id => roomCast.EffectiveOf(id) == DriverKind.LLM && !IsBusy(id, now)))
                StartLife(id, $"It's time for \"{g.Title}\" in #{g.RoomName} — you said you'd be there.");
        }
        await state.WriteStateAsync();
    }

    /// <summary>A Gathering at a Place: LLM attendees who are awake walk there and get a Life turn; humans come themselves.</summary>
    private async Task StartAtPlaceAsync(Gathering g, Guid placeId, DateTimeOffset now)
    {
        var places = GrainFactory.GetGrain<Places.IPlacesGrain>(PartyId);
        // Planned somewhere offscreen: the get-together is what puts it on the map (ADR 0027).
        await places.OpenAsync(placeId, null, "gathering");
        await places.ExpectAsync(placeId, g.HostId, g.AttendeeIds.ToList());
        var cast = await GrainFactory.GetGrain<IPartyGrain>(PartyId).GetCastAsync();
        var asleep = (await places.GetAsleepAsync()).ToHashSet();
        var going = ClockPolicy.WalkToGathering(g.AttendeeIds, cast, asleep);
        foreach (var id in going)
            await places.MoveAsync(id, placeId, null);
        // Humans come themselves; only personas who could have come and didn't count as no-shows.
        var personas = g.AttendeeIds.Where(id => cast.FirstOrDefault(c => c.Id == id)?.DefaultDriver == DriverKind.LLM).ToHashSet();
        await responsibilities.KeptAsync(PartyId, new Gathering { HostId = g.HostId, AttendeeIds = g.AttendeeIds.Where(personas.Contains).ToList() }, going);

        await RecordAsync(new WorldActivity
        {
            PersonaId = g.HostId, PersonaName = g.HostName, App = "calendar", Action = "start",
            Summary = $"\"{g.Title}\" is starting {g.Where} ({g.AttendeeIds.Count} going)",
        });
        foreach (var id in going.Where(id => !IsBusy(id, now)))
            StartLife(id, $"It's time for \"{g.Title}\" {g.Where}; you're on your way, you said you'd be there.");
    }

    /// <summary>Jobs happen in time (ADR 0026): shifts start, end or get missed whether or not anyone picks them.</summary>
    private async Task StepShiftsAsync(DateTimeOffset now)
    {
        try
        {
            var cast = await GrainFactory.GetGrain<IPartyGrain>(PartyId).GetCastAsync();
            var asleep = (await GrainFactory.GetGrain<Places.IPlacesGrain>(PartyId).GetAsleepAsync()).ToHashSet();
            var local = now.ToLocalTime();
            foreach (var c in cast.Where(c => c.DefaultDriver == DriverKind.LLM && c.Id != Narrator.PersonaId))
            {
                var change = await GrainFactory.GetGrain<Work.IOccupationGrain>(WorldKeys.Life(PartyId, c.Id)).StepAsync(local, asleep.Contains(c.Id));
                if (change.Step == Work.ShiftStep.None) continue;
                if (await workplaces.ApplyAsync(PartyId, c, change) is { } activity) await RecordAsync(activity);
                await responsibilities.WeighShiftAsync(PartyId, c.Id, change);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Shift step failed");
        }
    }

    /// <summary>Novelty against echo chambers: the Narrator lets something small happen at a real Place, likelier where people are.</summary>
    private async Task AmbientHappeningAsync()
    {
        try
        {
            var recent = state.State.Activities
                .Where(a => a.Action == "happening").TakeLast(6).Select(a => $"- {a.Summary}");
            var talk = state.State.Activities
                .Where(a => a.Action is "post" or "reply" or "say" or "dm").TakeLast(10).Select(a => $"- {a.PersonaName} {a.Summary}").ToList();
            var snapshot = await GrainFactory.GetGrain<Places.IPlacesGrain>(PartyId).GetSnapshotAsync();
            if (!HappeningRules.AnyoneUp(snapshot.Bodies)) return;
            var place = HappeningRules.PickPlace(snapshot.Places, snapshot.Bodies, Random.Shared);
            var kind = HappeningRules.PickKind(_kindsLately, Random.Shared, talk: talk.Count > 0);
            _kindsLately.Enqueue(kind);
            if (_kindsLately.Count > HappeningRules.KindMemory) _kindsLately.Dequeue();
            var worn = kind == HappeningRules.Talk ? (await GrainFactory.GetGrain<Self.IWorldVoiceGrain>(PartyId).GetReportAsync()).WornWords : new List<string>();
            var seen = HappeningRules.Seen(place is null ? 0 : HappeningRules.Witnesses(snapshot.Bodies, place.Id).Count);
            var local = DateTimeOffset.Now;
            var json = await llm.CompleteJsonAsync(Grains.Generation.JobComplexity.General,
                HappeningRules.Brief(HappeningRules.Setting(place, snapshot.Districts), HappeningRules.Kinds[kind], seen, AmbientShape),
                $"It is {local:dddd HH:mm}: {HappeningRules.DayPart(local)}.\n\nAlready happened lately (not these again, nor the same things in them):\n{string.Join("\n", recent)}\n\n{HappeningRules.Talking(kind, worn, kind == HappeningRules.Talk ? talk : talk.TakeLast(6))}",
                CancellationToken.None, 1.0, ["happening"]);
            var text = json?["happening"]?.ToString();
            if (!string.IsNullOrWhiteSpace(text))
                await AnnounceAsync(text, place?.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ambient happening failed");
        }
    }

    /// <summary>Something happens in one awake Persona's own life (ADR 0055): theirs alone, remembered by them.</summary>
    private async Task OwnHappeningAsync()
    {
        try
        {
            var snapshot = await GrainFactory.GetGrain<Places.IPlacesGrain>(PartyId).GetSnapshotAsync();
            var cast = await GrainFactory.GetGrain<IPartyGrain>(PartyId).GetCastAsync();
            var claimed = state.State.PlayersByClerkId.Values.ToHashSet();
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var awake = cast
                .Where(c => c.DefaultDriver == DriverKind.LLM && c.Id != Narrator.PersonaId && !claimed.Contains(c.Id)
                    && snapshot.Bodies.Any(b => b.ParticipantId == c.Id && !b.Asleep && b.Activity != Places.PlacesGrain.Dozing))
                .Select(c => (c.Id, state.State.OwnAt.GetValueOrDefault(c.Id)))
                .ToList();
            if (Self.OwnHappenings.Pick(awake, nowMs, Random.Shared) is not { } id || cast.FirstOrDefault(c => c.Id == id) is not { } who) return;
            state.State.OwnAt[id] = nowMs;
            var kind = Self.OwnHappenings.PickKind(_ownKindsLately, Random.Shared);
            _ownKindsLately.Enqueue(kind);
            if (_ownKindsLately.Count > Self.OwnHappenings.KindMemory) _ownKindsLately.Dequeue();

            var self = GrainFactory.GetGrain<Self.IPersonaSelfGrain>(WorldKeys.Life(PartyId, id));
            var sheet = await Self.LivedSheets.SourceAsync(GrainFactory, who) ?? $"{who.SystemPrompt}\n{who.Bio}";
            var json = await llm.For(id).CompleteJsonAsync(Grains.Generation.JobComplexity.General,
                Self.OwnHappenings.Brief(who.Name, Self.OwnHappenings.Kinds[kind]),
                Self.OwnHappenings.Material(who.Name, sheet, await self.GetStateAsync(), DateTimeOffset.Now),
                CancellationToken.None, 1.0, ["happening", "summary"]);
            if (Self.OwnHappenings.Clean(json?["happening"]?.ToString(), 300) is not { } text) return;
            var summary = Self.OwnHappenings.Clean(json?["summary"]?.ToString(), 160) ?? text;

            await self.HappenAsync(text);
            var activity = new WorldActivity { PersonaId = id, PersonaName = who.Name, App = "life", Action = Self.OwnHappenings.Action, Summary = summary };
            await RecordAsync(activity);
            _ = memory.CaptureAsync(new Moment(PartyId, MomentSources.World, activity.Id,
                new List<MomentLine> { new(Narrator.PersonaId, $"[Happened to {who.Name}] {who.Name} {summary}", activity.Ts) }, 0,
                new List<ParticipantView> { new(who.Id, who.Name, who.DefaultDriver) }), CancellationToken.None);
            await NotifyAsync(new List<Guid> { id });
            logger.LogInformation("Own happening for {Persona} ({Kind}): {Text}", who.Name, kind, text);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Own happening failed");
        }
    }

    private bool IsBusy(Guid personaId, DateTimeOffset now)
        => _busy.TryGetValue(personaId, out var since) && now - since < LifeTimeout;

    private WorldStatus Status()
    {
        var hourAgo = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();
        return new WorldStatus
        {
            PartyId = PartyId,
            Running = state.State.Running,
            TickSeconds = state.State.TickSeconds,
            MaxActionsPerHour = state.State.MaxActionsPerHour,
            ActionsLastHour = state.State.ActionTimes.Count(t => t >= hourAgo),
            PlayerId = state.State.PlayerId,
            BusyPersonaIds = _busy.Keys.ToList(),
            LastTickAt = state.State.LastTickAt,
            AmbientMinutes = state.State.AmbientMinutes,
        };
    }

    private Task PublishAsync(string kind, object payload)
        => this.GetStreamProvider(PartyStreamIds.Provider).PublishWorldAsync(PartyId, kind, payload);
}

[Alias("PartyTown.World.IWorldClockGrain")]
public interface IWorldClockGrain : IGrainWithGuidKey
{
    [AlwaysInterleave, Alias("GetStatusAsync")] Task<WorldStatus> GetStatusAsync();
    [Alias("UpdateAsync")] Task<WorldStatus> UpdateAsync(bool? running, int? tickSeconds, int? maxActionsPerHour, int? ambientMinutes = null);
    [Alias("PulseAsync")] Task PulseAsync();
    /// <summary>Force one Life turn now (ignores pause and interval, respects busy).</summary>
    [Alias("TickNowAsync")] Task<bool> TickNowAsync(Guid? personaId, string? nudge);
    [Alias("GetActivitiesAsync")] Task<List<WorldActivity>> GetActivitiesAsync(int limit, Guid? personaId);
    [Alias("RecordAsync")] Task RecordAsync(WorldActivity activity);
    /// <summary>The human's Participant (Driver = User), created on first use.</summary>
    [Alias("FindPlayerAsync")] Task<Guid?> FindPlayerAsync(string clerkUserId);
    [Alias("ClaimPlayerAsync")] Task<Guid?> ClaimPlayerAsync(string clerkUserId, string name);
    [Alias("IsClaimedAsync")] Task<bool> IsClaimedAsync(Guid participantId);
    /// <summary>Every Participant a human owns, on auto or not.</summary>
    [AlwaysInterleave, Alias("GetClaimedAsync")] Task<List<Guid>> GetClaimedAsync();
    /// <summary>Clerk user id to Participant, for every human here.</summary>
    [Alias("GetPlayersAsync")] Task<Dictionary<string, Guid>> GetPlayersAsync();
    /// <summary>Humans moving in from another World keep their Participants; a Clerk id already here keeps its own.</summary>
    [Alias("AdoptPlayersAsync")] Task AdoptPlayersAsync(Dictionary<string, Guid> players);
    [Alias("ForgetPlayerAsync")] Task ForgetPlayerAsync(Guid participantId);
    /// <summary>A Happening: logged and perceived by every Life; at a Place, news there and remembered around it, else by everyone.</summary>
    [Alias("AnnounceAsync")] Task<WorldActivity> AnnounceAsync(string text, Guid? placeId = null);
    /// <summary>Something concerning these Personas landed outside a live chat: they're likelier to get the next turn.</summary>
    [Alias("NotifyAsync")] Task NotifyAsync(List<Guid> personaIds);
    /// <summary>When to look at what this Persona put off; null clears it (ADR 0031).</summary>
    [Alias("RemindAsync")] Task RemindAsync(Guid personaId, long? at);
    /// <summary>Spend one action on something Reflex says out loud; a human is never made to wait on the budget.</summary>
    [Alias("TrySpendAsync")] Task<bool> TrySpendAsync(bool forHuman);
}

[GenerateSerializer, Alias("PartyTown.World.WorldClockState")]
public sealed class WorldClockState
{
    [Id(0)] public bool Running { get; set; }
    [Id(1)] public int TickSeconds { get; set; } = 45;
    [Id(2)] public int MaxActionsPerHour { get; set; } = 40;
    [Id(3)] public long? LastTickAt { get; set; }
    [Id(4)] public List<long> ActionTimes { get; set; } = new();
    [Id(5)] public List<WorldActivity> Activities { get; set; } = new();
    [Id(6)] public Dictionary<Guid, long> LastActedAt { get; set; } = new();
    /// <summary>Legacy: lull cursors now live in <see cref="LullCaptureGrain"/>; drained on first pulse.</summary>
    [Id(7)] public Dictionary<Guid, int> CaptureCursors { get; set; } = new();
    [Id(8)] public Guid? PlayerId { get; set; }
    /// <summary>Minutes between Narrator-generated Happenings; 0 = off.</summary>
    [Id(9)] public int AmbientMinutes { get; set; } = 30;
    [Id(10)] public long? LastAmbientAt { get; set; }
    [Id(11)] public Dictionary<Guid, long> NotifiedAt { get; set; } = new();
    /// <summary>Clerk user id → their Player. Players not listed here are unowned.</summary>
    [Id(12)] public Dictionary<string, Guid> PlayersByClerkId { get; set; } = new();
    /// <summary>Who has something coming back to mind, and when.</summary>
    [Id(13)] public Dictionary<Guid, long> Reminders { get; set; } = new();
    /// <summary>When each Persona's own life last brought them something (ADR 0055).</summary>
    [Id(14)] public Dictionary<Guid, long> OwnAt { get; set; } = new();
    [Id(15)] public long? LastOwnAt { get; set; }
}
