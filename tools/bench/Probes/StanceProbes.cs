using System.Text;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.Services.ResponsePipeline;

namespace PartyTown.Bench.Probes;

public static class StanceProbes
{
    [Probe("Where you stand: Vlad appraises a cold-open twice — once carrying intrinsic Stances (a monotone line toward Sam + an ambivalent fold toward Denise), once with none. Read the composed prompt for the '# Where you stand' block and the contrast fold, then judge whether the WITH-stance reasoning/voice takes colour the WITHOUT-stance run lacks.")]
    public static async Task Stance_WhereYouStand(Bench bench)
    {
        var history = BenchCast.ColdOpenHistory(
            "i keep showing up to this group chat and i honestly couldn't tell you why anymore");
        bench.Observe("scenario", new { history[0].Content });

        var stances = new[]
        {
            new StanceLine(
                Reasoning: "Sam is the only mortal you've let close in a century — their weariness is yours",
                Valence: 0.85),
            new StanceLine(
                Reasoning: "Denise's relentless cheer wears on you",
                Valence: -0.4,
                Contrast: "you used to envy how easily she fills a silence"),
        };

        var stanceLines = stances.Select(StanceBlock.FormatLine).ToList();
        bench.Observe("vlad.stanceLines (FormatLine output → fed as '# Where you stand')", stanceLines);
        bench.Observe("vlad.stanceBlock (rendered block, for eyeballing the fold)",
            StanceBlock.Render(stanceLines));

        await RunDecision(bench, BenchCast.Vlad, history, stanceLines, label: "withStance");
        await RunDecision(bench, BenchCast.Vlad, history, stances: null, label: "noStance");
    }

    [Probe("Full #91 loop (real AGE): Vlad acquires a Stance toward Denise in party A, the curator promotes it to Intrinsic, and in a fresh party B — where Denise herself speaks the very cheer the Stance is about — the union read surfaces it and colours Vlad's live appraisal of HER line. Read partyB.recalledStances (did it travel?) and the withIntrinsic-vs-noStance decision diff (does the travelled weariness actually bend his read of Denise?).", RequiresMemory = true)]
    public static async Task Stance_IntrinsicTravelsToNewParty(Bench bench)
    {
        var repo = bench.Memory;
        var ct = bench.Cancellation;

        var partyA = Guid.NewGuid();
        var partyB = Guid.NewGuid();
        bench.Observe("scope", new { partyA, partyB, vlad = BenchCast.VladId, denise = BenchCast.DeniseId });

        var acquiredId = await repo.AppendStanceAsync(
            partyA,
            BenchCast.VladId,
            new StanceTargetSpec(StanceTargetKind.Participant, BenchCast.DeniseId, null, null),
            valence: -0.6,
            reasoning: "Denise's relentless cheer wore you down across a long night in that first room",
            attribution: null,
            ct);
        bench.Observe("partyA.acquiredStanceId", acquiredId);

        var intrinsicId = await repo.PromoteStanceAsync(partyA, BenchCast.VladId, acquiredId, ct);
        bench.Observe("promotedIntrinsicStanceId", intrinsicId);

        var recalled = await repo.RecallStancesAsync(
            partyB,
            BenchCast.VladId,
            new[] { BenchCast.VladId, BenchCast.DeniseId },
            anchorText: "",
            limit: 10,
            ct);
        bench.Observe("partyB.recalledStances",
            recalled.Select(l => new { l.Reasoning, l.Valence, l.Contrast }).ToList());

        if (recalled.Count == 0)
            bench.Observe("WARNING",
                "no Stances recalled in party B — promotion or the cross-Party union read did not travel; "
                + "the decision below has nothing to colour and the loop is broken.");

        var stanceLines = recalled.Select(StanceBlock.FormatLine).ToList();
        bench.Observe("partyB.stanceBlock (rendered '# Where you stand')", StanceBlock.Render(stanceLines));

        var history = BenchCast.PersonaSays(
            BenchCast.DeniseId,
            "okay okay NEW room, fresh start, i love it!! who's bringing the energy today — let's actually make something happen!");
        bench.Observe("partyB.scenario (Denise — the Stance's target — speaking)", new { history[0].Content });

        await RunDecision(bench, BenchCast.Vlad, history, stanceLines, label: "partyB.withIntrinsic");
        await RunDecision(bench, BenchCast.Vlad, history, stances: null, label: "partyB.noStance");
    }

    private static async Task RunDecision(
        Bench bench,
        SelfView self,
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<string>? stances,
        string label)
    {
        var service = new PersonaDecisionService(bench.Router, bench.Logger("PersonaDecisionService"));

        var urge = UrgeMath.CalculateResponseUrge(self, history, totalAiRoundsInGroup: 0);
        bench.Observe($"{label}.{self.Name}.urge", new { urge.Total, urge.ColdOpenScore, urge.ChaosScore });

        var raw = new StringBuilder();
        var result = await service.ShouldRespondAsync(
            self,
            history,
            BenchCast.All,
            totalAiRoundsInGroup: 0,
            onEvent: (_, data, _) => { raw.Append(data); return Task.CompletedTask; },
            cancellationToken: bench.Cancellation,
            recollections: null,
            stances: stances);

        bench.Observe($"{label}.{self.Name}.decision", new
        {
            result.Respond,
            result.Instruction,
            result.Reason,
        });

        if (raw.Length > 0)
            bench.Observe($"{label}.{self.Name}.rawStream", raw.ToString());
    }
}
