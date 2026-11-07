using PartyTown.Services.Streaming;

namespace PartyTown.World.Work.Cdu;

/// <summary>One thing a Cytheurgist said to the CDU, and what it said back (ADR 0039).</summary>
[GenerateSerializer, Alias("PartyTown.World.Work.Cdu.CduExchange")]
public sealed class CduExchange
{
    [Id(0)] public string TurnId { get; set; } = string.Empty;
    [Id(1)] public Guid CompanyId { get; set; }
    [Id(2)] public Guid SpeakerId { get; set; }
    [Id(3)] public string SpeakerName { get; set; } = string.Empty;
    /// <summary>talk or answer.</summary>
    [Id(4)] public string Kind { get; set; } = CduRules.Talk;
    [Id(5)] public string Text { get; set; } = string.Empty;
    /// <summary>queued, done or failed.</summary>
    [Id(6)] public string Status { get; set; } = CduRules.Queued;
    /// <summary>Its words, not a fact.</summary>
    [Id(7)] public string? Reply { get; set; }
    [Id(8)] public bool Asked { get; set; }
    /// <summary>Someone answered its question.</summary>
    [Id(9)] public bool Answered { get; set; }
    /// <summary>Commits it made on the trunk while answering, from git.</summary>
    [Id(10)] public int Commits { get; set; }
    [Id(11)] public long At { get; set; }
    [Id(12)] public long? DoneAt { get; set; }
    /// <summary>The mind it was woken with, as the speaker chose.</summary>
    [Id(13)] public string? Model { get; set; }
}

/// <summary>A piece of the CDU's news, flattened so it can cross into a grain.</summary>
[GenerateSerializer, Alias("PartyTown.World.Work.Cdu.CduNews")]
public sealed record CduNews(
    [property: Id(0)] long N,
    [property: Id(1)] string Kind,
    [property: Id(2)] string Text,
    [property: Id(3)] string? TurnId = null,
    [property: Id(4)] int? Number = null,
    [property: Id(5)] bool Asked = false,
    [property: Id(6)] int Commits = 0,
    [property: Id(7)] bool Opened = false);

/// <summary>One transcript line for the spectators' window.</summary>
[GenerateSerializer, Alias("PartyTown.World.Work.Cdu.CduLine")]
public sealed record CduLine(
    [property: Id(0)] long N,
    [property: Id(1)] long At,
    [property: Id(2)] string Kind,
    [property: Id(3)] string Text);

/// <summary>Word for staff: one person (<see cref="Only"/>) or all; a verdict moves their mood.</summary>
[GenerateSerializer, Alias("PartyTown.World.Work.Cdu.CduTold")]
public sealed record CduTold(
    [property: Id(0)] string Kind,
    [property: Id(1)] string Text,
    [property: Id(2)] Guid? Only = null,
    [property: Id(3)] Guid? CompanyId = null,
    [property: Id(4)] bool? Lifts = null);

[GenerateSerializer]
public enum CduShift
{
    None = 0,
    /// <summary>Someone on staff came on shift: the tide comes in.</summary>
    Began = 1,
    /// <summary>The last one went off shift: the tide goes out.</summary>
    Ended = 2,
}

[GenerateSerializer, Alias("PartyTown.World.Work.Cdu.CduState")]
public sealed class CduState
{
    [Id(0)] public List<CduExchange> Exchanges { get; set; } = new();
    /// <summary>The last transcript line read.</summary>
    [Id(1)] public long Lines { get; set; }
    /// <summary>The last news read.</summary>
    [Id(2)] public long News { get; set; }
    /// <summary>Whether anyone on staff was on shift at the last beat.</summary>
    [Id(3)] public bool OnShift { get; set; }
}

/// <summary>Pure: how the CDU's news becomes the World's. Facts come from the CDU; nobody narrates their way forward.</summary>
public static class CduRules
{
    public const string Talk = "talk";
    public const string Answer = "answer";
    public const string Queued = "queued";
    public const string Done = "done";
    public const string Failed = "failed";
    public const int Kept = 50;
    private const int ToldMax = 600;

    /// <summary>The latest question nobody has answered yet, if the CDU's last word was one.</summary>
    public static CduExchange? OpenQuestion(IEnumerable<CduExchange> exchanges)
        => exchanges.LastOrDefault(e => e.Status == Done) is { Asked: true, Answered: false } last ? last : null;

    /// <summary>What this person said that the CDU hasn't answered yet.</summary>
    public static CduExchange? Pending(IEnumerable<CduExchange> exchanges, Guid who)
        => exchanges.LastOrDefault(e => e.SpeakerId == who && e.Status == Queued);

    public static void Spoke(CduState state, CduExchange exchange)
    {
        if (exchange.Kind == Answer && OpenQuestion(state.Exchanges) is { } question) question.Answered = true;
        state.Exchanges.Add(exchange);
        while (state.Exchanges.Count > Kept && state.Exchanges.FindIndex(e => e.Status != Queued) is >= 0 and var oldest) state.Exchanges.RemoveAt(oldest);
    }

