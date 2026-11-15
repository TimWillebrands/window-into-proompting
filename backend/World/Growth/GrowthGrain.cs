using System.Globalization;

namespace PartyTown.World.Growth;

public sealed class GrowthGrain(
    [PersistentState(stateName: "growth", storageName: "parties")]
    IPersistentState<GrowthState> state,
    TimeProvider time,
    ILogger<GrowthGrain> logger)
    : Grain, IGrowthGrain
{
    public async Task NoteAsync(Guid localeId, Guid participantId, string source)
    {
        var now = Now();
        var signal = new FootfallSignal
        {
            LocaleId = localeId, ParticipantId = participantId, Source = source,
            Weight = FootfallSources.WeightOf(source), At = now, Day = DayKey(),
        };
        if (FootfallMath.Duplicate(state.State.Signals, signal)) return;
        state.State.Signals.RemoveAll(s => now - s.At > FootfallMath.Kept.TotalMilliseconds);
        state.State.Signals.Add(signal);
        await EvaluateAsync(now);
        await state.WriteStateAsync();
    }

    public Task<List<FootfallReading>> GetReadingsAsync() => Task.FromResult(FootfallMath.Read(state.State.Signals, Now()));

    public Task<Dictionary<Guid, string>> GetWouldOpenAsync() => Task.FromResult(new Dictionary<Guid, string>(state.State.WouldOpen));

    public async Task SetAutoAsync(bool on)
    {
        state.State.Auto = on;
        await state.WriteStateAsync();
    }

    public Task<bool> GetAutoAsync() => Task.FromResult(state.State.Auto);

    private async Task EvaluateAsync(long now)
    {
        var today = DayKey();
        foreach (var r in FootfallMath.Read(state.State.Signals, now).Where(r => r.Missing is null))
        {
            if (state.State.WouldOpen.GetValueOrDefault(r.LocaleId) == today) continue;
            state.State.WouldOpen[r.LocaleId] = today;
            logger.LogInformation("World growth: {Locale} passed the gates: footfall {Footfall}, {People} people, {Days} days",
                r.LocaleId, r.Footfall, r.People, r.Days);
            if (!state.State.Auto) continue;
            try
            {
                await GrainFactory.GetGrain<Places.IPlacesGrain>(this.GetPrimaryKey()).OpenAsync(r.LocaleId, null, "footfall");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Opening {Locale} failed", r.LocaleId);
            }
        }
    }

    private long Now() => time.GetUtcNow().ToUnixTimeMilliseconds();

    private string DayKey() => time.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

[Alias("PartyTown.World.Growth.IGrowthGrain")]
public interface IGrowthGrain : IGrainWithGuidKey
{
    [Alias("NoteAsync")] Task NoteAsync(Guid localeId, Guid participantId, string source);

    [Alias("GetReadingsAsync")] Task<List<FootfallReading>> GetReadingsAsync();

    [Alias("GetWouldOpenAsync")] Task<Dictionary<Guid, string>> GetWouldOpenAsync();

    [Alias("SetAutoAsync")] Task SetAutoAsync(bool on);

    [Alias("GetAutoAsync")] Task<bool> GetAutoAsync();
}

[GenerateSerializer, Alias("PartyTown.World.Growth.GrowthState")]
public sealed class GrowthState
{
    [Id(0)] public List<FootfallSignal> Signals { get; set; } = new();
    [Id(1)] public Dictionary<Guid, string> WouldOpen { get; set; } = new();
    [Id(2)] public bool Auto { get; set; }
}
