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
        var due = state.State.Gatherings.Where(g => g.Status == GatheringStatus.Planned && g.StartsAt <= now).ToList();
        if (due.Count == 0) return due;
        foreach (var g in due)
        {
            g.Status = GatheringStatus.Started;
            await SaveAsync(g);
        }
        return due;
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
    /// <summary>Flip every due Planned gathering to Started and return them.</summary>
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
