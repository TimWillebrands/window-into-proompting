using System.Text;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.Services.ResponsePipeline;

namespace PartyTown.Bench.Probes;

/// <summary>
/// Probes over the ambient "# Where you stand" block (ADR 0016, issue #91), in two tiers.
/// <para>
/// <see cref="Stance_WhereYouStand"/> is the Docker-free render half: it constructs
/// <see cref="StanceLine"/> values directly — exactly the shape <c>RecallStancesAsync</c> returns —
/// formats them through the real <see cref="StanceBlock"/>, and runs a cold-open twice (with vs.
/// without stances) so the artifact diff isolates the Stance's effect on the decision. It never
/// touches the graph, so it runs in tier-0.
/// </para>
/// <para>
/// <see cref="Stance_IntrinsicTravelsToNewParty"/> (<c>RequiresMemory</c>) closes the loop against a
/// real <see cref="MemoryRepository"/> over the bench's ephemeral AGE: it actually writes, promotes
/// and re-targets a Stance, then reads it back across Parties before colouring the decision —
/// exercising the union-read / Participant→Persona promotion that the render half can only mimic.
/// </para>
/// </summary>
public static class StanceProbes
{
    [Probe("Where you stand: Vlad appraises a cold-open twice — once carrying intrinsic Stances (a monotone line toward Sam + an ambivalent fold toward Denise), once with none. Read the composed prompt for the '# Where you stand' block and the contrast fold, then judge whether the WITH-stance reasoning/voice takes colour the WITHOUT-stance run lacks.")]
    public static async Task Stance_WhereYouStand(Bench bench)
    {
        // A statement (no '?', no persona names) → urge stays below the 0.9 auto-respond shortcut,
        // so the real decision model runs and the Stance block can colour it (see ADR 0011 trap).
        var history = BenchCast.ColdOpenHistory(
            "i keep showing up to this group chat and i honestly couldn't tell you why anymore");
        bench.Observe("scenario", new { history[0].Content });

        // The lines recall would surface for Vlad this beat. A monotone Stance renders its reasoning
        // verbatim; the Denise line carries a Contrast so StanceBlock folds it into one playable
        // tension ("X — though you used to feel otherwise: Y") rather than two competing entries.
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

        // Mirror production exactly: ResponsePipeline maps recall's StanceLines through FormatLine
        // before handing the strings to the decision service.
        var stanceLines = stances.Select(StanceBlock.FormatLine).ToList();
        bench.Observe("vlad.stanceLines (FormatLine output → fed as '# Where you stand')", stanceLines);
        bench.Observe("vlad.stanceBlock (rendered block, for eyeballing the fold)",
            StanceBlock.Render(stanceLines));

        await RunDecision(bench, BenchCast.Vlad, history, stanceLines, label: "withStance");
        await RunDecision(bench, BenchCast.Vlad, history, stances: null, label: "noStance");
    }

    /// <summary>
    /// The #91 full loop end-to-end against a real <see cref="MemoryRepository"/> over the bench's
    /// ephemeral AGE (ADR 0011 amendment): Vlad acquires a Stance toward Denise in one Party, the
    /// curator promotes it to Intrinsic (Participant→Persona re-target), and in a *second* Party the
    /// union read surfaces it — which then colours a live decision. This is the slice nothing else
    /// reaches: the stub-DB probes never recall, and <c>IntrinsicStanceIntegrationTest</c> hits real
    /// AGE but with a NullExtractor, so no model ever reacts. Read the recalled lines and the WITH vs
    /// WITHOUT decision contrast to judge whether the travelled Stance actually lands.
    /// </summary>
    [Probe("Full #91 loop (real AGE): Vlad acquires a Stance toward Denise in party A, the curator promotes it to Intrinsic, and in a fresh party B — where Denise herself speaks the very cheer the Stance is about — the union read surfaces it and colours Vlad's live appraisal of HER line. Read partyB.recalledStances (did it travel?) and the withIntrinsic-vs-noStance decision diff (does the travelled weariness actually bend his read of Denise?).", RequiresMemory = true)]
    public static async Task Stance_IntrinsicTravelsToNewParty(Bench bench)
    {
        var repo = bench.Memory;
        var ct = bench.Cancellation;

        // Fresh Party scopes per run keep the artifact clean against an accumulating graph if the
        // container is ever reused (WithReuse); the cast Persona ids stay fixed for diffable output.
        var partyA = Guid.NewGuid();
        var partyB = Guid.NewGuid();
        bench.Observe("scope", new { partyA, partyB, vlad = BenchCast.VladId, denise = BenchCast.DeniseId });

        // 1. Party A — Vlad acquires a Stance toward Denise (a Participant-scoped STANCE edge).
        var acquiredId = await repo.AppendStanceAsync(
            partyA,
            BenchCast.VladId,
            new StanceTargetSpec(StanceTargetKind.Participant, BenchCast.DeniseId, null, null),
            valence: -0.6,
            reasoning: "Denise's relentless cheer wore you down across a long night in that first room",
            attribution: null,
            ct);
        bench.Observe("partyA.acquiredStanceId", acquiredId);

        // 2. Promote → re-points the target from this Party's Participant to Denise's underlying
        // Persona, writing a Persona-scope edge that travels into every Party Vlad joins.
        var intrinsicId = await repo.PromoteStanceAsync(partyA, BenchCast.VladId, acquiredId, ct);
        bench.Observe("promotedIntrinsicStanceId", intrinsicId);

        // 3. Party B — a fresh room Vlad has no acquired history in; only the Intrinsic edge can
        // reach here, and only because Denise is present.
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

        // 4. Render the travelled Stance and let it colour a live appraisal — but of a beat the
        // Stance actually bears on: Denise (its target) speaking her relentless cheer. A neutral
        // user cold-open would leave the Denise-targeted line with nothing to grip, so the diff
        // would only prove the prompt carried the block — not that it bent Vlad's read. Her line
        // names no persona (no "Vlad") and is an "assistant" turn, so urge stays below the 0.9
        // auto-respond shortcut and the decision model still runs (ADR 0011 trap).
        var stanceLines = recalled.Select(StanceBlock.FormatLine).ToList();
        bench.Observe("partyB.stanceBlock (rendered '# Where you stand')", StanceBlock.Render(stanceLines));

        var history = BenchCast.PersonaSays(
            BenchCast.DeniseId,
            "okay okay NEW room, fresh start, i love it!! who's bringing the energy today — let's actually make something happen!");
        bench.Observe("partyB.scenario (Denise — the Stance's target — speaking)", new { history[0].Content });

        await RunDecision(bench, BenchCast.Vlad, history, stanceLines, label: "partyB.withIntrinsic");
        await RunDecision(bench, BenchCast.Vlad, history, stances: null, label: "partyB.noStance");
    }

    /// <summary>Runs one appraisal with an explicit Stance set and records the urge, the parsed
    /// decision, and the raw stream under a label so the with/without runs diff cleanly. The
    /// composed decision prompt (incl. the "# Where you stand" block) is captured automatically.</summary>
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
