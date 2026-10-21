namespace PartyTown.World;

/// <summary>
/// The Diary App's store: one Persona's private journal inside one Party (key = <c>party:persona</c>).
/// The visible face of rest — entries are written alongside Consolidation.
/// </summary>
public sealed class DiaryGrain(
    [PersistentState(stateName: "diary", storageName: "parties")]
    IPersistentState<DiaryState> state)
    : Grain, IDiaryGrain
{
    private const int MaxEntries = 100;

    public Task<List<DiaryEntry>> GetEntriesAsync()
        => Task.FromResult(state.State.Entries.OrderByDescending(e => e.CreatedAt).ToList());

    public async Task<DiaryEntry> AddEntryAsync(string body, string? mood)
    {
        var entry = new DiaryEntry
        {
            Id = Guid.NewGuid(),
            Body = body.Trim(),
            Mood = mood,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        state.State.Entries.Add(entry);
        if (state.State.Entries.Count > MaxEntries)
            state.State.Entries.RemoveAt(0);
        await state.WriteStateAsync();

        var (partyId, personaId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        await this.GetStreamProvider(Services.Streaming.PartyStreamIds.Provider)
            .PublishWorldAsync(partyId, "diary", new { personaId, entry });
        return entry;
    }
}

[Alias("PartyTown.World.IDiaryGrain")]
public interface IDiaryGrain : IGrainWithStringKey
{
    [Alias("GetEntriesAsync")] Task<List<DiaryEntry>> GetEntriesAsync();
    [Alias("AddEntryAsync")] Task<DiaryEntry> AddEntryAsync(string body, string? mood);
}

[GenerateSerializer, Alias("PartyTown.World.DiaryState")]
public sealed class DiaryState
{
    [Id(0)] public List<DiaryEntry> Entries { get; set; } = new();
}

[GenerateSerializer, Alias("PartyTown.World.DiaryEntry")]
public sealed class DiaryEntry
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public string Body { get; set; } = string.Empty;
    [Id(2)] public string? Mood { get; set; }
    [Id(3)] public long CreatedAt { get; set; }
}

public static class WorldKeys
{
    public static string Life(Guid partyId, Guid personaId) => $"{partyId:N}:{personaId:N}";

    public static (Guid PartyId, Guid PersonaId) ParseLife(string key)
    {
        var parts = key.Split(':');
        return (Guid.Parse(parts[0]), Guid.Parse(parts[1]));
    }
}
