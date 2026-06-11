---
name: bench
description: Run and author Bench probes — pointed runners that exercise LLM-driven subsystem slices (Decision/Speaking phase, prompt composition, salience, memory extraction) through the real grain path, headless, emitting Probe Artifacts to judge quality. Use when working on any LLM-driven feature or prompt change, when verifying persona behaviour without the UI, or when the user mentions bench, probes, or a bench session. If the bench cannot cover the task, stop and ask the user how to proceed instead of working around it silently.
---

# Bench

The Bench (`tools/bench`) runs **Probes**: plain C# methods that activate one subsystem
slice through the real router → endpoint grains and emit a **Probe Artifact** (console
render + `bench-runs/<probe>/<timestamp>.json`) instead of pass/fail. Probes observe;
assertions live in `backend-test/`. Design: `docs/adr/0011-bench-probe-runner.md`.
Vocabulary (Bench, Probe, Probe Artifact, Bench Session): `CONTEXT.md` § The Bench.

## Quick start

```bash
dotnet run --project tools/bench -- doctor       # ALWAYS run first
dotnet run --project tools/bench -- list         # discover probes
dotnet run --project tools/bench -- <ProbeName>  # run one
```

`doctor` verdicts:
- **"bench session LIVE"** — providers reachable; probes get real model output.
- **No providers reachable** — probes still capture composed prompts up to the routing
  point (tier-0). Prompt-composition work stays unblocked; quality judging does not.
- Watch the **complexity coverage** block: seeded providers default to `General` only.
  A probe routing `CharacterVoice`/`CharacterThoughts` (salience, emotes, race) fails
  until provider entries carry those flags — that is a bottleneck, see below.

## Workflow on a feature ticket

1. Run `doctor`. Note the verdict in your working notes.
2. Find an existing probe covering your slice (`list`), or write one (below).
3. Loop: edit code/prompt → run probe → read the artifact → judge the output yourself
   (you are the quality judge — read composed prompts and model output critically,
   in the persona's voice, against the ticket's intent). Diff JSON artifacts across
   iterations to see what your change did.
4. Probes are **instruments** (re-tuned slices like the decision phase — keep) or
   **scaffolding** (one feature's development aid — delete when the feature ships).
   Say which yours is when the ticket closes.

## Authoring a probe

```csharp
[Probe("One-line: what this exercises and what to look for")]
public static async Task Speaking_MemoryCallback(Bench bench)
{
    var logger = bench.LoggerFactory.CreateLogger("probe");
    // build cast (GenerationParticipant) + history (ChatMessage), call the real
    // service with bench.Router, accumulate streams via onEvent
    bench.Observe("vlad.decision", result);   // anything worth seeing in the artifact
}
```

Composed prompts are captured automatically (grain-call filter) — never refactor
production services to expose prompt internals for a probe.

Traps:
- **Auto-respond shortcut eats the LLM call**: urge ≥ 0.9 skips the decision model.
  Cold-open (0.5) + trailing "?" (0.6) or a persona-name mention (1.0) triggers it.
  Want the LLM path? Statement, no names. Want the math path? Mention a name.
- `IMemoryRepository` is stubbed; memory probes need the real one + Postgres (bottleneck).
- Orleans grain args/returns: no collection expressions (`[]`, `[.. x]`) — CLAUDE.md.
- `onEvent` receives streaming chunks AND the final `done=true` payload — filter `done`
  when accumulating raw streams or the artifact shows the result twice.

## Bottleneck protocol

When the bench cannot do what the task needs — missing capability (complexity tier,
real memory repo, full Room loop), missing probe shape, or friction bad enough that
the feedback loop is gone — **stop and ask the user**. Do not silently hack around it,
and do not silently grow the bench's scope. Present the situation as options:

> Bench bottleneck: <what's missing, one line>. Options:
> a. **Improve bench** — <concrete extension + rough effort, e.g. "complexity flags in
>    benchsettings, ~30 min">
> b. **Use what's available** — <the degraded loop, e.g. "prompt-capture only; I judge
>    composed prompts, you spot-check behaviour in the UI">
> c. **Skip bench for this task type** — fall back to backend-test / full stack.
> Recommend: <your pick + why>.

Apply the same protocol in reverse: if you catch yourself building bench machinery the
ticket didn't ask for, stop and ask.

## Bench Sessions

Real model output requires a **Bench Session**: the user deliberately set up providers
(`tools/bench/benchsettings.json`, usually local Ollama) and told you the bench is
live. Never assume one; `doctor` is the ground truth. Without a session, work tier-0
and say so in your report.
