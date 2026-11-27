using Orleans.Concurrency;
using PartyTown.Services.Memory;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.SystemOne;
using PartyTown.Services.Streaming;

namespace PartyTown.World;

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
    private readonly Queue<int> _kindsLately = new();
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
        if (desks is not null) _ = desks.RefreshAsync(PartyId, now);
        if (sessions is not null) _ = sessions.StepAsync(PartyId, now);
        if (!ClockPolicy.WithinBudget(state.State.ActionTimes, state.State.MaxActionsPerHour, now)) return;
        (var ambientDue, state.State.LastAmbientAt) = ClockPolicy.Ambient(state.State.LastAmbientAt, state.State.AmbientMinutes, now);
        if (ambientDue)
        {
            state.State.ActionTimes.Add(now.ToUnixTimeMilliseconds());
            _ = AmbientHappeningAsync();
        }
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

    public Task<Dictionary<Guid, long>> GetNotifiedAsync()
        => Task.FromResult(state.State.NotifiedAt.ToDictionary(kv => kv.Key, kv => kv.Value));

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

    public async Task<bool> TakePersonaAsync(string clerkUserId, Guid personaId)
    {
        if (state.State.PlayersByClerkId.ContainsKey(clerkUserId)) return false;
        var party = GrainFactory.GetGrain<IPartyGrain>(PartyId);
        var info = await party.GetParty();
        var free = await Takeover.FreeAsync(GrainFactory, info.Participants, state.State.PlayersByClerkId.Values, await party.GetChatGroups());
        if (free.FirstOrDefault(p => p.Id == personaId) is not { } link) return false;

        var humans = info.Participants.Where(p => p.Driver == DriverKind.User && p.Id != Narrator.PersonaId).ToList();
        await party.UpsertParticipantsAsync(new List<PartyParticipant> { link with { Driver = DriverKind.User } });
        state.State.PlayersByClerkId[clerkUserId] = personaId;
        if (state.State.PlayerId is not { } current || (humans.All(h => h.Id != current) && !state.State.PlayersByClerkId.ContainsValue(current))) state.State.PlayerId = personaId;
        await state.WriteStateAsync();
        return true;
    }

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

    private async Task<Guid?> PickPersonaAsync(DateTimeOffset now, bool forced = false)
    {
        var cast = await GrainFactory.GetGrain<IPartyGrain>(PartyId).GetCastAsync();
        var asleep = (await GrainFactory.GetGrain<Places.IPlacesGrain>(PartyId).GetAsleepAsync()).ToHashSet();
        var personas = cast.Where(c => c.DefaultDriver == DriverKind.LLM && c.Id != Narrator.PersonaId).ToList();
        var candidates = personas.Where(c => !IsBusy(c.Id, now) && !asleep.Contains(c.Id)).ToList();
        if (candidates.Count == 0) return null;
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

    private async Task StartDueGatheringsAsync(DateTimeOffset now)
    {
        var due = await GrainFactory.GetGrain<ICalendarGrain>(PartyId).StartDueAsync(now.ToUnixTimeMilliseconds());
        if (due.Count == 0) return;
        foreach (var g in due)
            if (g.PlaceId is { } placeId)
                await StartAtPlaceAsync(g, placeId, now);
        await state.WriteStateAsync();
    }

    private async Task StartAtPlaceAsync(Gathering g, Guid placeId, DateTimeOffset now)
    {
        var places = GrainFactory.GetGrain<Places.IPlacesGrain>(PartyId);
        await places.OpenAsync(placeId, null, "gathering");
        await places.ExpectAsync(placeId, g.HostId, g.AttendeeIds.ToList());
        var cast = await GrainFactory.GetGrain<IPartyGrain>(PartyId).GetCastAsync();
        var asleep = (await places.GetAsleepAsync()).ToHashSet();
        var going = ClockPolicy.WalkToGathering(g.AttendeeIds, cast, asleep);
        foreach (var id in going)
            await places.MoveAsync(id, placeId, null);
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
            var due = Self.OwnHappenings.Due(awake, nowMs);
            var breaks = await Task.WhenAll(due.Select(d => GrainFactory.GetGrain<Self.IPersonaSelfGrain>(WorldKeys.Life(PartyId, d)).BreakingAsync()));
            var breaking = due.Where((_, i) => breaks[i]).ToHashSet();
            if (Self.OwnHappenings.Pick(awake, nowMs, Random.Shared, breaking) is not { } id || cast.FirstOrDefault(c => c.Id == id) is not { } who) return;
            state.State.OwnAt[id] = nowMs;
            var forced = breaking.Contains(id);
            var self = GrainFactory.GetGrain<Self.IPersonaSelfGrain>(WorldKeys.Life(PartyId, id));
            var kind = Self.OwnHappenings.KindFor(forced, _ownKindsLately, Random.Shared, !forced && await self.MendingAsync());
            _ownKindsLately.Enqueue(kind);
            if (_ownKindsLately.Count > Self.OwnHappenings.KindMemory) _ownKindsLately.Dequeue();

            var sheet = await Self.LivedSheets.SourceAsync(GrainFactory, who) ?? $"{who.SystemPrompt}\n{who.Bio}";
            var day = await self.GetStateAsync();
            var json = await llm.For(id).CompleteJsonAsync(Grains.Generation.JobComplexity.General,
                Self.OwnHappenings.Brief(who.Name, Self.OwnHappenings.Kinds[kind]),
                Self.OwnHappenings.Material(who.Name, sheet, day, DateTimeOffset.Now, forced ? Self.Bio.Lately(day) : null),
                CancellationToken.None, 1.0, ["happening", "summary"]);
            if (Self.OwnHappenings.Clean(json?["happening"]?.ToString(), 300) is not { } text) return;
            var summary = Self.OwnHappenings.Clean(json?["summary"]?.ToString(), 160) ?? text;

            await self.HappenAsync(text);
            if (kind == Self.OwnHappenings.Body && await self.AilAsync() is { } strain)
                logger.LogInformation("Ailing {Persona} (strain {Strain:F2})", who.Name, strain);
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
    [Alias("TickNowAsync")] Task<bool> TickNowAsync(Guid? personaId, string? nudge);
    [Alias("GetActivitiesAsync")] Task<List<WorldActivity>> GetActivitiesAsync(int limit, Guid? personaId);
    [Alias("RecordAsync")] Task RecordAsync(WorldActivity activity);
    [AlwaysInterleave, Alias("GetNotifiedAsync")] Task<Dictionary<Guid, long>> GetNotifiedAsync();
    [Alias("FindPlayerAsync")] Task<Guid?> FindPlayerAsync(string clerkUserId);
    [Alias("ClaimPlayerAsync")] Task<Guid?> ClaimPlayerAsync(string clerkUserId, string name);
    [Alias("TakePersonaAsync")] Task<bool> TakePersonaAsync(string clerkUserId, Guid personaId);
    [Alias("IsClaimedAsync")] Task<bool> IsClaimedAsync(Guid participantId);
    [AlwaysInterleave, Alias("GetClaimedAsync")] Task<List<Guid>> GetClaimedAsync();
    [Alias("GetPlayersAsync")] Task<Dictionary<string, Guid>> GetPlayersAsync();
    [Alias("AdoptPlayersAsync")] Task AdoptPlayersAsync(Dictionary<string, Guid> players);
    [Alias("ForgetPlayerAsync")] Task ForgetPlayerAsync(Guid participantId);
    [Alias("AnnounceAsync")] Task<WorldActivity> AnnounceAsync(string text, Guid? placeId = null);
    [Alias("NotifyAsync")] Task NotifyAsync(List<Guid> personaIds);
    [Alias("RemindAsync")] Task RemindAsync(Guid personaId, long? at);
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
    [Id(7)] public Dictionary<Guid, int> CaptureCursors { get; set; } = new();
    [Id(8)] public Guid? PlayerId { get; set; }
    [Id(9)] public int AmbientMinutes { get; set; } = 30;
    [Id(10)] public long? LastAmbientAt { get; set; }
    [Id(11)] public Dictionary<Guid, long> NotifiedAt { get; set; } = new();
    [Id(12)] public Dictionary<string, Guid> PlayersByClerkId { get; set; } = new();
    [Id(13)] public Dictionary<Guid, long> Reminders { get; set; } = new();
    [Id(14)] public Dictionary<Guid, long> OwnAt { get; set; } = new();
    [Id(15)] public long? LastOwnAt { get; set; }
}
