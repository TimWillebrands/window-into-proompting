# Arrival: a Persona's nurture is seeded by an agent, not imported from a format

> Status: accepted. Supersedes [ADR 0017](0017-import-scene-workshop-over-stateless-map.md)
> (and with it [ADR 0013](0013-import-as-iterative-scene-workshop.md)). Builds on ADR 0016
> (Stances) and ADR 0021 (lived self).

The importer turned one chat-export format into a Room's message log plus memories, scene by
scene. It was chained to that format, it recreated Room history nobody needed, and it gave
imported memories historical timestamps that fell behind the Consolidation watermark. Meanwhile
the real need went unmet: a Persona joining a Party arrives as a blank slate. They know their
Bio and nothing that ever happened to them.

## Decision

A Persona has two halves:

- **Nature** lives on the Persona: Bio, system prompt, dials, Intrinsic Stances. It travels
  into every Party.
- **Nurture** lives in the Party: Recollections, Acquired Stances and the Events they hang off.
  It is the Persona's history *in this world*.

**Arrival** seeds nurture when a Persona joins a Party. Its input is a **seed corpus**: any
free text, from a vibe ("grew up restless, moves a lot") to concrete pointers ("Denise knows
Vlad from five years ago, when he still lived in Bangladesh"). An agent works the corpus into
memories. It reads the Persona's nature, the Party's cast and the existing nurture of anyone
the corpus touches, so a shared past stays consistent with what Vlad already remembers.

Arrival is a dry run first:

1. The agent produces an **Arrival draft**: the newcomer's Events, Recollections and Stances,
   *and* the additions to existing Participants' nurture (Vlad remembers Denise too).
2. The author reads the draft and prompts for changes; the agent revises the draft.
3. The author accepts; the draft commits to the Party in one go.

Nothing touches the memory graph before acceptance.

The importer, its session grain, the Import Workshop app and the `Scene` vocabulary are
removed. A chat log is just more corpus: it becomes memories, never Room messages.

## Why an agent, not a parser

The corpus has no format and is mostly underspecified. Turning "they used to be close" into
Events, perspectives and Stances that fit an existing cast needs judgement and lookups (who
is Vlad, what does he already remember, what Concepts exist), not a schema. Revising by prompt
also needs the agent to keep the whole draft in view.

## Consequences

- Arrival writes through the memory graph's shared write shapes (`MemoryGraphWrites`), so
  seeded and organic memories look the same to Recall and Consolidation.
- Seeded memories need past timestamps ("five years ago"). Arrival must leave them
  consolidatable: either Arrival writes the Stances that consolidation would have formed, or
  the watermark is placed before the seeded span. The design pass decides which.
- Arrival can change existing Participants' memories, so it is author-driven only — never
  triggered by the World clock or a Life turn.
- The Persona generator (nature) and Arrival (nurture) are separate steps: generate or write
  a Persona, then bring them into a Party.
