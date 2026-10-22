using PartyTown.Model;
using PartyTown.Services.SystemOne;

namespace PartyTown.World.Desires;

/// <summary>
/// Rest decides what wanting came to (ADR 0026), like Consolidation decides what moments meant: in the Diary,
/// System One reads what they remember against each active desire. Got it → fulfilled; closer → it stays on top.
/// </summary>
public sealed class DesireReview(IGrainFactory grains, ISystemOneJudge judge, WorldMemory memory, ILogger<DesireReview> logger)
{
    public const double Done = 0.7;
    public const double Progress = 0.6;

    public async Task ReviewAsync(Guid partyId, CastMember self, IReadOnlyList<Guid> cast, CancellationToken ct)
    {
        if (!judge.Enabled) return;
        var grain = grains.GetGrain<IDesiresGrain>(WorldKeys.Life(partyId, self.Id));
        try
        {
            var active = DesireRules.OnMind((await grain.GetAsync()).All);
            if (active.Count == 0 || !await grain.TryReviewAsync()) return;
            var (memories, _) = await memory.RecallAsync(partyId, self.Id, cast, string.Join(" ", active.Select(d => d.Text)), ct);
            var state = $"{self.Name}.\nWhat they remember lately:\n{string.Join("\n", memories.Take(8).Select(m => $"- {m.Snippet}"))}";
            var questions = new Dictionary<string, SystemOneQuestion>();
            for (var i = 0; i < active.Count; i++)
            {
                // Things are got by buying or being given them; only becoming is judged.
                if (active[i].ItemKey is null) questions[$"done{i}"] = new NoulQuestion($"{self.Name} has got what they wanted: {active[i].Text}");
                questions[$"closer{i}"] = new NoulQuestion($"Lately {self.Name} has moved closer to what they want: {active[i].Text}");
            }
            var answers = await judge.AskAsync(state, questions, ct);
            for (var i = 0; i < active.Count; i++)
            {
                var (done, closer) = (answers.Noul($"done{i}", 0), answers.Noul($"closer{i}", 0));
                if (Reviewed(done, closer) is not { } outcome) continue;
                if (outcome == DesireStatus.Fulfilled) await grain.MarkAsync(active[i].Id, DesireStatus.Fulfilled);
                else await grain.TouchAsync(active[i].Id);
                logger.LogInformation("Desire review for {Name}: \"{Desire}\" done={Done:F2} closer={Closer:F2}", self.Name, active[i].Text, done, closer);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Desire review failed for {Name}", self.Name);
        }
    }

    /// <summary>Fulfilled when clearly got, still active (touched) when closer, null when nothing moved.</summary>
    public static DesireStatus? Reviewed(double done, double closer)
        => done >= Done ? DesireStatus.Fulfilled : closer >= Progress ? DesireStatus.Active : null;
}
