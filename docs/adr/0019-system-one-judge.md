# System One judge: weigh with a calibrated decision model, voice with an LLM

> Status: accepted. Builds on ADR 0015 (recall as RR) and ADR 0018 (Initiative).

Every relevance-realization instance has a *weighing* part and sometimes a *voicing*
part. Interruption asks "is this worth dropping my draft for?". Capture asks "is this
worth remembering?". Initiative asks "which of these Affordances pulls hardest?". Only
the voicing ("what exactly do I say") needs generated text.

Until now both parts ran through one chat LLM. LLMs are System Two machinery:
deliberate, verbose, expensive, and uncalibrated when asked for a number. They also
collapse the opponent process into an argmax, because a single sampled completion
names a single winner.

## Decision

Introduce `ISystemOneJudge`, a port shaped like TypeSafe's Jev: give it a **state** and
**typed questions**, get **probabilities** back and no prose.

- `noul`: is this statement true → p.
- `choice`: which option → a distribution over the options.

There are two adapters:

| Adapter | When | `Enabled` |
| --- | --- | --- |
| `JevJudge` (`POST https://api.typesafe.ai/v1/systemone`) | `TYPESAFE_API_KEY` is set | always |
| `LlmJudge` (General tier, JSON of probabilities) | otherwise | only if `SYSTEM_ONE_EMULATE=true` |

When the judge is `Enabled`:

- **Initiative**: System One returns a distribution over the Affordances plus rest. Life
  **samples** from it at temperature 0.8 instead of taking the argmax. The second-best
  pull still wins sometimes. That is the opponent process kept alive, the
  explore ⇄ exploit tension that a single LLM completion flattens. Only the winning
  Affordance is then voiced by the LLM (thought + intent).
- **Capture gate**: before a lull Capture (two LLM calls), System One estimates
  p(someone would still remember this in a week). Spans scoring below 0.35 are dropped.

When it is not enabled, Initiative stays the single combined LLM call and Capture is
ungated. That is cheaper than emulating System One with an extra LLM call.

## Why a port, not a direct dependency

- Jev is closed-weight and in early access. The world must keep living without it.
- The seam names the cognitive distinction (weigh vs voice) in the code. New RR
  instances should ask "is this weighing or voicing?" first.
- Candidates for later migration: the stop-signal race salience call
  (`PersonaSalienceService`) and the Urge gate. Both are System One shaped already. They
  are not migrated yet, because they have tuned behaviour and tests that would need
  re-baselining against a calibrated model.

## Consequences

- With Jev, an Initiative turn costs one fast decision call plus one short LLM call,
  instead of one long LLM call. The decision is calibrated and sampled.
- The Jev adapter is tested against the documented wire format only, because no key
  was available when it was written. The first real run should be checked against the
  logs (`Capture gate p(memorable)=…`, `Life X → …`).

## Addendum: a local decision model first, Jev on OpenRouter as the fallback

Decision models are now a commodity. Jev is served by OpenRouter (`POST /api/v1/systemone`,
`~typesafe/jev-latest`), and llama.cpp serves open decision models on the same
`/v1/systemone` wire format, for example Cloudflare's **Clef-Flash** (a 9B model; the Q4_K_M
build is 6.5 GB). That changes where compute should go:

- **Local GPU → System One.** Decisions are short, high-frequency and latency-sensitive: one
  forward pass, no generation. That is the best use of a consumer GPU.
- **OpenRouter → System Two, with stronger models.** Voicing is infrequent and is where model
  quality shows. The Speaking phase (`CharacterVoice` tier) now runs on `anthropic/claude-haiku-5.5`.
  Decision and Capture extraction stay on a cheap fast model.

`FailoverJudge` tries the judges in order: `SYSTEM_ONE_LOCAL_URL` (for example llama-server on :8091),
then `TYPESAFE_API_KEY`, then Jev via `OPENROUTER_API_KEY` (on by default; turn it off with
`SYSTEM_ONE_OPENROUTER=false`). A judge that fails sits out for 5 min.

System One now weighs three RR instances:

| Instance | Question | Use |
| --- | --- | --- |
| Initiative | `choice` over Affordances | sampled at temperature 0.8 |
| Interruption | `noul` "finishing now would look out of touch" + `choice` kind | the stop-signal race salience |
| Capture | `noul` "remembered in a week" | gate below 0.35 |
