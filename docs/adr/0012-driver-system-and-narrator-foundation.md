# Driver.System + IsUser → DriverKind migration (Narrator foundation)

> Status: accepted.

A **Participant**'s **Driver** is the thing currently operating it. Until now there were only two kinds (`User`, `LLM`), encoded on storage as a single `bool IsUser` on `PartyParticipant`. This ADR records the move to three kinds (`User | LLM | System`) and the introduction of the singleton **Narrator** Persona — the first inhabitant of `System` — and the back-fill that gives every existing Party a Narrator-Participant.

The vocabulary (**Driver**, **DriverKind**, **Narrator**) is canonised in [`CONTEXT.md`](../../CONTEXT.md). This ADR records *why now*, *what the storage shape looks like*, and *what alternatives were rejected*.

## Why now

The import workshop (PRD #74) needs to attribute narration lines — "Vlad enters the room", "the door creaks open" — to a real Participant in the Room, not a per-character speaker. The cleanest way to do that without polluting the persona library with one ghost-character per imported chat is to make narration a first-class voice: a singleton library Persona, joined to every Party, whose Driver tells the response pipeline "never auto-generate a turn for this one".

That requires three things this ADR locks in:

1. A third **DriverKind** so the Narrator's Participant has somewhere to live.
2. A hard pipeline guard so `System` Participants never speak unprompted.
3. An eager auto-grow so every Party (existing and future) has a Narrator-Participant available, without each caller having to remember to add it.

The CONTEXT footnote on `IsUser` previously said "promote to a `DriverKind` enum (or equivalent) when a third kind appears". This is that moment.

## What ships

### `DriverKind` enum

```csharp
public enum DriverKind { User = 0, LLM = 1, System = 2 }
```

`PartyParticipant.IsUser` (bool, `[Id(2)]`) is removed; `PartyParticipant.Driver` (DriverKind, `[Id(3)]`) takes its place. The integer values are part of the wire contract — frontend re-exports them in `src/lib/driver.ts`.

The slot index moves (`Id(2)` → `Id(3)`) deliberately, so the rebuilt Orleans serialiser doesn't try to read the old `bool` into the new `DriverKind` field on replay. Per the `feedback_no_migration_safety_internal` memory: stored event streams in dev are not load-bearing — accept the wipe rather than ship a migration path.

### Singleton Narrator library Persona

A well-known Persona id (`0000aaaa-0000-0000-0000-00000000a17e`) is reserved so imports and exports can refer to the same Narrator across deployments. A `NarratorSeederHostedService` on silo startup:

1. Registers the Narrator Persona in `PersonaRootGrain` if absent (idempotent).
2. Walks every Party via `PartyRootGrain.GetAll()` and re-asserts its participant list. `PartyGrain.SetParticipants` enforces the Narrator-present invariant, so this single call covers parties that never had a Narrator and parties that already do (latter is a no-op).

### Auto-grow invariant on `PartyGrain.SetParticipants`

`PartyGrain.SetParticipants` always normalises the submitted list to contain exactly one Narrator-Participant with the canonical id, name, and `Driver = System`. Mislabelled Narrator rows (e.g., a caller passing the Narrator id with `Driver = LLM`) are corrected on the way in. Callers don't need to know about Narrator.

### Hard response-pipeline guard

`ChatGroupGrain.SelectAutoRespondTargets` (the filter feeding `NotifyAllParticipantsAsync`) and `PartyGrain.SelectCancelTargets` (the filter feeding `CancelAllGenerations`) both return only `Driver == DriverKind.LLM` Participants. The Narrator is never fanned out, regardless of urge, regardless of mention, regardless of chattiness.

This is intentionally a **structural** guard rather than a chattiness=0 hack: urge math is a soft signal that the next persona prompt could re-derive, but `Driver == System` is a contract that the pipeline must respect even under future scoring changes.

### Frontend (`GroupChatWindow`)

The participant column renders the Narrator as a non-toggleable row with a `system` badge, distinct styling, and no driver-flip affordance. The user-persona picker and the AI-toggle list both exclude the Narrator id explicitly.

## Alternatives considered

### `chattiness = 0` instead of `Driver = System`

A Persona with chattiness 0 still has its decision LLM called and still might respond on a direct mention. Narrator must not respond *under any condition*, so the guard belongs at the pipeline edge, not inside the urge math.

### Keep `bool IsUser`, add `bool IsNarrator`

Two booleans encoding three states is a classic Hamlet-flag setup: every new caller has to learn that `IsUser && IsNarrator` is illegal, and every filter site has to encode the implicit AND. A proper enum eliminates the dead state.

### Don't seed Narrator eagerly; lazy-add on first import

The PRD wants Narrator visible in every Room's participant list (story 30), not just imported ones. Eager auto-grow keeps the data model consistent: every Party has the same shape regardless of how it was created.

### Per-Party Narrator instance vs. singleton

A per-Party Narrator would let each Room style its narrator differently (formal vs. snarky). Out of scope for this slice — the singleton ships now; per-Party customisation is a forward-compatible later change because the Driver enum is per-Participant, not per-Persona.

## Risks

- **Wire-contract drift between backend `DriverKind` integer values and frontend `Driver` constants.** Mitigation: frontend `src/lib/driver.ts` carries a comment pointing at this ADR and at `Model/Party.cs`. Tests on either side are integer-typed.
- **Pre-PR Orleans grain state on existing dev databases won't deserialize cleanly** (old `[Id(2)] bool IsUser` → no longer present). Acceptable per `feedback_no_migration_safety_internal`; reset the dev volume if it bites.
- **Back-fill on a very large set of Parties is sequential.** Acceptable today (single-digit Parties in dev). Re-evaluate when multi-tenant ships.
