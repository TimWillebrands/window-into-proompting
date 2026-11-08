# System One through the provider layer

> Status: accepted. Replaces the judge chain in ADR 0019's addendum. Builds on ADR 0032 and ADR 0033. Vocabulary in `CONTEXT.md`.

System One's judges were an env-configured chain beside the providers. A local Clef couldn't use a Tailnet, changing a
judge meant a redeploy, and decisions never showed up in Control Panel or AI observability.

## Decision

1. **System One is the Decisions tier.** A provider that handles Decisions answers `/systemone`. URL, key, model, Tailnet,
   enabled and priority work as they do for generation.
2. **The router picks and the endpoint calls.** `ProviderJudge` routes on the tier and asks the endpoint grain. A failing
   endpoint benches itself, so routing again finds the next provider within the same call.
3. **Any judge failure benches, not only hard ones.** Decisions sit on the beat path, so a slow or broken provider would
   cost latency on every beat. A bench covers the whole entry, so decision models get their own entries.
4. **Decisions don't queue behind generations.** Endpoint grains aren't reentrant, so `JudgeAsync` and `PressureAsync` are
   `[AlwaysInterleave]`. Without this, routing a decision waits on any generation in progress.
5. **`Enabled` stays synchronous.** Beat-path callers read it in about 14 places, so `ProviderJudge` caches it, refreshes
   every 30 seconds, and answers before the World starts. Disabling the last Decisions provider takes up to 30 seconds.

   *Superseded by ADR 0044:* `Enabled` is gone. `ProviderJudge` still refreshes whether it has a pool every 30 seconds.
6. **Cloud judges seed once.** `TYPESAFE_API_KEY` and `OPENROUTER_API_KEY` become Decisions providers on first boot, in
   the old order. Worlds seeded earlier get this too, or Jev would silently stop. After that Control Panel owns them, so
   a deleted one stays deleted and those variables are ignored.
7. **A local decision model has no wiring of its own.** It's a provider with Decisions ticked. `SYSTEM_ONE_LOCAL_URL` is
   gone, and model discovery falls back to `/v1/models`, so any compatible server lists its models.
8. **A Persona Route can carry decisions.** Persona Models has a Decisions row, like any other tier. Calls about one
   Persona (`judge.For(persona)`) use it, then the pool if it's benched or fails. A Route to a Reserved model enables
   System One for that Persona even when the pool has no decision model.

## What stays out

- **Calls about a group or a place** use the pool: Thermostat, Lull capture, Moment appraisal, Places tone and naming.
- **LLM emulation** stays the fallback when no Decisions provider exists (`SYSTEM_ONE_EMULATE`).

  *Amended by ADR 0044:* emulation is always the floor; `SYSTEM_ONE_EMULATE` is gone.
- **The Bench's Reflex calibration probe** keeps calling Jev directly through `HttpSystemOneJudge`.

## Cost

Decisions are now captured as AI generations, so PostHog events rise with the beat rate. Spend on a shared OpenRouter key
already showed in Control Panel usage.
