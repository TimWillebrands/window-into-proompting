# Import as iterative scene workshop, not linear pipeline

> Status: superseded by [ADR 0017](0017-import-scene-workshop-over-stateless-map.md).
> The scene-by-scene, human-gated workshop skeleton survives there; the extraction
> engine (identify → classify) and the AGE read-back context mechanism do not.

Chat import (Gemini AI Studio `.json` → a resumable **Room** with seeded **Memory**) runs as an iterative **scene workshop**: the user marks one scene at a time, the backend runs identify → classify → event extraction → review for *that scene only*, the user commits, and the next scene's extractor reads back what the previous scene wrote — sharing the recall layer that live capture uses. The Room and all parsed messages are persisted up-front on `init`; scenes layer **Event** / **Recollection** / **Concept** edges on top.

This replaces the prototype's linear `Identify → Classify → Commit` pipeline, which produced amnesiac Personas (no Recollections, no Stances), polluted the library with a per-import `_narrator` Persona, and offered no path to match an imported "Lena" against a library "Lena."

## Why scene-by-scene (not one-shot, not auto)

Four shapes were on the table:

| | Shape | Verdict |
|---|---|---|
| **A** | One-shot pipeline (current prototype) — identify the whole transcript, classify all msgs, commit everything | Rejected — no per-msg review tractable at scale; mis-extractions become permanent before the user sees them. |
| **B** | Fully automated — extract everything, write everything, surface a "review and rollback" UI after | Rejected — rollback of memory edges across N scenes is harder to design than just gating commit per scene. |
| **C** | Mark-all-scenes-first, then process | Rejected — pre-planning scene boundaries before seeing any extraction output gives the user nothing to anchor on. |
| **D** | Iterative scene workshop (chosen) | **Chosen.** |

Reasons D wins:

- **Memory crystallises in stages, matching how the human reads a chat.** Scenes are the natural unit a reader segments by; one-event-per-message is too fine, one-event-per-scene is too coarse. The LLM judges intra-scene event granularity; the user judges scene boundaries. Each gets the decision it is best suited for.
- **Prior-scene context is read from AGE, not from a parallel summary pipeline.** Scene N's extractor calls `IMemoryRepository.RecallRoomNarrativeAsync` (sibling to `RecallRecentSnippetsAsync` from [ADR 0009](0009-mvp-recall-top-n-recent.md)). Import shares the live recall layer rather than maintaining its own running summary. When recall improves (matching, embeddings), import benefits free.
- **Cancellation is graceful.** Bail after 3 of 20 scenes → the Room has all messages and three scenes worth of memory. The user can either resume the import later (single-browser draft persistence) or just continue the chat live; live capture writes per-msg Events from there as normal.

## Why α: Room + messages persisted up-front

Four cadence combos were considered:

| | Room+msgs | Scene Events |
|---|---|---|
| **α** | created up-front, bulk msgs in | committed scene-by-scene, read back via AGE | **chosen** |
| **β** | created up-front, bulk msgs in | staged in client until final "commit all" | rejected — fragile (can't survive page refresh), no live AGE feedback |
| **γ** | created on first-scene commit | committed scene-by-scene | rejected — empty-Room rendering and first-scene atomicity are both worse |
| **δ** | nothing persists until final | all staged | rejected — defeats the visualisation feedback loop |

Reasons α wins:

- **Messages are substrate, not crystallisation.** They do not need iterative review; the user has already reviewed inclusion at the file-pick stage. Bulk-importing them is cheap and rollback-safe (delete the Room).
- **"Build on prior scenes" works naturally** — scene N's extractor `MATCH`es AGE for Events from scenes 1..N-1 in this Room. No client-side context bookkeeping.
- **Memory Graph viz becomes the live feedback surface** — the user watches their import crystallise scene by scene.
- **Half-imported Rooms are functional.** Per the `feedback_no_migration_safety_internal` memory, stored DB state is not load-bearing for internal flows; users tolerate a half-imported Room better than a stuck import draft.

## Why Scene is workshop-only (not a first-class entity)

Scene exists only in the import draft and dissolves on finalize. No `Scene` node in AGE, no `IN_SCENE` edge.

Rejected: making Scene a first-class Reality entity with its own identity. Reasons for the rejection:

- Import is the only producer of Scenes today; live capture has no scene-end signal.
- Recall MVP is top-N recent ([ADR 0009](0009-mvp-recall-top-n-recent.md)); scene-as-filter only earns its keep when recall outgrows top-N — and that is deferred.
- Reversible later: `(:Event)-[:IN_SCENE]->(:Scene)` can be back-filled from import metadata when (if) Scene becomes a useful filter.

## Consequences

- The backend API surface for import shrinks to three endpoints (`POST /init`, `WS /scene`, `POST /finalize`). The legacy `extract-personas`, `identify-ws`, `classify-ws`, `merge-personas`, `regenerate-char-detail`, and single-shot `commit` endpoints are deleted.
- A new `IMemoryRepository.RecallRoomNarrativeAsync(chatGroupId, limit)` sits beside `RecallRecentSnippetsAsync`. Same Cypher style, different shape (recent Events with summaries + Concepts + recent Stances).
- Stance creation during import follows the existing **Consolidation** primitive — author-triggered, not coupled to generation. `finalize` auto-runs Consolidation per cast Participant; user can opt out.
- Persona dedup happens at the import boundary via `PersonaMatchService` (exact + Levenshtein on the union of `primaryName` + aliases). Per-char state machine: `unmatched → proposed → (confirmed-match | confirmed-mint)`. Scene commit blocks until every newly discovered char is confirmed.
- Driver assignment is uniform: every new Participant gets `Default Driver = LLM`. The Narrator Participant (added per [ADR 0012](0012-driver-system-and-driverkind-migration.md)) gets `Default Driver = System`. Existing Participants matched into the Room keep their existing default unchanged; import writes no Room-level Driver overrides.
- Timestamps anchor per-scene (scene 1 absolute, scenes 2..N relative offset from previous, within-scene one-minute tick). The legacy global `baseDateIso` + `stepSeconds` fields are removed.
- Prompt-injection defences are explicitly *not* in scope (see PRD #74). Trust is at the operator boundary; a future ADR can revisit when multi-user attackers become realistic.
