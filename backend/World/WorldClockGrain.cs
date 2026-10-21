using PartyTown.Services.Memory;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.SystemOne;
using PartyTown.Services.Streaming;

namespace PartyTown.World;

/// <summary>
/// A Party's World clock (ADR 0018). Pulsed by <see cref="WorldPulseService"/>; on each due tick
/// hands the turn to one idle Persona's Life, and closes lulled Room/DM conversations into memory.
/// Owns the activity log and the hourly action budget. Worlds start paused.
/// </summary>
public sealed class WorldClockGrain(
    [PersistentState(stateName: "worldclock", storageName: "parties")]
    IPersistentState<WorldClockState> state,
    WorldMemory memory,
    ISystemOneJudge judge,
    IWorldLlm llm,
    ILogger<WorldClockGrain> logger)
    : Grain, IWorldClockGrain
{
    private const int MaxActivities = 300;
    private static readonly TimeSpan LifeTimeout = TimeSpan.FromMinutes(3);
    private const double MemorableThreshold = 0.35;
    private const string AmbientShape = """{"happening": "<one or two sentences>"}""";

    private readonly Dictionary<Guid, DateTimeOffset> _busy = new();
    private readonly HashSet<Guid> _capturing = new();

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

        await CaptureLullsAsync(now);
        await StartDueGatheringsAsync(now);
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
        var weighed = candidates.Select((c, i) => new LifeCandidate(
            c.Id, c.Chattiness, state.State.LastActedAt.TryGetValue(c.Id, out var last) ? last : null, free[i])).ToList();
        return ClockPolicy.Pick(weighed, ClockPolicy.LifeWeights(weighed, state.State.TickSeconds, cast.Count, now), Random.Shared.NextDouble());
    }

    /// <summary>Automatic Capture: a conversation that went quiet becomes one remembered moment.</summary>
    private async Task CaptureLullsAsync(DateTimeOffset now)
    {
        var threads = await GrainFactory.GetGrain<IPartyGrain>(PartyId).GetChatGroups();
        var moved = false;
        foreach (var thread in threads)
        {
            if (_capturing.Contains(thread.Id)) continue;
            var room = GrainFactory.GetGrain<IChatGroupGrain>(thread.Id);
            var cursor = state.State.CaptureCursors.TryGetValue(thread.Id, out var c) ? c : (int?)null;
            switch (ClockPolicy.Lulled(await room.GetRecentMessagesAsync(14), cursor, now))
            {
                case LullDecision.Seed seed:
                    state.State.CaptureCursors[thread.Id] = seed.Cursor;
                    moved = true;
                    break;
                case LullDecision.Capture capture:
                    state.State.CaptureCursors[thread.Id] = capture.Cursor;
                    moved = true;
                    _capturing.Add(thread.Id);
                    _ = CaptureAsync(thread, capture.Anchor, capture.Context, ClockPolicy.Witnesses(capture.Context, (await room.GetCastAsync()).Views()));
                    break;
            }
        }
        // Only cursor moves are worth a storage write; most ticks change nothing here.
        if (moved) await state.WriteStateAsync();
    }

    private async Task CaptureAsync(ChatGroupInfo thread, ChatMessage anchor, List<ChatMessage> context, List<ParticipantView> present)
    {
        try
        {
            if (judge.Enabled && !await IsMemorableAsync(context, present))
                return;
            var placeId = thread.Kind == ChatGroupKinds.Place
                ? await GrainFactory.GetGrain<Places.IPlacesGrain>(PartyId).GetPlaceOfTalkRoomAsync(thread.Id)
                : null;
            var result = await memory.CaptureAsync(
                Moment.FromMessages(PartyId, MomentSources.OfKind(thread.Kind), thread.Id, context, anchor.MessageId, present, placeId),
                CancellationToken.None);
            if (result is { EventCreated: true, RecollectionsCreated: > 0 })
            {
                var where = thread.Kind switch
                {
                    ChatGroupKinds.Direct => $"the chat {thread.Name}",
                    ChatGroupKinds.Place => $"talk at {thread.Name}",
                    _ => $"#{thread.Name}",
                };
                await RecordAsync(new WorldActivity
                {
                    App = "memory", Action = "capture", PersonaName = "World", TargetId = thread.Id,
                    Summary = $"a moment in {where} became a memory for {result.RecollectionsCreated} people",
                });
            }
        }
        finally
        {
            _capturing.Remove(thread.Id);
        }
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
        var cast = await GrainFactory.GetGrain<IPartyGrain>(PartyId).GetCastAsync();
        var asleep = (await places.GetAsleepAsync()).ToHashSet();
        var going = ClockPolicy.WalkToGathering(g.AttendeeIds, cast, asleep);
        foreach (var id in going)
            await places.MoveAsync(id, placeId, null);

        await RecordAsync(new WorldActivity
        {
            PersonaId = g.HostId, PersonaName = g.HostName, App = "calendar", Action = "start",
            Summary = $"\"{g.Title}\" is starting {g.Where} ({g.AttendeeIds.Count} going)",
        });
        foreach (var id in going.Where(id => !IsBusy(id, now)))
            StartLife(id, $"It's time for \"{g.Title}\" {g.Where}; you're on your way, you said you'd be there.");
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

    /// <summary>Capture as relevance realization: System One judges whether the span is worth a memory at all.</summary>
    private async Task<bool> IsMemorableAsync(List<ChatMessage> context, List<ParticipantView> present)
    {
        var names = present.ToDictionary(p => p.Id, p => p.Name);
        var transcript = string.Join("\n", context.Select(m => $"{names.GetValueOrDefault(m.SenderId, "someone")}: {m.Content}"));
        try
        {
            var answers = await judge.AskAsync(transcript, new Dictionary<string, SystemOneQuestion>
            {
                ["memorable"] = new NoulQuestion("Something happened in this conversation that the people in it would still remember a week from now."),
            }, CancellationToken.None);
            var p = answers.Noul("memorable", 1);
            logger.LogInformation("Capture gate p(memorable)={P:F2}", p);
            return p >= MemorableThreshold;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "System One capture gate failed; capturing anyway");
            return true;
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
    /// <summary>Minutes between Narrator-generated Happenings; 0 = off.</summary>
    [Id(9)] public int AmbientMinutes { get; set; } = 30;
    [Id(10)] public long? LastAmbientAt { get; set; }
}
