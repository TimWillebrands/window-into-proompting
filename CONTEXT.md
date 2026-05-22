# Proompting

The domain language of Proompting (aka Partytown): a Windows-XP-themed app where one human and several AI **Personas** hang out in **Rooms** and talk.

## Language

**Party**:
A tenant boundary. Hosts a set of **Rooms** and a list of **Participants** (the **Personas** present in this Party). Each Party is a self-contained universe with its own cast, its own Rooms, and its own per-Persona **Memories**.
_Avoid_: server, workspace, tenant, world, desktop.

> Note: today the frontend always opens the default Party (id = `Guid.Empty`, exposed as `ROOT_PARTY_ID`). The API is fully multi-party; that surface is intentional, not dead code.

**Room**:
A named conversation thread inside a **Party** that a small cast of **Participants** talks in. Has its own message log, participant list, and optional **Scenario** text.
_Avoid_: chat-group, chat-room, channel, thread.

> Note: the backend code currently spells this `ChatGroup` (`ChatGroupGrain`, `ChatGroupEvent`, `chatGroupId` in API paths). That spelling is legacy — treat `Room` as the canonical name in all new code, comments, issues, and docs. Existing code can be renamed opportunistically.

**Persona**:
A character with a name, bio, system prompt, and behavioural dials (**Chattiness**, **Impulsivity**). Lives outside any Party — Personas are library entries that can join multiple Parties (eventually) and accumulate per-Party **Memories**. A Persona is a definition, not a membership.
_Avoid_: character, agent, bot, NPC, user.

**Participant**:
A **Persona**'s membership in a **Party**. The link object that says "Persona X is in Party Y, driven by Z." Rooms draw their cast from the Party's Participants. Removing a Persona from a Party deletes the Participant; the Persona itself remains in the library. The Participant owns the **Driver** — the same Persona can be User-driven in one Party and LLM-driven in another.
_Avoid_: member, attendee, party-member.

**Driver**:
The thing currently operating a **Participant**. Today, one of two kinds: `User` (a human types the messages) or `LLM` (the agent stack generates them). Driver lives on the Participant, not the Persona — so a Persona can have different Drivers in different Parties.
_Avoid_: operator, pilot, controller, agent.

> Note: today the backend expresses this as a `bool IsUser` flag on `PartyParticipant`. That flag is the current crude form of `Driver` — promote to a `DriverKind` enum (or equivalent) when a third kind appears.

**Scenario**:
Free-text in-fiction setup for a **Room**: where we are, what's going on, what the mood is. Visible to (and influences) every Persona in the Room. Optional — Rooms can have no Scenario.
_Avoid_: prompt, setting, premise, context.

### Persona behaviour dials

**Chattiness** (0..1):
A Persona's baseline urge to speak. Low = brooding, only chimes in when directly addressed or when something jumps out. High = wants in on every conversation.

**Impulsivity** (0..1):
How committed the Persona is to a reply once they've started forming one. Low = deliberative, easily interrupted by something new in the room. High = impulsive, barrels through their thought once committed.

### Response pipeline

The two-phase process a **Persona** runs when a new message arrives in a **Room**: a **Decision phase** that judges whether and how to engage, then (conditionally) a **Speaking phase** that drafts the visible message. The two phases run as separate LLM calls so the persona reacts in-character before committing to airtime, and so the decision can be skipped when it's clearly not warranted. The pipeline as a whole is what's torn down by a stop-signal race (see ADR 0001) or by `CancelGenerationAsync`.

**Decision phase**:
The first phase of the **Response pipeline**. The Persona considers what's been said, forms a **gut reaction** in-character, decides whether they have something worth saying, and — if so — picks which past **Recollection** (if any) is on their mind. Output is consulted, never displayed directly: it shapes the **Speaking phase**.
_Avoid_: appraisal, thinking, evaluation, pre-generation.

