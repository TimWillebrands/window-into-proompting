# The Bench: probe runners with non-boolean outputs

> Status: accepted.

Development on Proompting's LLM-driven features (the **Response pipeline**, memory
extraction, salience scoring) had no quality feedback loop short of starting the full
stack — Aspire, Postgres+AGE, the frontend — and eyeballing the thought-log. For a
human that is slow; for a coding agent it is a dead end: the agent edits
prompt-composition code and never sees what the prompt became, let alone what a model
does with it.

The **Bench** closes that loop. It is a headless console host (`tools/bench`) that runs
**Probes**: pointed runners that activate one subsystem slice — a service, a grain, or
a multi-grain interaction — through the *real* code path, and emit a structured
**Probe Artifact** instead of a pass/fail verdict. A probe is a test with non-boolean
output: it observes, it does not assert.

The vocabulary (**Bench**, **Probe**, **Bench Session**, **Probe Artifact**) is
canonised in [`CONTEXT.md`](../../CONTEXT.md). This ADR records the shape and the
reasoning.

## Decisions

### Probes, not tests

A probe is a plain C# static method marked `[Probe]`, taking a `Bench` context. It
builds a cast and history with normal code, invokes the subsystem under design, and
returns. No asserts required. The output is the artifact: composed prompts, urge
breakdowns, parsed decisions, raw model output, attribution, latency.

Why not xunit: the deliverable of LLM-feature work is *quality*, which a boolean
cannot carry. Wrapping observation runs in a test framework invites pass/fail theater
(trivially-green tests) and buries the artifact in test logs. The unit/TestCluster
suite in `backend-test/` remains the home for mechanism assertions; the bench is the
home for observation.

### Dogfood the real LLM grain path

Probes route through the real `ILlmRouterGrain` → `ILlmEndpointGrain` (Ollama,
OpenRouter — whatever the session configured). No scripted fake in the bench's hot
path. Consequences:

- Probe output is real model behaviour, judgeable for quality.
- Routing, pressure tracking, and provider config get incidental exercise on every
  probe run.
- The bench is useless without a reachable provider — by design (see Bench Session).

### Composition root swap via DI, not parallel code

The bench host reuses the backend's registrations and swaps only the edges:

- **Provider config source**: the same polymorphic `LlmOptions` binding
  (`AddLlmProviderOptions`, shared with the backend's `Program.cs`) reads
  `benchsettings.json` instead of the backend's appsettings. `LlmProviderConfigGrain`
  seeds from it exactly as in production.
- **Grain storage**: in-memory (localhost clustering, off-default Orleans ports so the
  bench coexists with a running Aspire stack). Bench state is ephemeral; every run
  seeds fresh.
- **Memory subsystem**: `IMemoryRepository` is stubbed by default (PersonaGrain hangs
  silently without a registration). Probes that target memory swap in the real one.

Everything between those edges — config grain, router, endpoint grains, decision/
speaking/salience/emote services, prompt composition — is production code, untouched.

### Prompt capture via grain-call filter, not service refactor

Prompt composition is private to the services (by design). Rather than exposing prompt
builders, the bench installs an `IIncomingGrainCallFilter` on the silo that records
every `LlmGenerationJob` flowing into any `ILlmEndpointGrain` — the composed messages,
the response-format schema, the job complexity, timing. Every probe gets exact-prompt
visibility for free, for every subsystem, with zero changes to production code.

This also gives the bench a degraded-but-useful tier-0 mode: when no provider is
reachable, the prompts are still composed and captured up to the point of routing, so
prompt-composition work keeps a feedback loop with no model at all.

### Bench Sessions are deliberate

Probing needs a model; models need setup (Ollama running, models pulled, or an API
key). A **Bench Session** is an intentional development mode: the driver-user starts
providers, edits `benchsettings.json` if needed, runs `bench doctor`, and tells the
agent the bench is live. `doctor` verifies each configured provider end-to-end through
the real router and reports what is reachable. Agents must not assume a session exists;
absent one, they fall back to prompt-capture-only runs or the assertion suite.

### Artifacts are files, runs are comparable

Every probe run renders to the console (for the human) and writes
`bench-runs/<probe>/<timestamp>.json` (for the agent and for diffing across
iterations). One artifact schema for all probes: scenario inputs, captured LLM calls
(prompts in, raw text out), per-step observations, attribution, timing. `bench-runs/`
is gitignored — artifacts are working material, not fixtures.

## Consequences

- **The agent loop becomes**: edit code → `dotnet run --project tools/bench -- <Probe>`
  → read artifact → judge → iterate. Seconds, no Aspire, no browser, no clicks.
- **Scenario material lives in C#** (casts, histories) close to the probes — normal
  code, refactorable, type-checked against the real models. As the library grows it
  becomes the seed corpus for a future eval harness (run N reps, score with rubrics);
  that harness is explicitly out of scope here and needs no rework of the bench to add.
- **Probes rot like code, not like docs**: they compile against production signatures,
  so signature drift breaks the build, not silently the probe.
- **Nondeterminism is recorded, not eliminated.** Chaos scoring (`Random.Shared`),
  clocks, and model output stay nondeterministic; artifacts capture the values that
  occurred (urge breakdown includes the rolled chaos score). If reproducibility
  pressure grows, injecting `TimeProvider`/seeded `Random` is a later, separate
  decision.
- **Two hosts share a composition seam.** The `LlmOptions` binding is the first shared
  registration extension; as bench probes reach deeper (full Room loops), more backend
  registrations should migrate into shared extensions rather than being duplicated in
  the bench host.

## Escape hatches

- **Scripted provider**: if zero-model determinism becomes necessary (CI, plumbing
  probes), add a `"scripted"` provider type behind `ILlmEndpointGrain` and register it
  in `benchsettings.json` like any provider. The router switch in `LlmRouterGrain`
  gains one case; nothing else changes.
- **Eval tier**: when prompt quality needs scoring rather than reading, add a runner
  that executes existing probes N times and applies a rubric (heuristic or LLM-judge)
  over the artifacts. The artifact schema is the contract; keep it stable.
