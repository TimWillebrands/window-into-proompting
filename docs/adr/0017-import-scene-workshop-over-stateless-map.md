# Import as a scene workshop over a stateless map engine

> Status: accepted. Supersedes [ADR 0013](0013-import-as-iterative-scene-workshop.md) —
> its interaction skeleton (scene-by-scene, human-gated, draft-first) survives; its
> extraction engine (identify → classify) and context mechanism (AGE read-back per scene)
> are replaced. Evidence: the import-spearhead bench probes, phases 1–3
> (`tools/bench/Probes/ImportSpearheadProbes.cs`, `ImportRecallProbes.cs`,
> `ImportCardProbes.cs`, `ImportMessageProbes.cs`; issue #116 judgment; model sweep in
> [`docs/research/2026-07-15-import-extraction-model.md`](../research/2026-07-15-import-extraction-model.md)).
> Implementation plan: [`docs/plans/2026-07-16-import-rewrite.md`](../plans/2026-07-16-import-rewrite.md).

Chat import (Gemini AI Studio `.json` → a resumable **Room** with seeded **Memory**) is
rebuilt as a human-driven **scene workshop** on top of the extraction engine the
spearhead validated: deterministic IR → stateless per-scene LLM map → code-side fold
into a reviewable draft → per-scene commit.

## The engine (validated by the spearhead)

1. **Deterministic IR.** One parser reads the source file into typed chunks
   (message / thought / media / recap / systemInstruction) using structured fields plus
   one regex — no LLM. Every downstream stage consumes only the IR; nothing reads the
   source format again. This is the seam where a second source format would plug in
   later — one concrete parser today, no format abstraction (deliberately reversible).
2. **Stateless map.** One General-tier LLM call per scene returns typed items —
   `trait | episode | rule` — plus a per-chunk routing record. Each call is independent:
   no running "story so far" is carried between scenes. Continuity comes from chunk
   overlap at scene edges and from the optional registry (below), both explicit inputs.
   The map function is `f(chunks, registry?, note?) → items`; same inputs, rerun freely.
3. **Fold (reduce).** Code merges each scene's items into the accumulated draft:
   overlap dedup, cross-source episode dedup (recap-derived vs message-derived),
   persona trait folding, alias/typo canonicalisation, concept merging, conservation
   ledger arithmetic, timestamp assignment (anchor + chunk-ordinal · spacing). The fold
   is code-first because every merge so far is mechanical — but that is a default, not
   dogma; a merge that genuinely needs judgment may earn an LLM call later.

Why map, not scan: ADR 0013 had scene N's extractor read scenes 1..N-1 back from AGE.
That serial chain propagates extraction errors forward, cannot run scenes independently,
and is not resumable. The spearhead validated the alternative (overlap + registry +
deterministic fold) end-to-end at measured cost (~53 calls for the 212-message band of
the reference export).

## The workshop (carried from ADR 0013, re-grounded)

The user drives: select a chunk range as a **Scene**, optionally attach a free-text note
and edit the registry, run the map, review the resulting draft items, regenerate at will,
commit when satisfied. Batch-import-everything is just the degenerate workshop session.

- **Scene** (workshop vocabulary, canonised in `CONTEXT.md`): a human-selected chunk
  range processed as one unit — usually one narrative scene, sometimes more (a recap).
  Workshop-only; dissolves on commit. No `Scene` node in AGE (unchanged from ADR 0013).
- **Regeneration is side-effect-free.** Run/rerun mutates only the session draft. The
  only cost is the LLM call.
- **Commits are per-scene and may be out of order.** Ordering only affects dedup
  context (a later-committed earlier scene deduped against less); that trade is left to
  the human. Bail mid-import → committed scenes form a usable Room (graceful
  cancellation, kept from ADR 0013).
- **Regeneration is a draft-only power.** Committed scenes are settled — re-running one
  would mean deleting committed memory edges (the rollback swamp ADR 0013 rejected).
  Whole-import rollback stays cheap: delete the Room.

## Session state: `ImportSessionGrain`

One plain persistent-state grain per import session (not event-sourced — no concurrent
writers, no time-travel value; draft state is not load-bearing archaeology). It holds
the IR chunks (stored once; scenes reference by index range), scene definitions,
registry, accumulated draft, per-chunk conservation ledger, and match-or-mint decisions.
Session grain is disposable after the workshop ends.

## The registry: optional context, nouns not narrative

A small, human-owned suggestion structure injected into map calls to improve extraction
quality. **Empty registry = valid run**; it is a quality dial, never a dependency.

- `cast[]` — canonical name + aliases + routing flag (persona | person-as-concept).
  Pins name canonicalisation (observed run-to-run drift: honorific variance, stray role
  labels) and encodes the cast-coverage rule: dossier'd cast → Persona; recurring
  referenced characters → Concept, deliberately (person-as-concept is load-bearing —
  phase 2 showed arc-critical non-cast characters are reachable only via Concept).
- `concepts[]` — established concept names + aliases, so the extractor links instead of
  minting near-duplicates.
