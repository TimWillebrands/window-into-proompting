# Response pipeline: Decision phase, then Speaking phase

> Status: accepted.

A Persona's **Response pipeline** runs as two distinct LLM calls. The **Decision phase** judges whether the persona has something worth saying and shapes what to bring to it. The **Speaking phase** — conditional on the Decision phase choosing to respond — drafts the actual chat message. The Decision phase shapes the Speaking phase via a structured handoff; the Speaking phase never sees the raw "should I respond" question.

The vocabulary (**Response pipeline**, **Decision phase**, **Speaking phase**) is canonised in [`CONTEXT.md`](../../CONTEXT.md). This ADR records *why two phases* and *what data crosses the boundary*.

## Why two phases (not one, not three)

Three candidates were on the table:

| | Shape | Verdict |
|---|---|---|
| **A** | One LLM call: structured output emits `{respond, message}` together | Rejected — see below. |
| **B** | Two calls: Decision then Speaking | **Chosen.** |
| **C** | Three calls: Decision → Plan → Speaking | Rejected — Plan added a step without earning it. |

Reasons B wins:

- **A persona must be free to decide *not* to speak.** The cost of generating a full message only to throw it away is high (tokens + latency + sometimes a streaming UI flash). A small Decision call gates a large Speaking call. Personas pass on most cascade messages; the gate pays for itself many turns over.
- **The persona reacts in-character *before* committing to airtime.** The Decision schema forces `gutReaction` first, then `memoryToReference`, then `wouldSay`, then `respond`. Field order drives generation order — the model engages emotionally before judging speakability. A single combined call tends to write justification-shaped output (because the schema asks for the message), which absorbs the assistant-restraint prior and degrades character.
- **Cancellation surface is cleaner.** A persona shipping a half-formed reply when a new message lands needs different handling than a persona still deciding. The race-trigger code can always cancel the Decision phase cheaply (no public artifact yet); the Speaking phase cancel logic only fires past the point-of-no-return. See [ADR 0001](0001-stop-signal-race-emote-on-cancel.md).
- **JSON-schema repair is simpler with a small payload.** Decision-phase JSON is short enough that the cleanup pipeline (`LlmJsonParsing` + `JsonRepairSharp`) handles real-world model failures. A combined response carrying a full chat message inside JSON would multiply the failure surface (newlines in strings, stray quotes, fences) — failures observed in production traces already.

Why not C (add a Plan phase):

- Two LLM hops already pay an observable latency cost. A third needs to earn the round-trip; we never found a step that does.
- The Speaking phase already accepts `turnInstruction` (the Decision phase's `wouldSay` sketch) as advisory guidance. That's the "plan", inlined into the call that uses it.

## The handoff contract

The Decision phase produces a `ShouldRespondResult`. Only some of its fields traverse the boundary into the Speaking phase. The split is deliberate:

| Decision field | Forwarded to Speaking? | Rendered as |
|---|---|---|
| `respond: bool` | No | Gates whether Speaking runs at all. |
| `wouldSay: string` | Yes (as `turnInstruction`) | A `system` message: `"Guidance for this turn: …"`. Advisory, not literal — Speaking phase may rewrite. |
| `memoryToReference: string?` | Yes (as `memoryToReference`) | A `# A memory surfacing for you` block at the recency position of the Speaking system prompt. |
| `gutReaction: string` | No | Surfaced to the thought-log UI only; deliberately kept out of Speaking to avoid double-priming the model's emotional register. |

Inputs the Decision phase receives but the Speaking phase does **not**:

- **Full recollections list** — Decision phase gets the top-N recollections (see [ADR 0009](0009-mvp-recall-top-n-recent.md)) and is responsible for picking one (`memoryToReference`) or none. The Speaking phase sees only the selection, not the menu. Contract is "Decision selects, Speaking executes" — a 10-item lookup table in the Speaking prompt was found to act as wallpaper, not influence.
- **`RepairHint`** — Levelt-style speech-repair cue (a message arrived while the persona was speaking last turn) lives in Decision only. The persona either acknowledges it in `wouldSay` or doesn't; the Speaking phase doesn't re-evaluate.
- **The `ResponseUrge` math** — frequency-control math (round penalty, self-dominance, chaos bonus) is exclusively a Decision-phase concern. It shapes whether the persona engages, never how they word a reply.

## Auto-respond shortcut

When `ResponseUrge.Total ≥ 0.9` (today: direct name mention) *and* no `RepairHint` is pending, the Decision LLM call is skipped. `ShouldRespondAsync` returns a canned `ShouldRespondResult { Respond=true, Instruction="React naturally — they spoke to you.", Reason="Heard my name (...)." }` with `MemoryToReference=null`. The Speaking phase then runs without any selected memory.

This is a cost/latency optimisation: the math is decisive enough that the LLM's verdict isn't needed. Trade-offs:

- **Memory-blind on the shortcut.** No recall snippets reach the Speaking phase. Acceptable when "they said my name" is the trigger — the reply is short, mostly a chime-in. Flagged as a known limitation in [ADR 0009](0009-mvp-recall-top-n-recent.md).
- **Bypassed on pending repair.** A pending `RepairHint` always pays the Decision LLM call so the prompt-level repair stanza fires; the shortcut would otherwise mask a missed message under the name-mention.

## Consequences

- **Two LLM calls per turn (when responding), one when not.** Most cascade messages are skips, so the average cost stays near one call. Cost grows linearly with response rate, not turn count.
- **The thought-log UX is a free projection.** `gutReaction` and `wouldSay` are already produced as discrete fields; the frontend's thought log just reads `appraisal.reason` and `appraisal.instruction`. No extra LLM call to "explain what the persona is thinking".
- **Race cancellation is phase-aware.** [ADR 0001](0001-stop-signal-race-emote-on-cancel.md) describes the cancel/emote flow; this ADR is the reason the race code has two branches (Decision-cancel = cheap discard, Speaking-past-PNR = repair hint).
- **Field-name drift across layers.** The schema's `wouldSay` is exposed in .NET as `ShouldRespondResult.Instruction`, and `gutReaction` is exposed as `Reason`. Renaming would break the frontend's `appraisal.instruction` / `appraisal.reason` thought-log readers. Acceptable; the JSON-attribute mappings carry the canonical names.
- **Adding handoff fields is cheap; removing them is not.** Every field in the handoff becomes part of the implicit contract between Decision and Speaking prompts. `memoryToReference` (added in this slice) is the second such field after `wouldSay`. A third addition should consider promoting the handoff to a named `SpeakingDirective` record so the contract is greppable.

## Escape hatches

When the two-call cost becomes a problem (high-traffic Parties, cheap models that handle combined output well):

1. Add a third `ShouldRespondMode` (today: `LlmDecision` and `AutoRespond` implicit) that runs a combined call and parses out both decision and message. Gate on persona-level or model-level config.
2. The Decision/Speaking vocabulary stays — the modes are implementation choices behind a shared contract.

When a Plan phase earns its place (multi-turn arcs, scene/beat planning, deliberate callbacks):

1. Insert between Decision and Speaking. Decision still owns "engage at all"; Plan owns "what shape"; Speaking owns "literal words".
2. The `SpeakingDirective` record grows fields (`responseShape`, `callbackBeat`, …) rather than the prompt growing more inline cues.
