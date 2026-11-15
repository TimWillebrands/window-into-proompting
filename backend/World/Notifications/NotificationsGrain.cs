namespace PartyTown.World.Notifications;

public sealed class NotificationsGrain(
    [PersistentState(stateName: "notifications", storageName: "parties")]
    IPersistentState<NotificationsState> state)
    : Grain, INotificationsGrain
{
    private const int MaxRead = 300;

    public Task<NotificationsState> GetAsync() => Task.FromResult(state.State);

    public async Task<NotificationsState> MarkReadAsync(List<string> ids, long? upTo)
    {
        if (upTo is { } at && at > state.State.SeenAt) state.State.SeenAt = at;
        foreach (var id in ids.Where(id => !state.State.Read.Contains(id))) state.State.Read.Add(id);
        if (state.State.Read.Count > MaxRead) state.State.Read = state.State.Read.TakeLast(MaxRead).ToList();
        await state.WriteStateAsync();
        return state.State;
    }
}

[Alias("PartyTown.World.Notifications.INotificationsGrain")]
public interface INotificationsGrain : IGrainWithStringKey
{
    [Alias("GetAsync")] Task<NotificationsState> GetAsync();
    [Alias("MarkReadAsync")] Task<NotificationsState> MarkReadAsync(List<string> ids, long? upTo);
}

[GenerateSerializer, Alias("PartyTown.World.Notifications.NotificationsState")]
public sealed class NotificationsState
{
    [Id(0)] public long SeenAt { get; set; }
    [Id(1)] public List<string> Read { get; set; } = new();
}
