using System.Text;
using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;

namespace PartyTown.Bench.Probes;

public static class DecisionProbes
{
    [Probe("Cold-open: a statement lands in a quiet room. Vlad (low chattiness) and Denise (high) each decide whether to break the silence. Vlad carries fake recollections to exercise the '# What you remember' block.")]
    public static async Task Decision_ColdOpen(Bench bench)
    {
        var history = BenchCast.ColdOpenHistory(
            "just got back from the worst offsite of my life, three hours of trust falls and we shipped nothing");
        bench.Observe("scenario", new { history[0].Content, totalAiRoundsInGroup = 0 });

        var vladMemories = new[]
        {
            "Sam once spent a whole evening ranting about a meeting that could have been an email.",
            "The last offsite Sam mentioned ended with someone crying by the trust-fall mat.",
        };

        await RunDecision(bench, BenchCast.Vlad, history, vladMemories);
        await RunDecision(bench, BenchCast.Denise, history);
    }

    [Probe("Direct mention: 'Vlad, you there?' drives urge≥0.9 → the auto-respond shortcut fires, returning canned text WITHOUT routing to a model. Artifact should show ZERO captured LLM calls — the pure math path.")]
    public static async Task Decision_DirectMention(Bench bench)
    {
        var history = new[]
        {
            new ChatMessage
            {
                MessageId = 1,
                Content = "Vlad, you there?",
                SenderType = "user",
                SenderId = BenchCast.SamId,
                ChatGroupId = BenchCast.RoomId,
            },
        };
        bench.Observe("scenario", new { history[0].Content });

        await RunDecision(bench, BenchCast.Vlad, history);
    }

    private static async Task RunDecision(
        Bench bench,
        SelfView self,
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<string>? recollections = null)
    {
        var service = new PersonaDecisionService(bench.Router, bench.Logger("PersonaDecisionService"));

        var urge = UrgeMath.CalculateResponseUrge(self, history, totalAiRoundsInGroup: 0);
        bench.Observe($"{self.Name}.urge (Total deterministic; ChaosScore re-rolls per call)", new
        {
            urge.Total,
            urge.MentionScore,
            urge.QuestionScore,
            urge.SilenceStreakScore,
            urge.ColdOpenScore,
            urge.ChaosScore,
        });

        var raw = new StringBuilder();
        var result = await service.ShouldRespondAsync(
            self,
            history,
            BenchCast.All,
            totalAiRoundsInGroup: 0,
            onEvent: (_, data, _) => { raw.Append(data); return Task.CompletedTask; },
            cancellationToken: bench.Cancellation,
            recollections: recollections);

        bench.Observe($"{self.Name}.decision", new
        {
            result.Respond,
            result.Instruction,
            result.Reason,
            result.MemoryToReference,
        });

        if (raw.Length > 0)
            bench.Observe($"{self.Name}.rawStream", raw.ToString());
    }
}
