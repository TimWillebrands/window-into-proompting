# Stance is append-only; current belief is computed, not stored

> Status: **superseded by [ADR 0016](0016-stance-floor-and-auto-appending-consolidation.md)**.
> The append-only core carries over unchanged; the curation model (curator approves
> proposed Stances before write) is replaced by auto-append + retract.

Every **Stance** capture writes a *new* edge in AGE between a **Persona** (Intrinsic) or **Participant** (Acquired) and a target (**Concept**, **Participant**, or self), carrying `(valence, reasoning, timestamp)`. No edge is ever mutated. The "current Stance" for a `(Persona, Target)` pair is just **the latest edge wins**, computed at read time over the union of Persona-scope and Participant-scope edges. There is **no materialised current-Stance table**.

## Why append-only

A Persona's beliefs evolve over a Party's lifetime — Denise warms up to Lisp, Vlad's reputation slides after one too many interruptions. That evolution is the *point* of the memory feature. Mutable upsert loses it.

- Matches the event-sourced precedent from [ADR 0002](0002-event-sourced-party-and-room-grains.md): conversational history is journalled; beliefs are part of the same world.
- **Promotion** (Acquired → Intrinsic) is just "write a new edge at Persona scope with today's timestamp and the current projection." No diff-tracking, no record mutation.
- **Time-travel queries are free**: "what did Denise believe about Lisp on 2026-03-01?" = `WHERE ts < '2026-03-01' ORDER BY ts DESC LIMIT 1`.
- **Consolidation** can rerun safely. Idempotency falls out — repeat runs append duplicate edges, latest still wins, no corruption.

## Why no materialised current-Stance table

- Read pattern is *cold and small*: a few Stances per Persona, read at generation time. Computing the projection on read is one indexed lookup per `(Persona, Target)` pair.
- A materialised table would have to stay consistent with every Stance write — a separate write path, a separate failure mode, and a backfill on every schema change.
- If projection latency ever bites at scale, the materialised table is straightforward to add behind the same `IMemoryRepository` interface. Defer until it bites.

## Consequences

- **Stance growth is unbounded.** Beliefs are coarse and infrequent, so this is likely a non-issue for years. If hot in practice, add archival or per-`(Persona, Target)` retention.
- **Latest-wins is recency-biased.** A noisy or hallucinated capture overrides everything earlier. Mitigation lives in the **Consolidation** flow — diff-review UX: the curator approves a proposed Stance before it's written. Captures themselves don't write Stances (see the **Recollection-only at capture** decision in `CONTEXT.md`).
- **No Stance ever truly disappears.** "Deletion" is a tombstone edge or a new edge with neutral valence — the prior history stays. Acceptable; matches event-sourced norms.
- **Union queries cross scope.** Every read merges Persona-scope (Intrinsic) and Participant-scope (Acquired) edges. The graph walk in recall does this naturally; ad-hoc queries need to remember it.
