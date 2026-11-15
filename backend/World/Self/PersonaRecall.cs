using PartyTown.Grains;
using PartyTown.Services.Memory;

namespace PartyTown.World.Self;

public static class PersonaRecall
{
    public static async Task<RecallBeat> ForBeatAsync(
        IMemoryRepository repository, IGrainFactory grains, BeatAnchors at, int memoryLimit, int stanceLimit, ILogger logger, CancellationToken ct)
    {
        var beat = await repository.RecallBeatAsync(at, memoryLimit, stanceLimit, logger, ct);
        try
        {
            var names = (await grains.GetGrain<IPartyGrain>(at.PartyId).GetCastAsync()).Select(c => c.Name).ToList();
            var stale = await grains.GetGrain<IWorldVoiceGrain>(at.PartyId).GetStaleWordsAsync(at.PersonaId);
            return StaleRecall.Apply(RecallVariety.Apply(beat, names), stale);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Stale recall filter failed");
            return beat;
        }
    }
}