**Speaking phase**:
The second phase of the **Response pipeline**, conditional on the **Decision phase** choosing to respond. The Persona drafts the actual chat message — shaped by the gut reaction and the **Recollection** carried forward from the Decision phase. Output is the visible reply.
_Avoid_: generation, response, output, reply-phase.

> Note: the code spelling `Generation` (e.g. `GenerationParticipant`, `_ctsByGeneration`, `CancelGenerationAsync`, log/tracing tags) predates the **Response pipeline** vocabulary. The umbrella namespace is now `Services/ResponsePipeline/`, the per-beat session is `SpeakingSession`/`SpeakingResult`, and the in-flight phase enum is `InFlightPhase.Speaking`. Treat **Response pipeline** (umbrella) and **Speaking phase** (per-phase) as canonical in new code, comments, issues, and docs. Remaining `Generation*` spellings can be renamed opportunistically (`Pipeline*` for umbrella scope, `Speaking*` for phase scope).

**Stop-signal race**:
When a new message arrives in a **Room** while a **Persona** has an in-flight **Response pipeline**, the persona evaluates whether the new message warrants interrupting their own draft. Outcomes: cancel the **Decision phase** (cheap, no public artifact yet), cancel the **Speaking phase** before the point-of-no-return (the in-flight draft is discarded and replaced with an emote about being interrupted), or commit and acknowledge the missed message on the next turn via a **Repair hint**. Per-persona; one race evaluation per in-flight pipeline per new message. See ADR 0001.
_Avoid_: interruption, preemption, barge-in.

**Repair hint**:
A one-shot Levelt-style speech-repair cue stashed by the **Stop-signal race** when a **Persona** committed past the point-of-no-return (or chose to continue) while a relevant message arrived. Consumed by the next **Decision phase** for that **Room** and cleared regardless of outcome — surfaces the missed message to the persona so they can acknowledge it naturally. In-memory only.
_Avoid_: catch-up, backlog, missed-message-marker.

> Note: the code spelling `Generation` (the `Services/Generation/` namespace, `GenerationSession`, `GenerationResult`, `GenerationParticipant`, `InFlightPhase.Generation`, `_ctsByGeneration`, `CancelGenerationAsync`, etc.) predates the **Response pipeline** vocabulary — treat **Response pipeline** (umbrella) and **Speaking phase** (per-phase) as canonical in new code, comments, issues, and docs. Existing `Generation*` spellings can be renamed opportunistically (`Pipeline*` for umbrella scope, `Speaking*` for phase scope).

### Memory

A Persona's memory is the set of edges from a **Participant** (or the underlying **Persona**) to entities in **Reality**. The edges carry the personal view — the entities themselves do not.

**Recollection**:
A **Participant**'s edge to an **Event** in **Reality**. Carries a short, second-person snippet ("you saw Vlad cut Hana off"). One Event can have many Recollections — one per Participant who remembers it, each with their own spin. Snippet-only for now; if a recollection *changes how the Persona feels* about someone or something, that lives as a separate **Stance**.
_Avoid_: episode, snippet, memory-line, recall.

**Stance**:
A Persona's feeling/opinion/orientation toward a target — another **Participant** ("Vlad is impatient"), a **Concept** ("Lisp is elegant"), or themselves. Carries valence and free-text reasoning. First-class entity — *not* prose buried in a bio.
_Avoid_: opinion, attitude, feeling, take, belief.

**Intrinsic Stance**:
A **Stance** attached at the **Persona** library level — part of who the Persona *is*. Travels into every **Party** the Persona joins. Authored in persona-management UI, or **promoted** from an **Acquired Stance**.
_Avoid_: baseline-stance, default-stance, hardcoded-stance.

**Acquired Stance**:
A **Stance** formed during play, attached to a **Participant** — local to one **Party**. Stays there unless **promoted**.
_Avoid_: learned-stance, runtime-stance, in-party-stance.

