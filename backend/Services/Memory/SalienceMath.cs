namespace PartyTown.Services.Memory;

/// <summary>
/// Read-time Salience scoring (ADR 0015):
/// <c>salience = weight × decay(now − ts) + use_bonus(recall_count, last_recalled)</c>.
/// Pure arithmetic over the candidate set, computed in C# — never in Cypher, never an
/// LLM call. Decay runs on wall-clock time, deliberately (ADR 0015 rejected ordinal
/// decay): a Party left alone for three weeks comes back with somewhat hazy personas.
/// </summary>
public static class SalienceMath
{
    /// <summary>
    /// Half-life of an unreinforced memory: after a week, a weight-1.0 capture scores
    /// like a fresh weight-0.5 one.
    /// </summary>
    public const double DecayHalfLifeDays = 7.0;

    /// <summary>
    /// Reinforcement fades on a slower clock than raw capture recency — a memory the
    /// persona keeps returning to stays warm even when its capture is old.
    /// </summary>
    public const double UseBonusHalfLifeDays = 14.0;

    private const double UseBonusPerRecall = 0.15;
    private const double UseBonusCap = 0.5;

    public static double Decay(TimeSpan age)
        => Math.Pow(0.5, Math.Max(0, age.TotalDays) / DecayHalfLifeDays);

    /// <summary>
    /// Log-diminishing bonus per pick, capped, fading by time since the last pick.
    /// Strengthening fires on the Decision phase's pick only — attention strengthens,
    /// exposure doesn't — so this rewards memories the persona actually reached for.
    /// </summary>
    public static double UseBonus(int recallCount, DateTimeOffset? lastRecalled, DateTimeOffset now)
    {
        if (recallCount <= 0)
        {
            return 0;
        }
        var bonus = Math.Min(UseBonusCap, UseBonusPerRecall * Math.Log(1 + recallCount));
        if (lastRecalled is null)
        {
            // recall_count without a last_recalled stamp shouldn't happen (one write sets
            // both) — keep the bonus undecayed rather than invent a timestamp.
            return bonus;
        }
        return bonus * Math.Pow(0.5, Math.Max(0, (now - lastRecalled.Value).TotalDays) / UseBonusHalfLifeDays);
    }

    public static double Score(
        double weight,
        DateTimeOffset capturedAt,
        int recallCount,
        DateTimeOffset? lastRecalled,
        DateTimeOffset now)
        => weight * Decay(now - capturedAt) + UseBonus(recallCount, lastRecalled, now);
}
