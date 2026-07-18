# Import rewrite — implementation plan

Decisions and rationale: [ADR 0017](../adr/0017-import-scene-workshop-over-stateless-map.md).
This doc is sequencing only. Slices are tracer-bullet vertical: each one ends with
something observable end-to-end. Not yet broken into issues (deliberate — cut issues
from this doc when work starts).

## What already exists and moves

The spearhead probes contain working, validated versions of most of the engine. Port,
don't rewrite:

| From (bench, scaffolding) | To (backend, production) |
|---|---|
| IR categorizer (316-chunk parse, `ImportSpearheadProbes.cs`) | `Services/Import/` parser (the format seam) |
| Section extraction prompt + schema (traits/episodes/rules, weights, concepts[]) | scene map call, canon/recap path |
| Window extraction prompt + schema (`ImportMessageProbes.cs`) | scene map call, message path |
| Fold: dedup (word-overlap + participants), persona folding, alias canonicalisation, conservation ledger | incremental fold into session draft |
| Seeder Cypher shapes (`ImportRecallProbes.cs`, mirrors `AddRecollectionAsync` at chosen ts/weight) | commit writes into AGE |
| Card fold (traits → SystemPrompt, `ImportCardProbes.cs`) | persona finalize input |

Probes stay until the importer ships, then delete (scaffolding, per bench convention).
The legacy `ImportController` / `ImportService` / `ImportPrompts` (~2100 LOC) are
deleted in slice 6 — untouched until the replacement surface is live, so the frontend
keeps a working (if bad) import throughout.

## Slices

### 1. Tracer: upload → scene → draft items over the wire

`ImportSessionGrain` (plain state) + IR parser + three endpoints: `POST /import`
(upload, parse, chunk overview), `POST /import/{id}/scenes` (chunk range + note),
`POST /import/{id}/scenes/{sceneId}/run` (map call → items into draft). Read draft via
REST. No registry, no fold beyond appending, no commit, no UI — verify via Swagger
against the Technokangs export. Proves: grain lifecycle, IR port, map call through the
LLM router at General tier, draft persistence across restart.

### 2. Fold + conservation + weight floor

Incremental fold on scene run: overlap/cross-source dedup vs accumulated draft, persona
trait folding, alias canonicalisation, per-chunk conservation ledger, weight-floor
routing marks (`history-only (below floor)`, flippable flag on items). Rerun-replaces
semantics for regeneration. Draft-edit REST (routing flips, item tweaks).

### 3. Commit: pure writes

`POST .../scenes/{sceneId}/commit` without persona finalize first: Room + messages,
Events/Recollections/Concepts into AGE (port seeder shapes; respect AGE footguns —
inline literals, typed reads). Out-of-order allowed; committed-item index feeds later
dedup. After this slice a workshop session produces a real, resumable Room.

### 4. Personas: match-or-mint + finalize

Registry `cast[]` with match proposals (exact + Levenshtein vs library) surfaced at
first appearance; confirm-match/confirm-mint state on the session. Finalize step at
commit for touched personas: trait compress + Bio synthesis, card lands in draft for
review, commit mints/updates the persona. Registry `concepts[]` + anchor/spacing
settings land here too (registry always optional — empty registry must stay a valid
run).

### 5. Frontend workshop app

XP-style import window: upload, chunk strip with scene range selection, per-scene note,
run/regenerate, draft review (items with weights, floor-greyed rows, flip controls,
conservation ledger), match-or-mint prompts, per-scene commit, registry editor as
suggestions. Realtime-hub streaming for run progress. `pnpm api-generate` after the
surface stabilises.

### 6. Correction ledger + legacy deletion

Correction ledger written at commit (suggested vs final diff + regeneration notes) to
durable storage. Delete `ImportController`, `ImportService`, `ImportPrompts`, legacy
frontend import flow; regenerate API client. Update `CONTEXT.md` glossary (Scene) and
delete the spearhead probes.

## Order notes

- 1→2→3 are strictly sequential (each consumes the previous). 4 can start after 2.
  5 needs 3 (nothing worth showing before commit exists); a thin read-only draft viewer
  could start after 2 if frontend work wants to parallelise. 6 is last, gated on 5
  replacing the legacy flow.
- Bench remains the extraction-quality loop throughout: prompt changes during the port
  are judged with the existing probes until slice 6 deletes them; after that, the
  correction ledger becomes the ground-truth corpus.

## Known risks

- **Fold code volume.** The probe fold is probe-shaped (single pass, in-memory); the
  incremental version against a persisted draft is the largest genuinely new code in
  the rewrite. Watch for it growing its own state bugs — keep it pure
  (draft in → draft out), test it in `backend-test/` (it's deterministic — assertable,
  no bench needed).
- **Persona card update semantics** (slice 4): re-finalize on later commits must update,
  not duplicate, and must not clobber human edits made to a card between commits.
  Simplest rule: human edit wins; re-finalize only appends a proposal for review.
- **Grain state size**: chunks stored once, scenes reference by index; keep the draft
  free of duplicated chunk text or a large import bloats grain writes.
- **Collision handling under multi-import** is proven-but-under-exercised (ADR 0017,
  parked list) — first real second import into an existing Room will stress it; expect
  a tuning pass then, not now.
