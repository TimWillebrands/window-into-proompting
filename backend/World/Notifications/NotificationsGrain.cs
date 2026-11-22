namespace PartyTown.World.Notifications;

public sealed class NotificationsGrain(
    [PersistentState(stateName: "notifications", storageName: "parties")]
    IPersistentState<NotificationsState> state)
    : Grain, INotificationsGrain
{
    private const int MaxKept = 300;

    public Task<NotificationsState> GetAsync() => Task.FromResult(state.State);

    public async Task<NotificationsState> MarkReadAsync(List<string> ids, long? upTo)
    {
        if (upTo is { } at && at > state.State.SeenAt) state.State.SeenAt = at;
        state.State.Read = Keep(state.State.Read, ids);
        await state.WriteStateAsync();
        return state.State;
    }

    public async Task<NotificationsState> DismissAsync(List<string> ids, long? upTo)
    {
        if (upTo is { } at && at > state.State.ClearedAt) state.State.ClearedAt = at;
        state.State.Dismissed = Keep(state.State.Dismissed, ids);
        state.State.Read = Keep(state.State.Read, ids);
        await state.WriteStateAsync();
        return state.State;
    }

    private static List<string> Keep(List<string> kept, IEnumerable<string> more)
    {
        foreach (var id in more.Where(id => !kept.Contains(id))) kept.Add(id);
        return kept.Count > MaxKept ? kept.TakeLast(MaxKept).ToList() : kept;
    }
}

[Alias("PartyTown.World.Notifications.INotificationsGrain")]
public interface INotificationsGrain : IGrainWithStringKey
{
    [Alias("GetAsync")] Task<NotificationsState> GetAsync();
    [Alias("MarkReadAsync")] Task<NotificationsState> MarkReadAsync(List<string> ids, long? upTo);
    [Alias("DismissAsync")] Task<NotificationsState> DismissAsync(List<string> ids, long? upTo);
}

[GenerateSerializer, Alias("PartyTown.World.Notifications.NotificationsState")]
public sealed class NotificationsState
{
    [Id(0)] public long SeenAt { get; set; }
    [Id(1)] public List<string> Read { get; set; } = new();
    [Id(2)] public List<string> Dismissed { get; set; } = new();
    [Id(3)] public long ClearedAt { get; set; }
}
