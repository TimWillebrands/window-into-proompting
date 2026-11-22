using PartyTown.Services.Streaming;

namespace PartyTown.World.Work.Cdu;

[GenerateSerializer, Alias("PartyTown.World.Work.Cdu.CduExchange")]
public sealed class CduExchange
{
    [Id(0)] public string TurnId { get; set; } = string.Empty;
    [Id(1)] public Guid CompanyId { get; set; }
    [Id(2)] public Guid SpeakerId { get; set; }
    [Id(3)] public string SpeakerName { get; set; } = string.Empty;
    [Id(4)] public string Kind { get; set; } = CduRules.Talk;
    [Id(5)] public string Text { get; set; } = string.Empty;
    [Id(6)] public string Status { get; set; } = CduRules.Queued;
    [Id(7)] public string? Reply { get; set; }
    [Id(8)] public bool Asked { get; set; }
    [Id(9)] public bool Answered { get; set; }
    [Id(10)] public int Commits { get; set; }
    [Id(11)] public long At { get; set; }
    [Id(12)] public long? DoneAt { get; set; }
    [Id(13)] public string? Model { get; set; }
    [Id(14)] public double? Usd { get; set; }
}

[GenerateSerializer, Alias("PartyTown.World.Work.Cdu.CduNews")]
public sealed record CduNews(
    [property: Id(0)] long N,
    [property: Id(1)] string Kind,
    [property: Id(2)] string Text,
    [property: Id(3)] string? TurnId = null,
    [property: Id(4)] int? Number = null,
    [property: Id(5)] bool Asked = false,
    [property: Id(6)] int Commits = 0,
    [property: Id(7)] bool Opened = false,
    [property: Id(8)] double? Usd = null);

[GenerateSerializer, Alias("PartyTown.World.Work.Cdu.CduLine")]
public sealed record CduLine(
    [property: Id(0)] long N,
    [property: Id(1)] long At,
    [property: Id(2)] string Kind,
    [property: Id(3)] string Text);

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
    Began = 1,
    Ended = 2,
}

[GenerateSerializer, Alias("PartyTown.World.Work.Cdu.CduState")]
public sealed class CduState
{
    [Id(0)] public List<CduExchange> Exchanges { get; set; } = new();
    [Id(1)] public long Lines { get; set; }
    [Id(2)] public long News { get; set; }
    [Id(3)] public bool OnShift { get; set; }
}

public static class CduRules
{
    public const string Talk = "talk";
    public const string Answer = "answer";
    public const string Ask = "ask";
    public const string Queued = "queued";
    public const string Parked = "parked";
    public const string Done = "done";
    public const string Failed = "failed";
    public const int Kept = 50;
    private const int ToldMax = 1200;

    public static CduExchange? OpenQuestion(IEnumerable<CduExchange> exchanges)
        => exchanges.LastOrDefault(e => e.Status == Done) is { Asked: true, Answered: false } last ? last : null;

    public static CduExchange? Pending(IEnumerable<CduExchange> exchanges, Guid who)
        => exchanges.LastOrDefault(e => e.SpeakerId == who && e.Status == Queued);

    public static CduExchange? ParkedFor(IEnumerable<CduExchange> exchanges, Guid who)
        => exchanges.LastOrDefault(e => e.SpeakerId == who && e.Status == Parked);

    public static List<(Guid Who, string Name, double Usd)> Spend(IEnumerable<CduExchange> exchanges, long since)
        => exchanges.Where(e => e.Usd is > 0 && (e.DoneAt ?? e.At) >= since).GroupBy(e => e.SpeakerId)
            .Select(g => (g.Key, g.Last().SpeakerName, g.Sum(e => e.Usd ?? 0))).OrderByDescending(s => s.Item3).ToList();

    public static void Spoke(CduState state, CduExchange exchange)
    {
        state.Exchanges.RemoveAll(e => e.SpeakerId == exchange.SpeakerId && e.Status == Parked);
        if (exchange.Kind == Answer && OpenQuestion(state.Exchanges) is { } question) question.Answered = true;
        state.Exchanges.Add(exchange);
        while (state.Exchanges.Count > Kept && state.Exchanges.FindIndex(e => e.Status is not (Queued or Parked)) is >= 0 and var oldest) state.Exchanges.RemoveAt(oldest);
    }

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
                    (e.Status, e.Reply, e.Asked, e.Commits, e.DoneAt, e.Usd) = (n.Kind == "replied" ? Done : Failed, n.Kind == "replied" ? n.Text : null, n.Asked, n.Commits, now, n.Usd);
                    told.Add(new(n.Kind, Replied(e), e.SpeakerId, e.CompanyId));
                    break;
                case "pr":
                    told.Add(new(n.Kind, n.Opened
                        ? $"The tide went out: the trunk is in the Agora as PR #{n.Number}, {Commits(n.Commits)}."
                        : $"The tide went out again: PR #{n.Number} now holds {Commits(n.Commits)}."));
                    break;
                case "merged":
                    told.Add(new(n.Kind, $"The One merged PR #{n.Number}, \"{n.Text}\". His light reached the world: what was built at your desk is real here now, for everyone who lives under it.", Lifts: true));
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
        return $"The CDU answered {(e.Kind == Ask ? "your question" : "you")}{moved}: {said}";
    }

    private static string Commits(int n) => n == 1 ? "1 commit" : $"{n} commits";
}

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
    [Alias("HeardAsync")] Task<List<CduTold>> HeardAsync(List<CduNews> news);
    [Alias("SawAsync")] Task SawAsync(List<CduLine> lines);
    [Alias("CatchUpAsync")] Task CatchUpAsync(long lines, long news);
    [Alias("ShiftAsync")] Task<CduShift> ShiftAsync(bool onShift);
}
