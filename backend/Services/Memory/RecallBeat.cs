namespace PartyTown.Services.Memory;

/// <summary>What a beat recalls from: who, where, which Party, the people present and the words in play.</summary>
public sealed record BeatAnchors(Guid PersonaId, Guid PartyId, IReadOnlyList<Guid> PresentIds, string AnchorText, Guid? PlaceId = null);

/// <summary>One beat's memory: Recollections (two-arm union, ADR 0015) and Stance lines (ADR 0016).</summary>
public sealed record RecallBeat(IReadOnlyList<RecalledMemory> Memories, IReadOnlyList<StanceLine> Stances)
{
    public static readonly RecallBeat Empty = new(Array.Empty<RecalledMemory>(), Array.Empty<StanceLine>());
}

public static class RecallBeatExtensions
{
    /// <summary>
    /// Recall + Stance floor for one beat. Each half is non-fatal on its own — a memory outage never mutes a
    /// Persona, and a failed Recall no longer drops the Stances with it. Cancellation still propagates.
    /// </summary>
    public static async Task<RecallBeat> RecallBeatAsync(
        this IMemoryRepository repository, BeatAnchors at, int memoryLimit, int stanceLimit, ILogger logger, CancellationToken ct)
    {
        IReadOnlyList<RecalledMemory> memories = Array.Empty<RecalledMemory>();
        IReadOnlyList<StanceLine> stances = Array.Empty<StanceLine>();
        try
        {
            memories = await repository.RecallAsync(at.PersonaId, at.PartyId, at.PresentIds, at.AnchorText, memoryLimit, ct, at.PlaceId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Recall failed for persona {PersonaId} in party {PartyId}; proceeding without recollections",
                at.PersonaId, at.PartyId);
        }
        try
        {
            stances = await repository.RecallStancesAsync(at.PartyId, at.PersonaId, at.PresentIds, at.AnchorText, stanceLimit, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Stance recall failed for persona {PersonaId} in party {PartyId}; proceeding without stances",
                at.PersonaId, at.PartyId);
        }
        return new RecallBeat(memories, stances);
    }
}
