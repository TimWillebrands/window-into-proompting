namespace PartyTown.World;

/// <summary>
/// The Calendar App's store (key = Party id): Gatherings planned in Rooms. When one comes due the
/// World clock pulls its attendees into the Room — Initiative with a future-facing arm.
/// </summary>
public sealed class CalendarGrain(
    [PersistentState(stateName: "calendar", storageName: "parties")]
    IPersistentState<CalendarState> state)
    : Grain, ICalendarGrain
{
    private const int MaxKept = 100;

    public Task<List<Gathering>> GetGatheringsAsync()
        => Task.FromResult(state.State.Gatherings.OrderBy(g => g.StartsAt).ToList());

    public async Task<Gathering> PlanAsync(Guid hostId, string hostName, string title, Guid roomId, string roomName, long startsAt, Guid? placeId = null)
    {
        var gathering = new Gathering
        {
            Id = Guid.NewGuid(),
            HostId = hostId,
            HostName = hostName,
            Title = WorldStream.Clip(title, 120),
            RoomId = roomId,
            RoomName = roomName,
            StartsAt = startsAt,
            AttendeeIds = new List<Guid> { hostId },
            PlaceId = placeId,
        };
        state.State.Gatherings.Add(gathering);
        if (state.State.Gatherings.Count > MaxKept)
            state.State.Gatherings = state.State.Gatherings.OrderByDescending(g => g.StartsAt).Take(MaxKept).ToList();
        await SaveAsync(gathering);
        return gathering;
    }

    public async Task<bool> CancelAsync(Guid gatheringId)
    {
        if (state.State.Gatherings.RemoveAll(g => g.Id == gatheringId) == 0) return false;
        await state.WriteStateAsync();
        return true;
    }

    public async Task<Gathering?> RsvpAsync(Guid gatheringId, Guid personaId, bool going)
    {
        var g = state.State.Gatherings.FirstOrDefault(x => x.Id == gatheringId);
        if (g is null || g.Status != GatheringStatus.Planned) return null;
        if (going && !g.AttendeeIds.Contains(personaId)) g.AttendeeIds.Add(personaId);
        if (!going) g.AttendeeIds.Remove(personaId);
        await SaveAsync(g);
        return g;
    }

    public async Task<List<Gathering>> StartDueAsync(long now)
    {
        var started = new List<Gathering>();
        foreach (var g in state.State.Gatherings)
        {
            var next = GatheringRules.Next(g, now);
            if (next == g.Status) continue;
            g.Status = next;
            if (next == GatheringStatus.Started) started.Add(g);
            await SaveAsync(g);
        }
        return started;
    }

    private async Task SaveAsync(Gathering changed)
    {
        await state.WriteStateAsync();
        await this.GetStreamProvider(Services.Streaming.PartyStreamIds.Provider)
            .PublishWorldAsync(this.GetPrimaryKey(), "calendar", changed);
    }
}

[Alias("PartyTown.World.ICalendarGrain")]
public interface ICalendarGrain : IGrainWithGuidKey
{
    [Alias("GetGatheringsAsync")] Task<List<Gathering>> GetGatheringsAsync();
    [Alias("PlanAsync")] Task<Gathering> PlanAsync(Guid hostId, string hostName, string title, Guid roomId, string roomName, long startsAt, Guid? placeId = null);
    [Alias("RsvpAsync")] Task<Gathering?> RsvpAsync(Guid gatheringId, Guid personaId, bool going);
    /// <summary>Author: it's off, and nobody owes anything for it.</summary>
    [Alias("CancelAsync")] Task<bool> CancelAsync(Guid gatheringId);
    /// <summary>Move every gathering along (start, miss, end) and return the ones that just started.</summary>
    [Alias("StartDueAsync")] Task<List<Gathering>> StartDueAsync(long now);
}

[GenerateSerializer, Alias("PartyTown.World.CalendarState")]
public sealed class CalendarState
{
    [Id(0)] public List<Gathering> Gatherings { get; set; } = new();
}

[GenerateSerializer, Alias("PartyTown.World.GatheringStatus")]
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<GatheringStatus>))]
public enum GatheringStatus
{
    Planned = 0,
    Started = 1,
    /// <summary>It ran its course.</summary>
    Over = 2,
    /// <summary>Its time passed while the World wasn't running; nobody went, nobody owes anything.</summary>
    Missed = 3,
}

/// <summary>Feast time (ADR 0020): a gathering starts on time or not at all, ends, and fits the host's waking hours.</summary>
public static class GatheringRules
{
    public static readonly TimeSpan LateStart = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan Lasts = TimeSpan.FromHours(3);
    private const int Day = 1440;

    public static GatheringStatus Next(Gathering g, long now) => g.Status switch
    {
        GatheringStatus.Planned when g.StartsAt <= now
            => now - g.StartsAt > LateStart.TotalMilliseconds ? GatheringStatus.Missed : GatheringStatus.Started,
        GatheringStatus.Started when now - g.StartsAt > Lasts.TotalMilliseconds => GatheringStatus.Over,
        _ => g.Status,
    };

    /// <summary>Minutes until bedtime from a local minute of day; 0 at night.</summary>
    public static int MinutesToBed(int minute, int bed, int wake)
        => Places.Sleep.IsNight(minute, bed, wake) ? 0 : (bed - minute + Day) % Day;

    /// <summary>Planning wants an evening left: at least 90 minutes before bed.</summary>
    public static bool MayPlan(int minute, int bed, int wake) => MinutesToBed(minute, bed, wake) >= 90;

    /// <summary>Minutes from now it starts: 15–240, and at least an hour before the host's bedtime.</summary>
    public static int StartIn(int minutes, int minuteNow, int bed, int wake)
        => Math.Clamp(minutes, 15, Math.Min(240, Math.Max(15, MinutesToBed(minuteNow, bed, wake) - 60)));
}

[GenerateSerializer, Alias("PartyTown.World.Gathering")]
public sealed class Gathering
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public Guid HostId { get; set; }
    [Id(2)] public string HostName { get; set; } = string.Empty;
    [Id(3)] public string Title { get; set; } = string.Empty;
    [Id(4)] public Guid RoomId { get; set; }
    [Id(5)] public string RoomName { get; set; } = string.Empty;
    [Id(6)] public long StartsAt { get; set; }
    [Id(7)] public List<Guid> AttendeeIds { get; set; } = new();
    [Id(8)] public GatheringStatus Status { get; set; }
    /// <summary>Set when it's at a physical Place (RoomName then holds the Place's name); null for Room gatherings.</summary>
    [Id(9)] public Guid? PlaceId { get; set; }

    /// <summary>"in #kitchen" or "at the Café".</summary>
    public string Where => PlaceId is null ? $"in #{RoomName}" : $"at {RoomName}";
}