**Promotion**:
The act of lifting an **Acquired Stance** to an **Intrinsic Stance** — "this is now part of who Denise is, not just Denise-in-this-Party." Author-driven, not automatic. Concretely: a new edge at **Persona** scope is written, capturing the *current projection* of the Acquired stance at the moment of promotion. The original Acquired observations stay where they happened.
_Avoid_: ascend, lift, propagate, graduate.

> Note: **Stance** is append-only — each capture writes a new edge with a timestamp, valence, and reasoning. The "current" stance is just the latest edge wins (per (Persona, Target)), unioned across Participant-scope and Persona-scope. No materialised projection table.

**Consolidation**:
A background pass that walks a **Participant**'s recent **Recollections** involving a target (a **Concept** or another **Participant**) and emits new **Stance** edges where a coherent belief has crystallised. Author-triggered (button or schedule) — *not* coupled to generation. Loose analogue to memory consolidation in sleep: episodes accumulate, beliefs crystallise later.
_Avoid_: digest, summarisation, reflection, dream.

### Reality

The shared, objective layer that **Personas** and **Participants** form **Stances** toward. Entities in Reality live independently of any Persona — multiple Personas can attach different Stances (and other edges) to the same entity. Think of Reality as the world; Memory is each Persona's view of that world.

**Concept**:
A "thing in reality" — abstract or concrete — that a Persona can have a **Stance** toward. "Lisp", "Software", "kindness". Flat for now (no hierarchy — see flagged ambiguities). Auto-created on first reference, mergeable in UI.
_Avoid_: topic, tag, thing, subject.

**Event**:
A crystallized moment in **Reality** that **Participants** may **Recollect**. An Event often *points to* a **Message** (or a span of Messages) when it happened in a Room — the Message holds the raw content, the Event is the anchor multiple **Recollections** hang off. Events can also represent backstory or non-Message reality (Scenario change, a Persona joining a Room) without a Message anchor. Events carry **objective** edges to the **Concepts** and **Participants** they are *about* (used by graph-walk recall) — these tags belong to the Event, not to any single Recollection of it. Subjective spin lives only on the Recollection edge.
_Avoid_: episode, fact, occurrence, snapshot.

## Relationships

- A **Party** contains zero or more **Rooms**.
- A **Party** has zero or more **Participants**.
- A **Participant** refers to exactly one **Persona**.
- A **Persona** can be a **Participant** in zero or more **Parties** (one Participant per Party).
- A **Room** draws its cast from its **Party**'s **Participants**.
- A **Participant** has exactly one **Driver** (`User` or `LLM`).

## Example dialogue

> **Dev:** When the human sends a message in a **Room**, what's the sender on that message?
> **Tim:** A **Participant** — specifically the one whose **Driver** is `User`. The human acts *through* a Persona; there's no separate "user message" path.
>
> **Dev:** So a **Persona** can be in multiple **Parties** with different Drivers in each?
> **Tim:** Yes — same Persona, different Participants. The Driver lives on the Participant. Each Participant has its own per-Party Memories too, so the same Persona will "remember" different things depending on which Party you meet them in.
>
> **Dev:** If I delete a **Persona** from the library, what happens to a **Room** that had them as cast?
> **Tim:** That's an ambiguity worth flagging — see below.

## Flagged ambiguities

- **"chat-group" / "ChatGroup"** was used throughout the backend to mean **Room** — resolved: **Room** is canonical; `ChatGroup` is a code-level legacy spelling that will be migrated opportunistically.
- **`IsUser` flag** on `PartyParticipant` is the current crude form of **Driver** — resolved in vocabulary (`Driver` is canonical, `IsUser` is the legacy field), code rename deferred until a third `DriverKind` appears or auth lands.
- **Persona deletion semantics** are not yet defined. If a Persona is deleted from the library, what happens to Participants referencing it (and to the messages they sent)? Soft-delete with tombstones? Hard delete with orphaned messages? Block deletion while participating? **Undecided.** Not blocking today — only set when this feature is built.
