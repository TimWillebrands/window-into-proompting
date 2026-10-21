namespace PartyTown.Services.Memory;

/// <summary>One fetched RECOLLECTS row before ranking; <see cref="Relevant"/> = matched an anchor leg.</summary>
internal readonly record struct RecallCandidate(string Key, RecalledMemory Memory, bool Relevant);

/// <summary>
/// Recall's C# half (ADR 0015) as pure functions: dedup the UNION's per-leg rows, then the
/// two-arm quota union. Scores come in from <see cref="SalienceMath.Score"/>; nothing here re-scores.
/// </summary>
internal static class RecallRanking
{
    /// <summary>Relevant-arm quota: structural tension, not a merged formula — keeps a serendipity budget on hot topics.</summary>
    public const int RelevantArmSlots = 5;
    /// <summary>How many newest edges the recency arm ranks.</summary>
    public const int CandidatePool = 50;

    /// <summary>Same edge from several legs → one candidate; relevance wins.</summary>
    public static List<RecallCandidate> Dedup(IEnumerable<RecallCandidate> rows)
    {
        var byKey = new Dictionary<string, RecallCandidate>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var row in rows)
        {
            if (byKey.TryGetValue(row.Key, out var existing))
            {
                byKey[row.Key] = existing with { Relevant = existing.Relevant || row.Relevant };
                continue;
            }
            byKey[row.Key] = row;
            order.Add(row.Key);
        }
        return order.Select(k => byKey[k]).ToList();
    }

    /// <summary>
    /// Relevant arm takes up to <see cref="RelevantArmSlots"/>; the recency arm (newest <see cref="CandidatePool"/>)
    /// fills the rest; both salience-ranked, then merged by salience so the prompt never shows which arm surfaced what.
    /// </summary>
    public static IReadOnlyList<RecalledMemory> QuotaUnion(IReadOnlyList<RecallCandidate> candidates, int limit)
    {
        var relevantArm = candidates
            .Where(c => c.Relevant)
            .OrderByDescending(c => c.Memory.Salience)
            .ThenByDescending(c => c.Memory.CapturedAt)
            .Take(Math.Min(RelevantArmSlots, limit))
            .ToList();
        var taken = relevantArm.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);

        // A relevant edge that missed the relevant quota still competes here — never duplicated.
        var recentArm = candidates
            .OrderByDescending(c => c.Memory.CapturedAt)
            .Take(CandidatePool)
            .Where(c => !taken.Contains(c.Key))
            .OrderByDescending(c => c.Memory.Salience)
            .ThenByDescending(c => c.Memory.CapturedAt)
            .Take(limit - relevantArm.Count);

        return relevantArm
            .Select(c => c.Memory with { Arm = RecallArm.Relevant })
            .Concat(recentArm.Select(c => c.Memory with { Arm = RecallArm.Recent }))
            .OrderByDescending(m => m.Salience)
            .ThenByDescending(m => m.CapturedAt)
            .ToList();
    }
}
