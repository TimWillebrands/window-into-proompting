namespace PartyTown.World.Work.Works;

[GenerateSerializer]
public enum CommissionKind
{
    /// <summary>Changes the code and can become a Proposition.</summary>
    Change = 0,
    /// <summary>A read-only question about how the world works.</summary>
    Survey = 1,
}

[GenerateSerializer]
public enum CommissionStatus
{
    Filed = 0,
    Working = 1,
    /// <summary>The builder has stopped and it's the Architect's move.</summary>
    Waiting = 2,
    Checking = 3,
    /// <summary>A Proposition, waiting on the world-spirits.</summary>
    Proposed = 4,
    Accepted = 5,
    Rejected = 6,
    /// <summary>Stacked on a Proposition the spirits turned down.</summary>
    Fallen = 7,
    Dropped = 8,
    Answered = 9,
    Failed = 10,
}

/// <summary>What a check said; only the Runner writes these (ADR 0039).</summary>
[GenerateSerializer, Alias("PartyTown.World.Work.Works.CommissionCheck")]
public sealed record CommissionCheck(
    [property: Id(0)] string Name,
    [property: Id(1)] bool Passed,
    [property: Id(2)] int Seconds,
    [property: Id(3)] string Detail);

/// <summary>One unit of work an Architect ordered from the builder (ADR 0039).</summary>
[GenerateSerializer, Alias("PartyTown.World.Work.Works.Commission")]
public sealed class Commission
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public Guid CompanyId { get; set; }
    [Id(2)] public Guid ArchitectId { get; set; }
    [Id(3)] public string ArchitectName { get; set; } = string.Empty;
    [Id(4)] public CommissionKind Kind { get; set; }
    [Id(5)] public string Title { get; set; } = string.Empty;
    /// <summary>What they saw, in their own words.</summary>
    [Id(6)] public string Why { get; set; } = string.Empty;
    /// <summary>What should behave differently; for a Survey, the question.</summary>
    [Id(7)] public string Brief { get; set; } = string.Empty;
    [Id(8)] public string Model { get; set; } = string.Empty;
    [Id(9)] public double BudgetUsd { get; set; }
    [Id(10)] public CommissionStatus Status { get; set; }
    [Id(11)] public string? RunId { get; set; }
    /// <summary>The builder's last words. Its claim, not a fact.</summary>
    [Id(12)] public string? Report { get; set; }
    [Id(13)] public double SpentUsd { get; set; }
    [Id(14)] public long Tokens { get; set; }
    [Id(15)] public int Files { get; set; }
    [Id(16)] public int Added { get; set; }
    [Id(17)] public int Removed { get; set; }
    [Id(18)] public List<string> Paths { get; set; } = new();
    /// <summary>Why the last turn ended early: budget, time or crash.</summary>
    [Id(19)] public string? Stop { get; set; }
    [Id(20)] public string? Error { get; set; }
    [Id(21)] public int? Proposition { get; set; }
    [Id(22)] public List<CommissionCheck> Checks { get; set; } = new();
    /// <summary>The world-spirits' word, when they gave one.</summary>
    [Id(23)] public string? Note { get; set; }
    /// <summary>The last transcript line read from the Runner.</summary>
    [Id(24)] public long Cursor { get; set; }
    [Id(25)] public long FiledAt { get; set; }
    [Id(26)] public long UpdatedAt { get; set; }
}

/// <summary>A Runner's read of a run, as the facts a Commission takes on.</summary>
[GenerateSerializer, Alias("PartyTown.World.Work.Works.CommissionFacts")]
public sealed record CommissionFacts(
    [property: Id(0)] CommissionStatus Status,
    [property: Id(1)] double SpentUsd,
    [property: Id(2)] long Tokens,
    [property: Id(3)] string? Report,
    [property: Id(4)] int Files,
    [property: Id(5)] int Added,
    [property: Id(6)] int Removed,
    [property: Id(7)] List<string> Paths,
    [property: Id(8)] string? Stop,
    [property: Id(9)] string? Error,
    [property: Id(10)] int? Proposition,
    [property: Id(11)] List<CommissionCheck> Checks,
    [property: Id(12)] long Cursor);