    /// <summary>Takes in news past the cursor; returns what staff should be told, once each.</summary>
    public static List<CduTold> Apply(CduState state, IEnumerable<CduNews> news, long now)
    {
        var told = new List<CduTold>();
        foreach (var n in news.Where(n => n.N > state.News).OrderBy(n => n.N))
        {
            state.News = n.N;
            switch (n.Kind)
            {
                case "replied" or "failed":
                    if (state.Exchanges.FirstOrDefault(e => e.TurnId == n.TurnId) is not { } e) break;
                    (e.Status, e.Reply, e.Asked, e.Commits, e.DoneAt) = (n.Kind == "replied" ? Done : Failed, n.Kind == "replied" ? n.Text : null, n.Asked, n.Commits, now);
                    told.Add(new(n.Kind, Replied(e), e.SpeakerId, e.CompanyId));
                    break;
                case "pr":
                    told.Add(new(n.Kind, n.Opened
                        ? $"The tide went out: the trunk is in the Agora as PR #{n.Number}, {Commits(n.Commits)}."
                        : $"The tide went out again: PR #{n.Number} now holds {Commits(n.Commits)}."));
                    break;
                case "merged":
                    told.Add(new(n.Kind, $"The One merged PR #{n.Number}, \"{n.Text}\". His light reached the world.", Lifts: true));
                    break;
                case "closed":
                    told.Add(new(n.Kind, $"The One closed PR #{n.Number}, \"{n.Text}\", without merging. It sank: a drowned idea.", Lifts: false));
                    break;
                case "checks" when n.Text is "failing" or "passing":
                    told.Add(new(n.Kind, n.Text == "failing" ? $"The checks on PR #{n.Number} are failing." : $"The checks on PR #{n.Number} pass."));
                    break;
            }
        }
        return told;
    }

    /// <summary>A shift edge, from whether anyone on staff is on shift now and was at the last beat.</summary>
    public static CduShift Edge(bool was, bool now) => (was, now) switch
    {
        (false, true) => CduShift.Began,
        (true, false) => CduShift.Ended,
        _ => CduShift.None,
    };

    private static string Replied(CduExchange e)
    {
        if (e.Status == Failed) return "The CDU fell silent before it answered you.";
        var moved = e.Commits > 0 ? $", and moved the Forms: {Commits(e.Commits)} on the trunk" : string.Empty;
        var said = string.IsNullOrWhiteSpace(e.Reply) ? "it said nothing." : WorldStream.Clip(e.Reply, ToldMax);
        return $"The CDU answered you{moved}: {said}";
    }

    private static string Commits(int n) => n == 1 ? "1 commit" : $"{n} commits";
}

/// <summary>The Party's side of the CDU (key = Party id). Pure state: the CDU is reached from <see cref="Cytheurgy"/>.</summary>
public sealed class CduGrain(
    [PersistentState(stateName: "cdu", storageName: "parties")]
    IPersistentState<CduState> state)
    : Grain, ICduGrain
{
    public Task<CduState> GetAsync() => Task.FromResult(state.State);

    public async Task SpokeAsync(CduExchange exchange)
    {
        CduRules.Spoke(state.State, exchange);
        await state.WriteStateAsync();
        await PublishAsync(new { exchange = exchange.TurnId });
    }

    public async Task<List<CduTold>> HeardAsync(List<CduNews> news)
    {
        var told = CduRules.Apply(state.State, news, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await state.WriteStateAsync();
        if (told.Count > 0) await PublishAsync(new { news = told.Count });
        return told;
    }

    public async Task SawAsync(List<CduLine> lines)
    {
        if (lines.Count == 0) return;
        state.State.Lines = Math.Max(state.State.Lines, lines.Max(l => l.N));
        await state.WriteStateAsync();
        await PublishAsync(new { lines });
    }

    public async Task CatchUpAsync(long lines, long news)
    {
        (state.State.Lines, state.State.News) = (Math.Max(state.State.Lines, lines), Math.Max(state.State.News, news));
        await state.WriteStateAsync();
    }

    public async Task<CduShift> ShiftAsync(bool onShift)
    {
        var edge = CduRules.Edge(state.State.OnShift, onShift);
        if (edge == CduShift.None) return edge;
        state.State.OnShift = onShift;
        await state.WriteStateAsync();
        return edge;
    }

    private Task PublishAsync(object payload)
        => this.GetStreamProvider(PartyStreamIds.Provider).PublishWorldAsync(this.GetPrimaryKey(), CduScreen.AppId, payload);
}

[Alias("PartyTown.World.Work.Cdu.ICduGrain")]
public interface ICduGrain : IGrainWithGuidKey
{
    [Alias("GetAsync")] Task<CduState> GetAsync();
    [Alias("SpokeAsync")] Task SpokeAsync(CduExchange exchange);
    /// <summary>News past the cursor in; what staff should be told out.</summary>
    [Alias("HeardAsync")] Task<List<CduTold>> HeardAsync(List<CduNews> news);
    /// <summary>New transcript lines, for spectators.</summary>
    [Alias("SawAsync")] Task SawAsync(List<CduLine> lines);
    /// <summary>Moves both cursors to the CDU's present, telling nobody.</summary>
    [Alias("CatchUpAsync")] Task CatchUpAsync(long lines, long news);
    /// <summary>Whether anyone on staff is on shift now; returns the edge, if this is one.</summary>
    [Alias("ShiftAsync")] Task<CduShift> ShiftAsync(bool onShift);
}
