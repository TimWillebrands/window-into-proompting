namespace PartyTown.Services.Memory;

internal readonly record struct RecallCandidate(string Key, RecalledMemory Memory, bool Relevant);

internal static class RecallRanking
{
    public const int RelevantArmSlots = 5;
    public const int CandidatePool = 50;
    public const int RecentPoolPerSlot = 3;

    public static int RelevantSlots(int limit) => Math.Min(RelevantArmSlots, (Math.Max(0, limit) + 1) / 2);

    public static int RecentPool(int slots) => Math.Min(CandidatePool, Math.Max(1, slots) * RecentPoolPerSlot);

    public static readonly TimeSpan Horizon = TimeSpan.FromDays(3);

    public static List<RecallCandidate> Fresh(IEnumerable<RecallCandidate> rows, DateTimeOffset now)
        => rows.Where(r => now - r.Memory.CapturedAt < Horizon).ToList();

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

    public static IReadOnlyList<RecalledMemory> QuotaUnion(IReadOnlyList<RecallCandidate> candidates, int limit)
    {
        var relevantArm = candidates
            .Where(c => c.Relevant)
            .OrderByDescending(c => c.Memory.Salience)
            .ThenByDescending(c => c.Memory.CapturedAt)
            .Take(RelevantSlots(limit))
            .ToList();
        var taken = relevantArm.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);

        var recentArm = candidates
            .Where(c => !taken.Contains(c.Key))
            .OrderByDescending(c => c.Memory.CapturedAt)
            .Take(RecentPool(limit - relevantArm.Count))
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
