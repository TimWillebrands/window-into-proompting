# MVP recall: top-N recent Recollections, no matching, no embeddings

> Status: accepted.

Slice 2 of the memory feature wires **Recollection** retrieval into persona generation. The MVP query is intentionally the simplest possible: **the N most recent Recollection snippets for a `(Persona, Party)` pair, ordered by timestamp DESC**. No concept matching, no embedding similarity, no relevance scoring. The decision LLM is left to judge which snippets matter in the current beat, in-context.

The retrieval seam is `IMemoryRepository.RecallRecentSnippetsAsync(personaId, partyId, limit, ct)`. The orchestrator (`PersonaGrain.NotifyMessageAsync`) calls it once per turn before `RunDecisionPhaseAsync` and threads the snippets into the system prompt via a `# What you remember` block.

## Why top-N recent (and not matching)

Four candidates were considered:

| | Query shape | Verdict |
|---|---|---|
| **A** | Top-N recent for `(Persona, Party)` | **Chosen.** |
| **B** | Recollections whose Event has a Concept match against current message tokens | Rejected for MVP — see below. |
| **C** | Concept-match first, fall back to top-N recent | Rejected — two queries, more code, no MVP win. |
| **D** | Embedding similarity over snippets/descriptions | Out of scope — embeddings column, index, and provider not yet in the stack. |

Reasons A wins MVP:

- **Concepts are coarse.** The slice-1 capture extractor rolls specifics into general tags ("career goals", not "cto"). A query that hinges on Concept matching would miss the very memory it was supposed to surface — the test case that drove this slice (Denise's "I want to be CTO") is precisely such a roll-up.
- **LLMs are good at in-context selection.** Given ~10 snippets and a fresh user message, the model reliably picks the relevant ones. The retrieval layer doesn't need to be the relevance layer at this scale.
- **The interface is the abstraction.** `RecallRecentSnippetsAsync` is a single repository method. When matching, ranking, or embeddings *do* earn their place, the swap happens inside this method — call sites, prompt assembly, and the rest of the stack stay untouched.
- **Cypher is one line.** `MATCH (:Participant)-[r:RECOLLECTS]->(:Event) RETURN r.snippet ORDER BY r.ts DESC LIMIT N`. Index already exists ([ADR 0008](0008-ef-migrations-for-memory-schema.md) carried it from the original DDL). No new schema work.

## Scope: same-Party, cross-Room

Recall walks **all Recollections** the Participant has accumulated in this Party — across every Room they've been in. The Room is deliberately *not* in the filter; cross-Room recall is the whole point of the feature. Cross-**Party** recall (the same Persona's memories from a different Party) is **not** in scope: per `CONTEXT.md`, Recollections are edges on `(Persona, Party)`-scoped Participants. Aggregating across Parties would either require Persona-scope edges (Intrinsic Stance territory) or a "merge memory across Parties" semantic that hasn't been defined.

## Self-Recollection

A second slice-2 change: the capture path now writes a Recollection for the **speaker** as well as observers. The `MemoryExtractor.ExtractRecollectionAsync` prompt splits on `isSpeaker` — observer variant keeps the `you saw / heard / watched` framing, speaker variant uses `you said / admitted / brought up` so the LLM produces a usable first-person memory of the persona's own utterance. Without this, the persona that *said* "I want to be CTO" had no memory of saying it; only the bystander did.

This change is mechanical, not architectural — the Recollection vocabulary in `CONTEXT.md` already covers it ("one per Participant who remembers it, each with their own spin"). Speaker is just another Participant who witnessed the moment.

## Consequences

- **Top-N is recency-biased.** A torrent of small Remembers in one Room could push earlier high-signal memories out of the prompt window. Mitigation lives downstream: better capture discipline (the human picks what's worth remembering), or eventual matching/ranking when this bites in practice. Defer until it bites.
- **Prompt size grows with `N`.** N=10 fits well inside current prompt budgets — each snippet is ≤ 25 words by extractor cap. If memory volume grows past ~50 per Party, revisit either N or move to matching.
- **Recall failure is non-fatal.** `PersonaGrain` catches exceptions from `RecallRecentSnippetsAsync` and proceeds with an empty list. A memory outage degrades the persona to slice-1 behaviour; it does not silence them.
- **Auto-respond shortcut bypasses recall.** At urge ≥ 0.9 (direct mention) `ShouldRespondAsync` returns a canned response without ever building the system prompt. Recollections therefore don't influence direct-name replies in slice 2. Acceptable — the canned reply is short, mostly a chime-in. Revisit if the shortcut starts producing recall-naive answers that an observer would find jarring.
- **Tested seam is the orchestrator, not the recall query itself.** `MemoryRepositoryIntegrationTest` already exercises the AGE round-trip for capture; the recall query rides the same fixture and can grow a unit test cheaply when needed.

## Escape hatch

When matching becomes worth its complexity:

1. Add an overload (or replace the impl behind the existing method) inside `MemoryRepository`. The Cypher grows a `MATCH (e)-[:ABOUT]->(:Concept)` join + a `WHERE` on a token set the caller supplies.
2. Optionally surface a `tokens: IReadOnlyList<string>` parameter on `RecallRecentSnippetsAsync` (extract from the latest user message).
3. Call sites change only if new parameters are required.

When embeddings become worth their complexity:

1. Add an embedding column to the `RECOLLECTS` edge (or a sibling Event property).
2. Index with `pgvector` or AGE's nearest-neighbour primitives.
3. Same repository surface — the method body switches retrieval strategy.

In both cases the public surface of `IMemoryRepository.RecallRecentSnippetsAsync` is the boundary: the rest of the stack stays unaware.
