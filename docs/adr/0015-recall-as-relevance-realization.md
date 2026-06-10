# Recall as relevance realization: salience-ranked, graph-walked, self-organizing

> Status: accepted. Supersedes the retrieval design of [ADR 0009](0009-mvp-recall-top-n-recent.md)
> (top-N recent, no ranking). What survives from 0009: the `IMemoryRepository` seam as the
> swap boundary — this ADR is exactly the swap that seam promised — and the
> "Decision selects, Speaking executes" handoff from [ADR 0010](0010-two-phase-persona-response.md).
> Builds on the graph shape of [ADR 0014](0014-property-anchored-events-drop-party-room-message-vertices.md).
> Vocabulary (**Relevance Realization**, **Salience**, **Recall**, **Capture**) is canonised in
> [`CONTEXT.md`](../../CONTEXT.md).

**Recall** stops being a recency query and becomes an instance of **Relevance Realization**:
candidates are ranked by **Salience** (a self-organizing score that strengthens with use and
decays with time), and surfaced through two arms held in tension — *relevant to this beat*
and *recent* — rather than collapsed into one formula.

## Salience scoring

The `RECOLLECTS` edge gains four properties at **Capture**:

| Property | Source |
| --- | --- |
| `id` (uuid) | generated — stable identity for strengthening writes (and the future-embeddings join key, see below) |
| `weight` (0..1) | emitted by the recollection extractor *in the same LLM call* that writes the snippet — "how much would this stick, a day later?" replaces the binary NONE judgement |
| `recall_count` | starts 0; incremented when the Decision phase *picks* this memory |
| `last_recalled` | ISO timestamp of the last pick |

Read-time score: `salience = weight × decay(now − ts) + use_bonus(recall_count, last_recalled)`.
Computed in C# over a candidate set fetched from AGE — no scoring in Cypher.

Decisions inside that formula:

- **Wall-clock decay, not ordinal.** Decay runs on real elapsed time, not on
  captures-since. A Party left alone for three weeks comes back with somewhat hazy
  personas — the world stays *alive* while you're away, and wall-clock is the only clock
  end-users can intuit. (Rejected: ordinal decay — fades per subsequent Capture — which
  would pause forgetting when the Party pauses. Defensible, but opaque to users.)
- **Strengthening fires on the Decision phase's *pick* only** — not on mere surfacing in
  the prompt. Attention strengthens; exposure doesn't.
- **No valence on Recollections.** Feeling lives on the participatory layer (**Stance**,
  [ADR 0016](0016-stance-floor-and-auto-appending-consolidation.md)); a "flashbulb"
  emotional memory is simply high `weight`. The extractor judging per-persona valence
  without knowing the persona's existing orientations would be noise anyway.

## Selection by index, not by copy

The Decision prompt numbers the surfaced recollections; the decision schema's
`memoryToReference` becomes a nullable *index* instead of "copy the text verbatim". Fixes
the fragile copy contract and gives the strengthening write its key. The Speaking phase
still receives the resolved snippet text — its contract is unchanged.

## Two-arm recall: quota union

| Arm | Query | Slots |
| --- | --- | --- |
| **Relevant** | anchors = Participants in the Room's cast + Concepts whose normalised `name` matches tokens of the triggering message; walk `←ABOUT← Event ←RECOLLECTS` for this Participant | up to ~5 |
| **Recent** | `ORDER BY ts DESC` (the 0009 query) | fills remainder to N≈10 |

Each arm is internally salience-ranked; results dedup by edge id; the model sees one
undifferentiated `# What you remember` block. Zero relevant matches → recent fills all
slots, i.e. the system degrades gracefully to exact ADR 0009 behaviour.

Rejected: a single merged formula (`weight × decay × relevance_boost`). It invents a
tunable cross-arm weight with no eval harness to tune against, and a hot topic can starve
everything else out of the window. The quota keeps the two pulls in *structural* tension —
opponent processing, which is the RR-faithful shape — and guarantees a serendipity budget
of recent-but-unrelated memories. Graduate to a formula if an eval harness ever exists.

## Beat-path invariant

**The response pipeline stays at ≤2 LLM calls per turn (Decision + Speaking), plus the
cheap-tier salience call only when a race occurs. Every LLM call the memory system adds
runs in rest — asynchronously, never blocking a reply.** Concretely: weights ride the
existing extraction call; strengthening is one DB write; decay is arithmetic; the
graph-walk is one Cypher query. Memory *extraction* routes to the cheap model tier
(`JobComplexity.CharacterThoughts`-class); **Consolidation** stays on the General tier —
it writes beliefs.

## Observability

Each recall logs its candidate set (ids + arm + scores) and the Decision's pick. This is
what makes "defer until it bites" honest — misses become visible in the papertrail instead
of hypothetical.

## Embeddings: deferred, with a contract

The steelman for early embeddings is real (read-time relevance vs write-time Concept tags;
pgvector is one line in an already-custom image). It loses on: the *provider* is the real
cost (OpenRouter serves no embeddings — new vendor key or mandatory local Ollama), and at
~10–50 memories per Participant the Decision LLM already sees essentially all candidates
in-context, so the bottleneck embeddings fix doesn't exist yet.

The deferral terms:

1. **Trigger**: recall misses observable in the papertrail, or memories-per-Participant
   routinely exceeding what the prompt window can carry (~50+).
2. **Substrate already laid**: the `RECOLLECTS.id` uuid is the join key for a pgvector
   sidecar table; adding vectors later requires no graph reshape and no seam change —
   the hybrid third arm slots into the same quota union.