/// <summary>One transcript line for the spectators' window.</summary>
[GenerateSerializer, Alias("PartyTown.World.Work.Works.WorksLine")]
public sealed record WorksLine(
    [property: Id(0)] long N,
    [property: Id(1)] long At,
    [property: Id(2)] string Kind,
    [property: Id(3)] string Text);

[GenerateSerializer]
public enum CommissionNews
{
    None = 0,
    /// <summary>The builder stopped; the Architect has something to look at.</summary>
    TurnDone = 1,
    Answered = 2,
    Proposed = 3,
    Failed = 4,
    Accepted = 5,
    Rejected = 6,
}

/// <summary>A Commission after a change, and what's worth telling about it.</summary>
[GenerateSerializer, Alias("PartyTown.World.Work.Works.CommissionUpdate")]
public sealed record CommissionUpdate(
    [property: Id(0)] Commission Commission,
    [property: Id(1)] CommissionNews News);

/// <summary>Pure: how a Commission moves. Facts come from the Runner; nobody narrates their way forward.</summary>
public static class CommissionRules
{
    public const int Kept = 50;

    public static bool IsOpen(Commission c) => c.Status is CommissionStatus.Filed or CommissionStatus.Working or CommissionStatus.Waiting or CommissionStatus.Checking;

    /// <summary>Still something the Runner has news about, whether a run or a verdict.</summary>
    public static bool IsWatched(Commission c) => IsOpen(c) || c.Status == CommissionStatus.Proposed;

    public static CommissionStatus StatusOf(string runStatus) => runStatus switch
    {
        "starting" or "working" => CommissionStatus.Working,
        "idle" => CommissionStatus.Waiting,
        "checking" => CommissionStatus.Checking,
        "proposed" => CommissionStatus.Proposed,
        "dropped" => CommissionStatus.Dropped,
        "done" => CommissionStatus.Answered,
        _ => CommissionStatus.Failed,
    };

    public static CommissionNews Apply(Commission c, CommissionFacts facts, long now)
    {
        var before = c.Status;
        (c.Status, c.SpentUsd, c.Tokens, c.Report, c.Stop, c.Error) = (facts.Status, facts.SpentUsd, facts.Tokens, facts.Report ?? c.Report, facts.Stop, facts.Error);
        (c.Files, c.Added, c.Removed, c.Paths) = (facts.Files, facts.Added, facts.Removed, facts.Paths);
        (c.Proposition, c.Checks, c.Cursor) = (facts.Proposition, facts.Checks, Math.Max(c.Cursor, facts.Cursor));
        if (before == c.Status) return CommissionNews.None;
        c.UpdatedAt = now;
        return c.Status switch
        {
            CommissionStatus.Waiting => CommissionNews.TurnDone,
            CommissionStatus.Answered => CommissionNews.Answered,
            CommissionStatus.Proposed => CommissionNews.Proposed,
            CommissionStatus.Failed => CommissionNews.Failed,
            _ => CommissionNews.None,
        };
    }

    /// <summary>The Runner's word on a Proposition; open changes nothing.</summary>
    public static CommissionNews Verdict(Commission c, string status, string? note, long now)
    {
        if (c.Status != CommissionStatus.Proposed) return CommissionNews.None;
        var (next, news) = status switch
        {
            "merged" => (CommissionStatus.Accepted, CommissionNews.Accepted),
            "rejected" => (CommissionStatus.Rejected, CommissionNews.Rejected),
            "fallen" => (CommissionStatus.Fallen, CommissionNews.Rejected),
            _ => (c.Status, CommissionNews.None),
        };
        if (news == CommissionNews.None) return news;
        (c.Status, c.Note, c.UpdatedAt) = (next, note, now);
        return news;
    }

    /// <summary>Drops finished ones first, so a long life doesn't grow the state without end.</summary>
    public static void Trim(List<Commission> all)
    {
        while (all.Count > Kept && all.FindIndex(c => !IsWatched(c)) is >= 0 and var oldest) all.RemoveAt(oldest);
    }
}

