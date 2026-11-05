# Persona Models: a Persona's work can go to its own provider

> Status: accepted. Builds on ADR 0015 (tier fallback) and 0018 (World Apps). Vocabulary in `CONTEXT.md`.

Providers were one shared pool: a tier went to whichever provider handled it. An author could not say "Vlad speaks
through the expensive model and everyone else stays cheap".

## Decision

1. **A Persona Route is (Persona, tier, provider).** Routes are per tier, so one Persona can keep the shared pool for
   thinking and use their own model for speaking. A tier without a route uses the pool as before.
2. **Routes live in the provider config grain, not on the Persona.** They cost money, so they are admin-only like
   providers, and removing a provider drops its routes in the same write. The router keeps one source and one cache.
3. **Reserved providers stay out of the pool.** Without this, adding a model for one Persona sends everyone to it.
   "Handles" says what the pool may send a provider, so it doesn't apply to a Reserved one, and a Route may target any
   provider at any tier.
4. **Identity is passed, not ambient.** `IWorldLlm.For(persona)` and `LlmCall`'s persona argument. Orleans
   `RequestContext` would need less editing, but it flows through the Room fan-out, so one Persona's identity would
   reach work done for another.
5. **A Route never mutes anyone.** A disabled or benched provider falls back to the pool, and the UI says so.

## What counts as a Persona's work

Routed: the Response pipeline, Life, App acts in a Persona's voice, the day and lived sheet, and stance consolidation.

Not routed: System One judges (the Decisions tier, mostly used with no Persona; see ADR 0034), memory extraction (one call covers several
Personas), Session homework (reads two people), Places design and art, the World Clock narrator.
