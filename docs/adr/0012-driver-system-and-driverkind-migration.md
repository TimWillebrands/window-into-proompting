# Driver.System kind and IsUser → DriverKind storage migration

> Status: accepted.

A third **Driver** kind, `System`, is introduced for the **Narrator** Persona — a singleton library Persona that joins every Party as a Participant to own ambient narration ("Vlad enters the room"). The **Response pipeline** never auto-generates a turn for any Participant whose **Effective Driver** resolves to `System`. To make room, the legacy `bool IsUser` storage on `PartyParticipant` migrates to a `DriverKind` enum (`User | LLM | System`), with `true → User` and `false → LLM`.

The vocabulary (**Narrator**, expanded **Driver**) is canonised in [`CONTEXT.md`](../../CONTEXT.md). This ADR records *why a third kind* and *why now*, since [ADR 0011](0011-default-and-override-driver.md) deliberately kept the bool.

## Why now (and why not chattiness=0)

The chat-import work (see [ADR 0013](0013-import-as-iterative-scene-workshop.md)) needs a permanent home for narration lines that no specific Persona owns. The existing prototype invented a per-import `_narrator` Persona on every commit — one new library Persona per imported chat, accumulating forever. That's the proximate trigger.

Three candidate shapes were on the table:

| | Shape | Verdict |
|---|---|---|
| **A** | Per-import Narrator Persona (current prototype) | Rejected — library pollution; one stale Narrator per import. |
| **B** | Singleton Narrator Persona with `chattiness=0` | Rejected — see below. |
| **C** | Singleton Narrator Persona with new `Driver.System` (chosen) | **Chosen.** |

Why C beats B:

- **`chattiness=0` is a soft signal, not a guarantee.** Urge math (see `UrgeMath`) can spike for direct-mention or scenario-pressure reasons even when chattiness is low. A Narrator that *might* speak unprompted under the right cascade is a footgun: scripted narration is no longer the only voice the Narrator carries.
- **`System` generalises beyond the import use case.** Future scripted NPCs, scenario-voice injection, and any "this voice is authored, not generated" path can re-use the same Driver kind without re-deriving the guard.
- **The guard becomes a single hard branch in the pipeline** — never auto-generate for `Effective Driver = System` — instead of a probabilistic gate that needs to be re-verified every time the urge math is tuned.

## Why the storage migration now

[ADR 0011](0011-default-and-override-driver.md) explicitly deferred the `bool IsUser → DriverKind` migration with the line *"kept until a third kind appears or auth lands."* The third kind has now appeared. Doing the migration with the Narrator work — rather than as a separate refactor — avoids a transitional period where storage is bool but pipeline types are enum (already true today for `CastMember`/`ParticipantView`/`SelfView`), with the bool acting as a lossy bridge.

Per the `feedback_no_migration_safety_internal` memory, stored event streams and DB data are not load-bearing for internal refactors. The migration maps `IsUser=true → User`, `IsUser=false → LLM`, and back-fills Narrator-Participants into every existing Party in a single Cypher write.

## Consequences

- `PartyParticipant.IsUser` is removed; `DriverKind` becomes the storage form everywhere.
- The CONTEXT footnote about `IsUser` being kept is rewritten to reference this migration.
- A library-level singleton Narrator Persona is seeded by migration. It is *not* deletable through normal Persona-management UI (a separate guard).
- Every Party — including existing ones — auto-grows a Narrator-Participant with `Default Driver = System` on creation or back-fill. See [ADR 0013](0013-import-as-iterative-scene-workshop.md) for the eager-vs-lazy reasoning.
- The Response pipeline gains a single guard at the auto-generation entry point that short-circuits when `Effective Driver = System`. Other Driver kinds proceed as today.
- Narrator accumulates **Memories** (Recollections, Stances, Concept references) like any other Participant. Scripted-narration callers can read those edges to produce in-context ambient text without re-deriving scene state.