- Band anchor + spacing — operator-controlled timestamp scheme. The operator UI must
  surface anchor-as-vividness: older anchor + salience decay = hazier memories (measured
  38× salience gap for a 30-day-old anchor).
- Per-scene free-text note — the user's hint ("this scene is a dream sequence").

New cast/concepts discovered by a scene are *suggested* into the registry; the human
confirms, edits, or ignores. Prior-episode injection is an explicit per-scene opt-in,
never a default — carrying narrative between calls is the error-propagation channel the
map shape exists to avoid.

## Routing, weights, conservation

- **Weight.** The extractor emits a per-episode weight (0..1); validated non-degenerate
  (spread 0.3–0.95, sane ordering). Good-enough is the bar; no calibration chasing.
- **Weight floor** (default 0.5, per-import setting): episodes below the floor route to
  Room history only — no AGE Event. This is a *draft default*, greyed-with-reason in
  review, flippable per item by the human. Without it, message-band extraction mints
  ~1 event per message and drowns the canon events (measured: ~180 vs 96).
- **Rules** (meta/instruction content found in chat) route to `discarded(reason)` —
  recorded, not silently dropped.
- **Conservation invariant is sacred:** every IR chunk is accounted for in the ledger
  (event-sourced | folded | history-only | discarded(reason) | unprocessed). No silent
  loss, ever.

## Commit: finalize, then pure writes

Committing a scene runs at most one LLM step, then only deterministic writes:

1. **Persona finalize** — for each persona whose traits this commit touches: one call to
   compress/dedup the trait list and synthesise a one-line **Bio** (the decision phase
   composes on Bio; without synthesis imported personas render `(no bio)`). Finalize
   output lands in the draft as the final persona card for review. Later scenes that add
   traits re-finalize and *update* the card.
2. **Pure writes** — Room + messages (history-routed chunks), personas (mint or update),
   AGE Events + Recollections at scheme timestamps with weights and per-episode concept
   links, Concepts. Retryable; no LLM.

Invariant: **every LLM output passes human review as draft before it becomes real.**
Traits live on the persona card (system prompt), never in the memory graph — intrinsic
self-stances collapse to one via latest-wins (phase-2 finding).

**Persona match-or-mint** (imported "Lena" vs library "Lena") surfaces *early* — when a
cast member first enters the registry — via the ADR 0013 mechanism (exact + Levenshtein
over primaryName + aliases, per-character `unmatched → proposed → confirmed-match |
confirmed-mint`). Commit executes the recorded decision; it never prompts.

## Correction ledger

At scene commit, the diff between suggested and human-final is appended to a durable
correction ledger: chunk refs, suggested vs final (weight, routing, canonical name,
dedup verdict), correction kind (promoted | demoted | reweighted | merged | split |
renamed | match-flipped | regenerated-with-note), and any regeneration note. The session
grain dies; the ledger survives. First consumer: the Bench (corrections as ground truth
for extraction-prompt iteration). Few-shot injection and finetuning are explicitly
deferred — nothing learns online; we just stop discarding labels.

## API surface

The legacy import (`ImportController`, `ImportService`, `ImportPrompts` — endpoints
`extract-personas`, `merge-personas`, `classify-ws`, `identify-ws`,
`regenerate-char-detail`, `commit`) is deleted wholesale, same stroke. Replacement:

- `POST /import` — upload + IR parse (no LLM) → session id + chunk overview
- `POST /import/{id}/scenes` — create scene (chunk range + note) → scene id
- `POST /import/{id}/scenes/{sceneId}/run` — run or regenerate the map (same endpoint)
- REST edits on session (registry, match-or-mint) and scene (draft items, routing flips)
- `POST /import/{id}/scenes/{sceneId}/commit` — finalize touched personas + pure writes
- `DELETE /import/{id}` — abandon; uncommitted draft evaporates

Progress and draft items stream over the existing realtime hub — no bespoke import
WebSockets. "Commit all reviewed" is a frontend loop over per-scene commits.

## Consequences

- ADR 0013's `RecallRoomNarrativeAsync` is not built — the read-back mechanism it served
  is gone. Import no longer couples to the recall layer at extraction time.
- The bench spearhead probes are scaffolding: they stay until the production importer
  ships, then are deleted. The extraction prompts and fold logic they validated move
  into backend import services.
- Importer model rides the existing LLM router at General tier; current default
  `mistralai/mistral-small-2603` per the research note. No hard model dependency —
  scene-sized work keeps slow local models usable (a whole-chat batch would not).
- Frontend gets an import workshop app (XP-style window); `pnpm api-generate` after the
  backend surface lands.
- Parked, deliberately: import salience vs organic recall (anchor decay), the
  reality-layer dimension (actual | dream | in-fiction), recap-vs-verbatim collision
  handling under multi-import (mechanism proven, under-exercised by the reference
  export). Prompt-injection defences remain out of scope at the operator boundary
  (unchanged from ADR 0013 / PRD #74).