/// <summary>The Party's Commissions (key = Party id). Pure state: the Runner is reached from <see cref="Worksite"/>.</summary>
public sealed class CommissionsGrain(
    [PersistentState(stateName: "commissions", storageName: "parties")]
    IPersistentState<CommissionsState> state)
    : Grain, ICommissionsGrain
{
    public Task<List<Commission>> GetAllAsync() => Task.FromResult(state.State.Commissions.ToList());

    public Task<Commission?> GetAsync(Guid id) => Task.FromResult(Find(id));

    public async Task<Commission?> FileAsync(Commission commission)
    {
        if (commission.Kind == CommissionKind.Change && state.State.Commissions.Any(c => c.Kind == CommissionKind.Change && CommissionRules.IsOpen(c))) return null;
        state.State.Commissions.Add(commission);
        CommissionRules.Trim(state.State.Commissions);
        await SaveAsync(commission);
        return commission;
    }

    public async Task<Commission?> StartedAsync(Guid id, string runId)
    {
        if (Find(id) is not { } commission) return null;
        (commission.RunId, commission.Status, commission.UpdatedAt) = (runId, CommissionStatus.Working, Now());
        await SaveAsync(commission);
        return commission;
    }

    public async Task<Commission?> MarkAsync(Guid id, CommissionStatus status, string? error = null)
    {
        if (Find(id) is not { } commission) return null;
        (commission.Status, commission.Error, commission.UpdatedAt) = (status, error ?? commission.Error, Now());
        await SaveAsync(commission);
        return commission;
    }

    public async Task<CommissionUpdate?> SyncAsync(Guid id, CommissionFacts facts, List<WorksLine> lines)
    {
        if (Find(id) is not { } commission) return null;
        var news = CommissionRules.Apply(commission, facts, Now());
        await SaveAsync(commission, lines);
        return new CommissionUpdate(commission, news);
    }

    public async Task<CommissionUpdate?> VerdictAsync(Guid id, string status, string? note)
    {
        if (Find(id) is not { } commission) return null;
        var news = CommissionRules.Verdict(commission, status, note, Now());
        if (news != CommissionNews.None) await SaveAsync(commission);
        return new CommissionUpdate(commission, news);
    }

    private Commission? Find(Guid id) => state.State.Commissions.FirstOrDefault(c => c.Id == id);

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private async Task SaveAsync(Commission commission, List<WorksLine>? lines = null)
    {
        await state.WriteStateAsync();
        await this.GetStreamProvider(PartyTown.Services.Streaming.PartyStreamIds.Provider).PublishWorldAsync(this.GetPrimaryKey(), "works",
            new { commissionId = commission.Id, status = commission.Status.ToString(), lines = lines ?? new List<WorksLine>() });
    }
}

[Alias("PartyTown.World.Work.Works.ICommissionsGrain")]
public interface ICommissionsGrain : IGrainWithGuidKey
{
    [Alias("GetAllAsync")] Task<List<Commission>> GetAllAsync();
    [Alias("GetAsync")] Task<Commission?> GetAsync(Guid id);
    /// <summary>Null when a change is already open: there's one builder and one line of work.</summary>
    [Alias("FileAsync")] Task<Commission?> FileAsync(Commission commission);
    [Alias("StartedAsync")] Task<Commission?> StartedAsync(Guid id, string runId);
    [Alias("MarkAsync")] Task<Commission?> MarkAsync(Guid id, CommissionStatus status, string? error = null);
    /// <summary>Takes in the Runner's read of a run, and any new transcript lines for spectators.</summary>
    [Alias("SyncAsync")] Task<CommissionUpdate?> SyncAsync(Guid id, CommissionFacts facts, List<WorksLine> lines);
    [Alias("VerdictAsync")] Task<CommissionUpdate?> VerdictAsync(Guid id, string status, string? note);
}

[GenerateSerializer, Alias("PartyTown.World.Work.Works.CommissionsState")]
public sealed class CommissionsState
{
    [Id(0)] public List<Commission> Commissions { get; set; } = new();
}
