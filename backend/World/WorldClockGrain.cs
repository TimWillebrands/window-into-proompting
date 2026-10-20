using PartyTown.Grains;
using PartyTown.Model;
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
    ILogger<WorldClockGrain> logger)
    : Grain, IWorldClockGrain
{
    private const int MaxActivities = 300;
    private static readonly TimeSpan LifeTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan Lull = TimeSpan.FromSeconds(90);
    private const int MinNewMessagesForCapture = 3;

    private readonly Dictionary<Guid, DateTimeOffset> _busy = new();
    private readonly HashSet<Guid> _capturing = new();

    private Guid PartyId => this.GetPrimaryKey();

    public Task<WorldStatus> GetStatusAsync() => Task.FromResult(Status());

    public async Task<WorldStatus> UpdateAsync(bool? running, int? tickSeconds, int? maxActionsPerHour)
    {
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
        if (state.State.LastTickAt is { } last && now.ToUnixTimeMilliseconds() - last < state.State.TickSeconds * 1000L)
            return;
        state.State.LastTickAt = now.ToUnixTimeMilliseconds();

        await CaptureLullsAsync(now);
        if (!WithinBudget(now)) return;

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
            var participants = info.Participants.ToList();
            participants.Add(new PartyParticipant { Id = id, Name = display, Driver = DriverKind.User });
            await party.SetParticipants(participants);
            link = participants[^1];
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
            activity = new WorldActivity
            {
                PersonaId = personaId, App = "life", Action = "error", Summary = WorldStream.Clip(ex.Message, 160),
            };
        }
        finally
        {
            _busy.Remove(personaId);
        }
        await RecordAsync(activity);
    }

    private async Task<Guid?> PickPersonaAsync(DateTimeOffset now)
    {
        var cast = await GrainFactory.GetGrain<IPartyGrain>(PartyId).GetCastAsync();
        var candidates = cast
            .Where(c => c.DefaultDriver == DriverKind.LLM && c.Id != Narrator.PersonaId && !IsBusy(c.Id, now))
            .ToList();
        if (candidates.Count == 0) return null;

        // Chattiness × time since last act: everyone gets a turn, the restless more often.
        var expectedGap = Math.Max(1, state.State.TickSeconds * cast.Count);
        var weights = candidates.Select(c =>
        {
            var since = state.State.LastActedAt.TryGetValue(c.Id, out var last)
                ? (now.ToUnixTimeMilliseconds() - last) / 1000.0
                : expectedGap * 3.0;
            return (0.25 + c.Chattiness) * Math.Clamp(since / expectedGap, 0.1, 3);
        }).ToList();

        var roll = Random.Shared.NextDouble() * weights.Sum();
        for (var i = 0; i < candidates.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0) return candidates[i].Id;
        }
        return candidates[^1].Id;
    }

    /// <summary>Automatic Capture: a conversation that went quiet becomes one remembered moment.</summary>
    private async Task CaptureLullsAsync(DateTimeOffset now)
    {
        var party = GrainFactory.GetGrain<IPartyGrain>(PartyId);
        var threads = await party.GetChatGroups();
        foreach (var thread in threads)
        {
            if (_capturing.Contains(thread.Id)) continue;
            var room = GrainFactory.GetGrain<IChatGroupGrain>(thread.Id);
            var recent = await room.GetRecentMessagesAsync(14);
            if (recent.Count == 0) continue;
            var last = recent[^1];

            if (!state.State.CaptureCursors.TryGetValue(thread.Id, out var cursor))
            {
                // Pre-world history is not re-lived on first sight.
                state.State.CaptureCursors[thread.Id] = last.MessageId;
                continue;
            }

            var fresh = recent.Where(m => m.MessageId > cursor).ToList();
            if (fresh.Count < MinNewMessagesForCapture) continue;
            if (now - DateTimeOffset.FromUnixTimeMilliseconds(last.SendAt ?? 0) < Lull) continue;

            state.State.CaptureCursors[thread.Id] = last.MessageId;
            var anchor = MostAnswered(fresh);
            var memberIds = await room.GetParticipantIdsAsync();
            var cast = await party.GetCastAsync();
            var present = cast.Where(c => memberIds.Contains(c.Id)).Select(c => new ParticipantView(c.Id, c.Name, c.DefaultDriver)).ToList();
            var context = recent.Where(m => m.MessageId <= anchor.MessageId || fresh.Contains(m)).ToList();
            _capturing.Add(thread.Id);
            _ = CaptureAsync(thread, anchor, context, present);
        }
        await state.WriteStateAsync();
    }

    private async Task CaptureAsync(ChatGroupInfo thread, ChatMessage anchor, List<ChatMessage> context, List<ParticipantView> present)
    {
        try
        {
            var result = await memory.CaptureAsync(PartyId, thread.Id, anchor.MessageId, context, present, CancellationToken.None);
            if (result is { EventCreated: true })
            {
                var where = thread.Kind == ChatGroupKinds.Direct ? $"the chat {thread.Name}" : $"#{thread.Name}";
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

    /// <summary>The line others reacted to most is the moment; ties go to the latest.</summary>
    private static ChatMessage MostAnswered(List<ChatMessage> span)
        => span
            .OrderByDescending(m => span.Count(x => x.TriggeredByMessageId == m.MessageId))
            .ThenByDescending(m => m.MessageId)
            .First();

    private bool IsBusy(Guid personaId, DateTimeOffset now)
        => _busy.TryGetValue(personaId, out var since) && now - since < LifeTimeout;

    private bool WithinBudget(DateTimeOffset now)
    {
        var hourAgo = now.AddHours(-1).ToUnixTimeMilliseconds();
        state.State.ActionTimes.RemoveAll(t => t < hourAgo);
        return state.State.ActionTimes.Count < state.State.MaxActionsPerHour;
    }

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
        };
    }

    private Task PublishAsync(string kind, object payload)
        => this.GetStreamProvider(PartyStreamIds.Provider).PublishWorldAsync(PartyId, kind, payload);
}

[Alias("PartyTown.World.IWorldClockGrain")]
public interface IWorldClockGrain : IGrainWithGuidKey
{
    [Alias("GetStatusAsync")] Task<WorldStatus> GetStatusAsync();
    [Alias("UpdateAsync")] Task<WorldStatus> UpdateAsync(bool? running, int? tickSeconds, int? maxActionsPerHour);
    [Alias("PulseAsync")] Task PulseAsync();
    /// <summary>Force one Life turn now (ignores pause and interval, respects busy).</summary>
    [Alias("TickNowAsync")] Task<bool> TickNowAsync(Guid? personaId, string? nudge);
    [Alias("GetActivitiesAsync")] Task<List<WorldActivity>> GetActivitiesAsync(int limit, Guid? personaId);
    [Alias("RecordAsync")] Task RecordAsync(WorldActivity activity);
    /// <summary>The human's Participant (Driver = User), created on first use.</summary>
    [Alias("EnsurePlayerAsync")] Task<Guid> EnsurePlayerAsync(string? name);
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
}
