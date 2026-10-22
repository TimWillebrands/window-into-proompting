namespace PartyTown.World.Self;

/// <summary>
/// What a Persona has looked at lately (key = <c>party:persona</c>): opened the Board, scrolled back through a chat.
/// Apps render in full only what is in mind; everything else reaches the Persona as a notification.
/// </summary>
public sealed class PersonaAttentionGrain(
    [PersistentState(stateName: "attention", storageName: "parties")]
    IPersistentState<AttentionState> state)
    : Grain, IPersonaAttentionGrain
{
    private static readonly TimeSpan Forget = TimeSpan.FromDays(2);

    public Task<Dictionary<Guid, AttentionLook>> GetLooksAsync()
        => Task.FromResult(state.State.Looks.ToDictionary(kv => kv.Key, kv => kv.Value));

    public async Task LookAsync(Guid target, int? fromMessageId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        state.State.Looks[target] = new AttentionLook { At = now, FromMessageId = fromMessageId };
        var stale = now - (long)Forget.TotalMilliseconds;
        foreach (var key in state.State.Looks.Where(kv => kv.Value.At < stale).Select(kv => kv.Key).ToList())
            state.State.Looks.Remove(key);
        await state.WriteStateAsync();
    }
}

[Alias("PartyTown.World.Self.IPersonaAttentionGrain")]
public interface IPersonaAttentionGrain : IGrainWithStringKey
{
    [Alias("GetLooksAsync")] Task<Dictionary<Guid, AttentionLook>> GetLooksAsync();
    /// <summary>Looked at <paramref name="target"/> now; for a chat, read back to <paramref name="fromMessageId"/>.</summary>
    [Alias("LookAsync")] Task LookAsync(Guid target, int? fromMessageId);
}

[GenerateSerializer, Alias("PartyTown.World.Self.AttentionState")]
public sealed class AttentionState
{
    [Id(0)] public Dictionary<Guid, AttentionLook> Looks { get; set; } = new();
}

[GenerateSerializer, Alias("PartyTown.World.Self.AttentionLook")]
public sealed class AttentionLook
{
    [Id(0)] public long At { get; set; }
    [Id(1)] public int? FromMessageId { get; set; }
}
