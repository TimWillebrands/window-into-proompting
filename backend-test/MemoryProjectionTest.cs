using PartyTown.Services.Memory;

namespace BackendTest;

/// <summary>Recall ranking and Stance projection rules (ADR 0015/0016) without Postgres.</summary>
public class MemoryProjectionTest
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-10-01T12:00:00Z");

    private static RecallCandidate Cand(string key, double salience, int minutesAgo, bool relevant)
        => new(key, new RecalledMemory(Guid.NewGuid(), key, 0.5, 0, null, T0.AddMinutes(-minutesAgo), salience), relevant);

    [Fact]
    public void Dedup_KeepsOnePerEdge_RelevanceWins()
    {
        var rows = RecallRanking.Dedup(new[] { Cand("a", 0.5, 1, false), Cand("a", 0.5, 1, true), Cand("b", 0.4, 2, false) });

        Assert.Equal(new[] { ("a", true), ("b", false) }, rows.Select(r => (r.Key, r.Relevant)));
    }

    [Fact]
    public void QuotaUnion_RelevantCappedAtFive_RecencyFills_MergedBySalience()
    {
        var relevant = Enumerable.Range(0, 7).Select(i => Cand($"r{i}", 0.9 - i * 0.01, 500 + i, true));
        var recent = Enumerable.Range(0, 5).Select(i => Cand($"n{i}", 0.5 - i * 0.01, i, false));

        var picked = RecallRanking.QuotaUnion(relevant.Concat(recent).ToList(), limit: 8);

        Assert.Equal(8, picked.Count);
        Assert.Equal(5, picked.Count(m => m.Arm == RecallArm.Relevant));
        Assert.Equal(picked.OrderByDescending(m => m.Salience).Select(m => m.Snippet), picked.Select(m => m.Snippet));
    }

    private static readonly Guid Self = Guid.NewGuid();
    private static readonly Guid Vlad = Guid.NewGuid();

    private static StanceEdgeRow Edge(double valence, string reasoning, int minutesAgo,
        Guid? target = null, string? concept = null, StanceOrigin origin = StanceOrigin.Curator)
        => new(Guid.NewGuid(), valence, reasoning, T0.AddMinutes(-minutesAgo), target, concept, concept, origin, null, null);

    private static IReadOnlyList<StanceEdgeRow> NewestFirst(params StanceEdgeRow[] rows)
        => rows.OrderByDescending(r => r.Ts).ToList();

    [Fact]
    public void Project_LatestWins_PresentOnly_ConceptsByWholeWords()
    {
        var rows = NewestFirst(
            Edge(0.8, "vlad is fine now", 1, Vlad),
            Edge(-0.6, "vlad was rude", 10, Vlad),
            Edge(0.5, "common lisp is elegant", 2, concept: "common lisp"),
            Edge(0.4, "sci-fi is home", 3, concept: "sci-fi"),
            Edge(0.9, "go is boring", 4, concept: "go"));

        var lines = StanceProjection.Project(rows, new HashSet<Guid> { Self, Vlad },
            "Anyone into Common Lisp or sci-fi? I'm going out.", limit: 5);

        Assert.Equal(new[] { "vlad is fine now", "common lisp is elegant", "sci-fi is home" }, lines.Select(l => l.Reasoning));
        Assert.Equal("vlad was rude", lines[0].Contrast);
    }

    [Fact]
    public void Project_RetractMasks_AbsentParticipantsDrop_AmbivalenceCappedAtTwo()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var rows = NewestFirst(
            Edge(0, "retracted", 1, Vlad, origin: StanceOrigin.Retract),
            Edge(0.9, "vlad is great", 5, Vlad),
            Edge(0.9, "a now", 1, a), Edge(-0.9, "a then", 9, a),
            Edge(0.8, "b now", 1, b), Edge(-0.8, "b then", 9, b),
            Edge(0.7, "c now", 1, c), Edge(-0.7, "c then", 9, c));

        var lines = StanceProjection.Project(rows, new HashSet<Guid> { Vlad, a, b, c }, null, limit: 5);

        Assert.DoesNotContain(lines, l => l.Reasoning.StartsWith("vlad") || l.Reasoning == "retracted");
        Assert.Equal(new[] { "a then", "b then", null }, lines.Select(l => l.Contrast));
        Assert.Empty(StanceProjection.Project(rows, new HashSet<Guid>(), null, limit: 5));
    }

    [Fact]
    public void Union_And_WithCurrent_SpanBothScopes()
    {
        var acquired = NewestFirst(Edge(0.2, "acquired older", 10, Vlad));
        var intrinsic = NewestFirst(Edge(0.6, "intrinsic newer", 1, Vlad));

        var flags = StanceProjection.WithCurrent(StanceProjection.Union(acquired, intrinsic))
            .Select(r => (r.Row.Reasoning, r.IsCurrent));

        Assert.Equal(new[] { ("intrinsic newer", true), ("acquired older", false) }, flags);
    }
}
