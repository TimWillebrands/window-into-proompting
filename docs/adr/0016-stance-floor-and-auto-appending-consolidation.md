# Stance floor: ambient in both phases; Consolidation auto-appends with retract

> Status: accepted. Supersedes [ADR 0007](0007-append-only-stance-edges.md) — its
> append-only core is restated (not reversed) below; what changes is the curation model.
> Vocabulary (**Stance**, **Consolidation**, the four knowing-layers) is canonised in
> [`CONTEXT.md`](../../CONTEXT.md). Recall mechanics live in
> [ADR 0015](0015-recall-as-relevance-realization.md).

First implementation of the participatory layer: **Stance** edges, an ambient prompt
block, and **Consolidation** as the only automated writer — auto-appending, with retract
instead of an approval queue.

## Carried over from ADR 0007 (unchanged)

- Stance is **append-only**: every write appends a new `STANCE` edge; nothing mutates.
- "Current" stance per (Persona, Target) = **latest edge wins**, computed at read over the
  union of Participant-scope (Acquired) and Persona-scope (Intrinsic) edges. No
  materialised projection.
- **Promotion** = a new edge written at Persona scope carrying the current projection.
- Time-travel and idempotent re-runs fall out for free.

## Edge shape

`(:Participant)-[:STANCE {id, valence, reasoning, ts}]->(target)` where target is another
`Participant`, a `Concept`, or the Participant itself (self-stances are in from the floor —
same edge, and the cheapest path to visible character growth). `valence` is a scalar
−1..1; `reasoning` is short free text in the same second-person voice as Recollection
snippets ("Denise exaggerates — you've watched her inflate three stories").

## Placement: ambient in *both* phases

Stances render as a `# Where you stand` block — at most ~5 one-liners, scoped to the
beat's live anchors (present cast + matched Concepts, the same anchor extraction ADR 0015's
relevant arm uses) — in **both** the Decision and Speaking prompts.

Rejected:

- **Decision-selects-one** (the Recollection mechanic): wrong category. A stance is not a
  callback you weave in; it is an orientation you speak *from*. It is identity-adjacent
  content, same class as the bio — which already renders in both phases.
- **Decision-only**: the persona would feel one way in thought and another in speech.

ADR 0010's wallpaper objection (the 10-item recollection menu in the Speaking prompt did
nothing) does not transfer: that was a *menu awaiting selection*; this is five lines of
who-I-am-relative-to-this-room. It also keeps the Decision→Speaking handoff at two fields —
ADR 0010 warns that handoff fields are cheap to add and expensive to remove.

Watch-item in play: an ever-present negative stance line risks a broken-record persona.
The bio's ever-presence doesn't loop, so the bet is that presence ≠ repetition; if it
loops in practice the fix is phrasing/rotation, not removal.

## Writers

1. **Curator, manually** (persona-management / debug UI + API) — the floor's first writer,
   so the read path is provable with hand-authored beliefs before any automation exists.
2. **Consolidation** (below) — the only automated writer.
3. **Capture, never.** Capture writes the propositional and perspectival layers only.
   Beliefs don't crystallise *during* an experience but in the rest after it; what a
   moment meant is decided retroactively, and may differ from how it felt live.

## Consolidation v1

Per-Participant run, **one LLM call** (General tier): inputs are the Recollections
accumulated since the watermark (`ts` property on the Participant vertex), the current
latest-wins Stances, and the bio; output is a list of proposed Stance appends
`{target, valence, reasoning}`.

**Auto-append + retract — this is the supersession of 0007's curation model.** Proposals
write immediately. The curator's tool is *retract*: one click appends a neutralizing or
corrective edge (append-only makes undo natural and history-preserving). A stance log /
graph-viz surface shows recent appends.

Why the approval gate goes:

- 0007's gate was designed against a threat that no longer exists — noisy *captures*
  writing stances mid-conversation. "Capture never writes the participatory layer" is now
  law, and Consolidation is already the slow, deliberate, runs-in-rest pass: gating it is
  double-filtering.
- The sleep analogy cuts the other way: nobody approves their own dreams. Beliefs
  crystallise unsupervised; the curator is the morning-after correction, not a gatekeeper.
- In a single-operator app, review queues rot — the participatory layer would starve
  behind an unread approval list.

Accepted risk: a bad model day can write junk beliefs across a Party before anyone looks.
Mitigations: gauge-spaced runs (below), every append logged and attributed, retract is one
click, and latest-wins means a correction immediately masks the junk.

**Triggers**: curator button; a gauge — Σ `weight` of unconsolidated Recollections per
Participant exceeding a threshold ("the persona has enough unprocessed experience to sleep
on"); and import-finalize per [ADR 0013](0013-import-as-iterative-scene-workshop.md).

## Ambivalence reads

When rendering `# Where you stand` for a target: fetch the latest Stance, then scan that
target's history for an earlier edge that *contradicts* — valence sign-flip or
|Δvalence| ≥ 1.0. If found, render one combined line ("Denise exaggerates — though you
used to admire her storytelling"), at most 1–2 such pairs per beat, most-salient targets
first. The append-only history stops being write-only archaeology and becomes playable
tension; latest-wins stays the resolution rule for everything else.

## Promotion re-targets Participants to Personas

An Acquired Stance often targets a *Participant* — "Vlad is impatient" points at
Vlad-in-this-Party. Promoted unchanged, the Intrinsic edge would point at a Party-scoped
node, meaningless everywhere else. Rule: **on Promotion, a Participant target re-points to
the target's underlying Persona.** At read time a Persona-targeted Intrinsic resolves
against whichever Participant embodies that Persona in the current Party, and renders
nothing where they're absent. Concept and self targets promote unchanged.

Open question (flagged, not solved): Concepts are global by construction today — one
memory graph across all Parties — which quietly violates "each Party is a self-contained
universe". Revisit if sealed Parties ever become a requirement.
