using PartyTown.Services.Memory;

namespace PartyTown.Services.ResponsePipeline;

/// <summary>
/// Deterministic deployment default for recalled memories — the "fighting chance" floor
/// for small decision models.
///
/// The extractor's weight rubric defines 0.7-0.9 as "significant — they'd bring it up on
/// their own". The Decision LLM is supposed to honor that by picking the memory, but the
/// pick is the single judgment small models most reliably fumble (bench: memory surfaced
/// at 0.8 via the relevant arm, decision returned null, callback never landed). This class
/// enforces the rubric's own semantics in math: when the model declines to pick, a memory
/// that (a) qualified via the RELEVANT arm — anchored to who's present / what's named, so
/// it can't hijack an unrelated beat, (b) sits at or above the "bring it up on their own"
/// weight, and (c) has never been deployed before, deploys anyway.
///
/// The RecallCount == 0 gate makes auto-deploy one-shot per memory: the strengthening
/// write increments the count on every pick (model or auto), so a floor-deployed memory
/// never loops. After its debut the memory is the model's to choose — the floor only
/// guarantees a significant moment gets its first callback.
///
/// An explicit model pick always wins; the floor fires only on null.
/// </summary>
public static class RecallDeployment
{
    /// <summary>The rubric's "they'd bring it up on their own" boundary (see
    /// <see cref="Memory.MemoryExtractor"/>'s weight scale).</summary>
    public const double AutoDeploySalienceFloor = 0.7;

    /// <summary>
    /// Resolves the Decision phase's index pick against the surfaced list, applying the
    /// salience floor when the model declined. <paramref name="modelPick"/> is the 1-based
    /// index (already range-clamped by the decision service); <paramref name="responding"/>
    /// gates the floor — a persona that stays silent deploys nothing (and burns no
    /// one-shot). Returns the resolved memory (null = nothing deploys) and whether the
    /// floor, rather than the model, chose it.
    /// </summary>
    public static (RecalledMemory? Memory, bool AutoDeployed) ResolvePick(
        int? modelPick,
        bool responding,
        IReadOnlyList<RecalledMemory> recalled)
    {
        if (modelPick is int pick && pick >= 1 && pick <= recalled.Count)
            return (recalled[pick - 1], false);

        if (!responding)
            return (null, false);

        // recalled is salience-ranked (RecallAsync contract) — first qualifier is the
        // strongest one.
        var auto = recalled.FirstOrDefault(m =>
            m.Arm == RecallArm.Relevant &&
            m.Salience >= AutoDeploySalienceFloor &&
            m.RecallCount == 0);

        return (auto, auto is not null);
    }
}
