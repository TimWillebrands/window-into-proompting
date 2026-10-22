using System.Globalization;

namespace PartyTown.World.Growth;

/// <summary>Footfall ledger of one Party (key = Party id, ADR 0027); opens Locales past the gates when Auto is on, else only logs.</summary>
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

    /// <summary>Each Locale past all three gates is logged once a day; with Auto on, the strongest opens, one a day.</summary>
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
            // Places keeps the one-a-day budget for every kind of Opening.
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
    /// <summary>One signal on a Locale from one Participant (<see cref="FootfallSources"/>); repeats within a day are ignored.</summary>
    [Alias("NoteAsync")] Task NoteAsync(Guid localeId, Guid participantId, string source);

    /// <summary>Every Locale with live footfall, strongest first, with the gate it's still missing.</summary>
    [Alias("GetReadingsAsync")] Task<List<FootfallReading>> GetReadingsAsync();

    /// <summary>Locales that passed all gates, with the day they last did (dry run).</summary>
    [Alias("GetWouldOpenAsync")] Task<Dictionary<Guid, string>> GetWouldOpenAsync();

    /// <summary>Author switch: open Locales that pass the gates (one a day), or only log them.</summary>
    [Alias("SetAutoAsync")] Task SetAutoAsync(bool on);

    [Alias("GetAutoAsync")] Task<bool> GetAutoAsync();
}

[GenerateSerializer, Alias("PartyTown.World.Growth.GrowthState")]
public sealed class GrowthState
{
    [Id(0)] public List<FootfallSignal> Signals { get; set; } = new();
    /// <summary>Locale id → last day it passed the gates.</summary>
    [Id(1)] public Dictionary<Guid, string> WouldOpen { get; set; } = new();
    /// <summary>Open automatically; off until the thresholds are tuned on a live world.</summary>
    [Id(2)] public bool Auto { get; set; }
}
