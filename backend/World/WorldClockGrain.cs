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
    ILogger<WorldClockGrain> logger)
    : Grain, IWorldClockGrain
{
    private const int MaxActivities = 300;
    private static readonly TimeSpan LifeTimeout = TimeSpan.FromMinutes(3);
    private const string AmbientShape = """{"happening": "<one or two sentences>"}""";

    private readonly Dictionary<Guid, DateTimeOffset> _busy = new();

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
        if (!ClockPolicy.TickDue(state.State.LastTickAt, state.State.TickSeconds, now))
            return;
        state.State.LastTickAt = now.ToUnixTimeMilliseconds();

        await CaptureLullsAsync();
        await StartDueGatheringsAsync(now);
        await StepShiftsAsync(now);
        if (!ClockPolicy.WithinBudget(state.State.ActionTimes, state.State.MaxActionsPerHour, now)) return;
        (var ambientDue, state.State.LastAmbientAt) = ClockPolicy.Ambient(state.State.LastAmbientAt, state.State.AmbientMinutes, now);
        if (ambientDue)
        {
            state.State.ActionTimes.Add(now.ToUnixTimeMilliseconds());
            _ = AmbientHappeningAsync();
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
        var target = personaId ?? await PickPersonaAsync(now);
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

    public async Task NotifyAsync(List<Guid> personaIds)
    {
        if (personaIds.Count == 0) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var id in personaIds) state.State.NotifiedAt[id] = now;
        await state.WriteStateAsync();
    }

    public async Task<WorldActivity> AnnounceAsync(string text)
    {
        var happening = new WorldActivity
        {
            PersonaId = Narrator.PersonaId, PersonaName = Narrator.DisplayName,
            App = "world", Action = "happening", Summary = WorldStream.Clip(text, 600),
        };
        await RecordAsync(happening);

        // Everyone in the World lived through it: one Event, a Recollection each.
        var cast = await GrainFactory.GetGrain<IPartyGrain>(PartyId).GetCastAsync();
        var present = cast.Select(c => new ParticipantView(c.Id, c.Name, c.DefaultDriver)).ToList();
        var line = new MomentLine(Narrator.PersonaId, $"[Happening in the world] {happening.Summary}", happening.Ts);
        _ = memory.CaptureAsync(
            new Moment(PartyId, MomentSources.World, happening.Id, new List<MomentLine> { line }, 0, present),
            CancellationToken.None);
        return happening;
    }

    public async Task<Guid> EnsurePlayerAsync(string? name)
    {
        var party = GrainFactory.GetGrain<IPartyGrain>(PartyId);
        var info = await party.GetParty();
        var humans = info.Participants.Where(p => p.Driver == DriverKind.User && p.Id != Narrator.PersonaId).ToList();
        var wanted = string.IsNullOrWhiteSpace(name) ? null : name.Trim();

        // Named → that human (several Players can share a World); unnamed → the default Player.
        var link = wanted is not null
            ? humans.FirstOrDefault(p => string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase))
            : humans.FirstOrDefault(p => p.Id == state.State.PlayerId) ?? humans.FirstOrDefault();

        if (link is null)
        {
            var id = Guid.NewGuid();
            var display = wanted ?? "You";
            await GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty).AddPersona(
                new Persona(id, display, "A human visiting this world.", "A real person, not a character."));
            link = new PartyParticipant { Id = id, Name = display, Driver = DriverKind.User };
            await party.AddParticipantsAsync(new List<PartyParticipant> { link });
        }

        if (state.State.PlayerId is null || humans.All(h => h.Id != state.State.PlayerId))
        {
            state.State.PlayerId = link.Id;
            await state.WriteStateAsync();
        }
        return link.Id;
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
            logger.LogWarning(ex, "Life turn failed for persona {PersonaId}", personaId);
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

    private async Task<Guid?> PickPersonaAsync(DateTimeOffset now)
    {
        var cast = await GrainFactory.GetGrain<IPartyGrain>(PartyId).GetCastAsync();
        var asleep = (await GrainFactory.GetGrain<Places.IPlacesGrain>(PartyId).GetAsleepAsync()).ToHashSet();
        var candidates = cast
            .Where(c => c.DefaultDriver == DriverKind.LLM && c.Id != Narrator.PersonaId && !IsBusy(c.Id, now) && !asleep.Contains(c.Id))
            .ToList();
        if (candidates.Count == 0) return null;

        var free = await Task.WhenAll(candidates.Select(c => AvailabilityAsync(c.Id)));
        var weighed = candidates.Select((c, i) =>
        {
            long? last = state.State.LastActedAt.TryGetValue(c.Id, out var l) ? l : null;
            long? notified = state.State.NotifiedAt.TryGetValue(c.Id, out var n) ? n : null;
            return new LifeCandidate(c.Id, c.Chattiness, last, free[i], ClockPolicy.Notified(notified, last));
        }).ToList();
        return ClockPolicy.Pick(weighed, ClockPolicy.LifeWeights(weighed, state.State.TickSeconds, cast.Count, now), Random.Shared.NextDouble());
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

    /// <summary>Novelty against echo chambers: the Narrator lets something small happen in the world.</summary>
    private async Task AmbientHappeningAsync()
    {
        try
        {
            var recent = state.State.Activities
                .Where(a => a.Action == "happening").TakeLast(6).Select(a => $"- {a.Summary}");
            var talk = state.State.Activities
                .Where(a => a.Action is "post" or "reply" or "say" or "dm").TakeLast(6).Select(a => $"- {a.PersonaName} {a.Summary}");
            var local = DateTimeOffset.Now;
            var json = await llm.CompleteJsonAsync(Grains.Generation.JobComplexity.General,
                $"You are the narrator of a small online world shared by a handful of people. Let ONE small, concrete thing happen in their surroundings — weather, a local event, a glitch, news, a delivery, a neighbourhood rumour. Everyday scale, no catastrophes, nothing about any person's inner life. It should give people something new to react to, unrelated to what they're already discussing. One or two sentences, present tense.\nOutput ONLY JSON: {AmbientShape}",
                $"It is {local:dddd HH:mm}.\n\nRecent happenings (don't repeat):\n{string.Join("\n", recent)}\n\nWhat people are talking about (steer away from it):\n{string.Join("\n", talk)}",
                CancellationToken.None, 1.0, ["happening"]);
            var text = json?["happening"]?.ToString();
            if (!string.IsNullOrWhiteSpace(text))
                await AnnounceAsync(text);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ambient happening failed");
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
    [Alias("GetStatusAsync")] Task<WorldStatus> GetStatusAsync();
    [Alias("UpdateAsync")] Task<WorldStatus> UpdateAsync(bool? running, int? tickSeconds, int? maxActionsPerHour, int? ambientMinutes = null);
    [Alias("PulseAsync")] Task PulseAsync();
    /// <summary>Force one Life turn now (ignores pause and interval, respects busy).</summary>
    [Alias("TickNowAsync")] Task<bool> TickNowAsync(Guid? personaId, string? nudge);
    [Alias("GetActivitiesAsync")] Task<List<WorldActivity>> GetActivitiesAsync(int limit, Guid? personaId);
    [Alias("RecordAsync")] Task RecordAsync(WorldActivity activity);
    /// <summary>The human's Participant (Driver = User), created on first use.</summary>
    [Alias("EnsurePlayerAsync")] Task<Guid> EnsurePlayerAsync(string? name);
    /// <summary>Author-announced Happening: logged, perceived by every Life, remembered by everyone.</summary>
    [Alias("AnnounceAsync")] Task<WorldActivity> AnnounceAsync(string text);
    /// <summary>Something concerning these Personas landed outside a live chat: they're likelier to get the next turn.</summary>
    [Alias("NotifyAsync")] Task NotifyAsync(List<Guid> personaIds);
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
}
