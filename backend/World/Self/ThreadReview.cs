using PartyTown.Model;
using PartyTown.Services.SystemOne;

namespace PartyTown.World.Self;

/// <summary>Rest decides what a Life thread came to (ADR 0052): System One reads what they remember against each thread and the Preoccupation.</summary>
public sealed class ThreadReview(IGrainFactory grains, ISystemOneJudge judge, WorldMemory memory, ILogger<ThreadReview> logger)
{
    public const double Closed = 0.7;
    public static readonly TimeSpan Every = TimeSpan.FromHours(3);
    private const int Remembered = 8;

    public async Task ReviewAsync(Guid partyId, CastMember self, IReadOnlyList<Guid> cast, CancellationToken ct)
    {
        var grain = grains.GetGrain<IPersonaSelfGrain>(WorldKeys.Life(partyId, self.Id));
        try
        {
            var day = await grain.GetStateAsync();
            var open = Open(day.Threads, day.Preoccupation);
            if (open.Count == 0 || !await grain.TryReviewThreadsAsync()) return;
            var (memories, _) = await memory.RecallAsync(partyId, self.Id, cast, string.Join(" ", open), ct, Remembered);
            var (settled, contradicted) = await JudgeAsync(judge.For(self.Id), self.Name, memories.Select(m => m.Snippet).ToList(), day.Threads, day.Preoccupation, ct);
            if (settled.Count == 0 && contradicted.Count == 0) return;
            await grain.CloseThreadsAsync(settled, contradicted);
            logger.LogInformation("Thread review for {Name}: settled [{Settled}], contradicted [{Contradicted}]", self.Name, string.Join("; ", settled), string.Join("; ", contradicted));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Thread review failed for {Name}", self.Name);
        }
    }

    /// <summary>Threads, then the Preoccupation: what a review reads.</summary>
    public static List<string> Open(IEnumerable<string> threads, string? preoccupation)
        => threads.Append(preoccupation ?? string.Empty).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();

    /// <summary>Settled and contradicted ones, by what they remember; nothing remembered or no answer leaves all open.</summary>
    public static async Task<(List<string> Settled, List<string> Contradicted)> JudgeAsync(
        ISystemOneJudge judge, string name, IReadOnlyList<string> memories, IReadOnlyList<string> threads, string? preoccupation, CancellationToken ct)
    {
        var (settled, contradicted) = (new List<string>(), new List<string>());
        var open = Open(threads, preoccupation);
        if (open.Count == 0 || memories.Count == 0) return (settled, contradicted);
        var state = $"{name}.\nWhat they remember lately:\n{string.Join("\n", memories.Select(m => $"- {m}"))}";
        var questions = new Dictionary<string, SystemOneQuestion>();
        for (var i = 0; i < open.Count; i++)
        {
            questions[$"settled{i}"] = new NoulQuestion($"By what {name} remembers, this is done or settled now: {open[i]}");
            questions[$"contradicted{i}"] = new NoulQuestion($"What {name} remembers shows this isn't so: the thing doesn't exist, can't happen here, or isn't theirs to do: {open[i]}");
        }
        var answers = await judge.AskAsync(state, questions, ct);
        for (var i = 0; i < open.Count; i++)
        {
            if (answers.Noul($"contradicted{i}", 0) >= Closed) contradicted.Add(open[i]);
            else if (answers.Noul($"settled{i}", 0) >= Closed) settled.Add(open[i]);
        }
        return (settled, contradicted);
    }
}
