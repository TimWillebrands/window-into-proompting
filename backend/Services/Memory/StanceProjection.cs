namespace PartyTown.Services.Memory;

/// <summary>One raw STANCE edge as read back: a Participant target (TargetPersonaId) or a Concept (ConceptName).</summary>
internal sealed record StanceEdgeRow(
    Guid Id,
    double Valence,
    string Reasoning,
    DateTimeOffset Ts,
    Guid? TargetPersonaId,
    string? ConceptName,
    string? ConceptDisplay,
    StanceOrigin Origin,
    Guid? RunId,
    Guid? RetractsId);

/// <summary>
/// The Stance floor's read rules (ADR 0007/0016) as pure functions over append-only edges:
/// latest-wins per target, retract masking, beat anchoring, ambivalence. No storage.
/// </summary>
internal static class StanceProjection
{
    /// <summary>At most this many ambivalence pairs render per beat; the rest stay plain latest-wins.</summary>
    public const int MaxAmbivalencePairs = 2;

    /// <summary>Acquired + Intrinsic edges as one newest-first stream; latest-wins then spans both scopes.</summary>
    public static IReadOnlyList<StanceEdgeRow> Union(IReadOnlyList<StanceEdgeRow> acquired, IReadOnlyList<StanceEdgeRow> intrinsic)
        => acquired.Count == 0 ? intrinsic
            : intrinsic.Count == 0 ? acquired
            : acquired.Concat(intrinsic).OrderByDescending(r => r.Ts).ToList();

    /// <summary>Each edge with whether it is its target's current belief (rows newest-first).</summary>
    public static IEnumerable<(StanceEdgeRow Row, bool IsCurrent)> WithCurrent(IEnumerable<StanceEdgeRow> newestFirst)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in newestFirst)
            yield return (row, seen.Add(TargetKey(row)));
    }

    /// <summary>
    /// The beat's "# Where you stand" lines: current belief per target, Participants only while present,
    /// Concepts only when live in the anchor text, strongest first, capped; contrast for 1–2 ambivalent ones.
    /// </summary>
    public static IReadOnlyList<StanceLine> Project(
        IReadOnlyList<StanceEdgeRow> newestFirst, IReadOnlySet<Guid> present, string? anchorText, int limit)
    {
        if (limit <= 0) return Array.Empty<StanceLine>();
        var words = MemoryRepository.AnchorWords(anchorText);

        var anchored = new List<(StanceEdgeRow Current, string? Contrast)>();
        foreach (var history in newestFirst.GroupBy(TargetKey).Select(g => g.ToList()))
        {
            var current = history[0];
            // A retract-current target holds no belief; the neutralizing edge renders nothing.
            if (current.Origin == StanceOrigin.Retract) continue;
            var live = current.TargetPersonaId is Guid tp ? present.Contains(tp) : ConceptLive(current, words);
            if (live) anchored.Add((current, FindContradiction(current, history)));
        }

        var pairs = 0;
        return anchored
            .OrderByDescending(a => Math.Abs(a.Current.Valence))
            .ThenByDescending(a => a.Current.Ts)
            .Take(limit)
            .Select(a => new StanceLine(a.Current.Reasoning, a.Current.Valence,
                a.Contrast is not null && ++pairs <= MaxAmbivalencePairs ? a.Contrast : null))
            .ToList();
    }

    /// <summary>
    /// A Concept is live when its name's (or display's) words occur consecutively in the anchor's words —
    /// the same word stream Recall's anchors come from; whole words, never substrings ("lisp" ≠ "lisping").
    /// </summary>
    public static bool ConceptLive(StanceEdgeRow row, IReadOnlyList<string> anchorWords)
        => (row.ConceptName is { Length: > 0 } name && ContainsRun(anchorWords, MemoryRepository.AnchorWords(name)))
            || (row.ConceptDisplay is { Length: > 0 } display && ContainsRun(anchorWords, MemoryRepository.AnchorWords(display)));

    private static bool ContainsRun(IReadOnlyList<string> haystack, IReadOnlyList<string> needle)
    {
        if (needle.Count == 0) return false;
        for (var i = 0; i + needle.Count <= haystack.Count; i++)
        {
            var j = 0;
            while (j < needle.Count && haystack[i + j] == needle[j]) j++;
            if (j == needle.Count) return true;
        }
        return false;
    }

    /// <summary>
    /// The earlier edge that most sharply contradicts the current one (sign flip or |Δ| ≥ 1),
    /// ties to the oldest ("you used to"); retracts never count. Null when the history is monotone.
    /// </summary>
    public static string? FindContradiction(StanceEdgeRow current, IReadOnlyList<StanceEdgeRow> history)
    {
        StanceEdgeRow? best = null;
        var bestDelta = 0.0;
        for (var i = 1; i < history.Count; i++)
        {
            var past = history[i];
            if (past.Origin == StanceOrigin.Retract || !Contradicts(current.Valence, past.Valence)) continue;
            var delta = Math.Abs(current.Valence - past.Valence);
            if (delta > bestDelta || (delta == bestDelta && (best is null || past.Ts < best.Ts)))
            {
                bestDelta = delta;
                best = past;
            }
        }
        return best?.Reasoning;
    }

    public static bool Contradicts(double current, double past)
        => (Math.Sign(current) != 0 && Math.Sign(past) != 0 && Math.Sign(current) != Math.Sign(past))
            || Math.Abs(current - past) >= 1.0;

    /// <summary>Latest-wins key: Participant targets by persona id (self included), Concepts by name.</summary>
    public static string TargetKey(StanceEdgeRow row)
        => row.TargetPersonaId is Guid tp ? $"p:{tp}" : $"c:{row.ConceptName}";

    public static StanceRecord ToRecord(StanceEdgeRow row, Guid sourcePersonaId, bool isCurrent, bool isIntrinsic = false)
        => row.TargetPersonaId is Guid tp
            ? new StanceRecord(row.Id, row.Valence, row.Reasoning, row.Ts,
                tp == sourcePersonaId ? StanceTargetKind.Self : StanceTargetKind.Participant, tp, null, null,
                isCurrent, row.Origin, row.RunId, row.RetractsId, isIntrinsic)
            : new StanceRecord(row.Id, row.Valence, row.Reasoning, row.Ts,
                StanceTargetKind.Concept, null, row.ConceptName, row.ConceptDisplay,
                isCurrent, row.Origin, row.RunId, row.RetractsId, isIntrinsic);
}
