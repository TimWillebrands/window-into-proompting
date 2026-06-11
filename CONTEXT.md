# Proompting

The domain language of Proompting (aka Partytown): a Windows-XP-themed app where one human and several AI **Personas** hang out in **Rooms** and talk.

## Language

**Party**:
A tenant boundary. Hosts a set of **Rooms** and a list of **Participants** (the **Personas** present in this Party). Each Party is a self-contained universe with its own cast, its own Rooms, and its own per-Persona **Memories**.
_Avoid_: server, workspace, tenant, world, desktop.

> Note: today the frontend always opens the default Party (id = `Guid.Empty`, exposed as `ROOT_PARTY_ID`). The API is fully multi-party; that surface is intentional, not dead code.

**Room**:
A named conversation thread inside a **Party** that a small cast of **Participants** talks in. Has its own message log, optional **Scenario** text, a subset of the Party's **Participants** as cast, and zero or more **Driver overrides**.
_Avoid_: chat-group, chat-room, channel, thread.

> Note: the backend code currently spells this `ChatGroup` (`ChatGroupGrain`, `ChatGroupEvent`, `chatGroupId` in API paths). That spelling is legacy — treat `Room` as the canonical name in all new code, comments, issues, and docs. Existing code can be renamed opportunistically.

**Persona**:
A character with a name, bio, system prompt, and behavioural dials (**Chattiness**, **Impulsivity**). Lives outside any Party — Personas are library entries that can join multiple Parties (eventually) and accumulate per-Party **Memories**. A Persona is a definition, not a membership.
_Avoid_: character, agent, bot, NPC, user.

**Narrator**:
The singleton library **Persona** representing un-personed speech in a **Room** — narration ("Vlad enters the room"), scenario-voice, ambient description. Joins every **Party** as a **Participant** with **Default Driver** = `System` so the **Response pipeline** never auto-generates from it. Authored content (imports, scripted scenes) attributes to the Narrator when no specific Persona owns the line. Accumulates **Memories** like any other Participant — narrator-as-observer remembers what happened, available to future scripted narration.

**Participant**:
A **Persona**'s membership in a **Party**. The link object that says "Persona X is in Party Y, defaulting to Driver Z." Rooms draw their cast from the Party's Participants. Removing a Persona from a Party deletes the Participant; the Persona itself remains in the library. The Participant owns the **Default Driver** — the same Persona can be User-driven by default in one Party and LLM-driven by default in another.
_Avoid_: member, attendee, party-member.

**Driver**:
The thing currently operating a **Participant** in a given **Room**. One of three kinds: `User` (a human types the messages), `LLM` (the agent stack generates them), or `System` (a sentinel for un-personed voices — narration, scenario-voice, scripted NPCs — that the **Response pipeline** never auto-generates from). Driver is contextual: every reference to "the Driver" in a Room means the **Effective Driver** for that (Participant, Room) pair. Two flavours of stored Driver back this up:
_Avoid_: operator, pilot, controller, agent.

**Default Driver**:
The **Driver** stored on a **Participant** — what applies in every Room of the Party unless that Room overrides it. Lives at Party scope. Set when the Participant is created (or edited via Party-level UI).

**Driver override**:
A per-Persona override stored on a **Room** — "in *this* Room, Persona X is driven differently from the Party default." Sparse: present only where the Room intentionally diverges. Lets the same Participant be User-driven in one Room and LLM-driven in another within the same Party (e.g. drop into the kitchen Room and watch Vlad-as-LLM chat with Denise even though Vlad is normally your User character).
_Avoid_: room-driver, local-driver, room-override.

**Effective Driver**:
The resolution rule consulted by the **Response pipeline** and any other consumer that needs to know "who is operating this Participant *right now*": `Driver override` for this (Persona, Room) if present, else the Participant's `Default Driver`. Always defined. Consumers see the Effective Driver only; the distinction between default and overridden is invisible to them.
_Avoid_: resolved-driver, current-driver.

> Note: backend storage for **Default Driver** is now `enum DriverKind` (`User | LLM | System`) on `PartyParticipant` — the legacy `bool IsUser` field was removed when the **Narrator** work introduced the third kind. Pipeline-internal types (`CastMember`, `ParticipantView`, `SelfView`) and Room-level override storage already used `DriverKind`. See [ADR 0012](docs/adr/0012-driver-system-and-driverkind-migration.md).

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

### Relevance realization

**Relevance Realization**:
The one faculty behind every "what matters right now?" judgement a **Persona** makes (term from John Vervaeke — deeper background is outside this glossary). Relevance is never computed from a fixed rule; it is realized by balancing opposing pulls (recent ⇄ relevant, speak ⇄ hold back, finish ⇄ interrupt). Four instances of it run in this project, at four timescales:

- **Interruption** (milliseconds) — is the new message worth abandoning my in-flight reply? Scored as **Salience** during the **Stop-signal race**.
- **Urge** (seconds) — is this beat worth speaking into? Feeds the **Decision phase**.
- **Recall** (days, backward) — which past moments belong in this beat?
- **Capture** (days, forward) — which present moments are worth remembering at all?

_Avoid_: relevance scoring, importance ranking, attention.

**Salience**:
How strongly something *pulls* on a **Persona**'s attention right now — an attractor acting on them from the **Arena**, the moment-to-moment output of **Relevance Realization**. Always relational: nothing is salient in itself, only salient *to this Persona in this beat*. Scored today during the **Stop-signal race** (is the interrupting message worth it?); the same notion ranks which **Recollections** surface at **Recall**. A Persona's memories under this ranking form their **salience landscape** — what looms large vs what has faded — reshaped by use: recalled memories strengthen, untouched ones sink.
_Avoid_: activation, importance, relevance score, weight.

**Urge**:
The inner pressure a **Persona** feels to act — a push from within, where **Salience** is pull from without. Today the only urge is the urge to speak: built up by being named, open questions, long silence, and the Persona's own **Chattiness**, consulted by the **Decision phase**.
_Avoid_: drive, motivation, impulse (see **Impulsivity**, a different thing).

**Recall**:
Surfacing past knowing into a **Persona**'s present beat — the backward-facing instance of **Relevance Realization**, ranked by **Salience**. The **Decision phase** receives what surfaces and carries at most one **Recollection** into the **Speaking phase**.
_Avoid_: retrieval, query, lookup, RAG.

**Capture**:
Crystallizing a present moment into memory: one marked **Message** becomes an **Event** (with its **Concept** tags) plus per-**Participant** **Recollections**. Capture writes the propositional and perspectival layers only — never the participatory. Beliefs don't crystallise *during* an experience but in the rest after it, so **Stances** form retroactively via **Consolidation**. A Capture may be triggered by an author or by spiking **Salience**.
_Avoid_: extraction, ingestion, save, remember-flow.

### Memory

A Persona's memory is the set of edges from a **Participant** (or the underlying **Persona**) to entities in the **Arena**. The edges carry the personal view — the entities themselves do not.

Memory is organized by Vervaeke's four kinds of knowing. Each kind is a layer; the entities beneath it implement that layer. No entity is purely one kind — a **Recollection** carries propositional content and can later shift a **Stance** — the layer names where that kind of knowing *predominantly* lives.

**Propositional** (knowing *that*):
Shared facts in the **Arena** — **Event** and **Concept**. Persona-independent; the things Recollections and Stances attach to.

**Perspectival** (knowing *what it was like from here*):
A Persona's view of moments — **Recollection**. What stood out to *them*, with their spin.

**Participatory** (knowing *by being in relation*):
A Persona's orientation toward things in the **Arena** — **Stance**. Identity-shaping: these edges say who the Persona is relative to their world.

**Procedural** (knowing *how*):
A Persona's engagement style. Today static — **Chattiness**, **Impulsivity**, the persona system prompt. Learned procedural memory is an open slot, not yet an entity.

**Recollection**:
A **Participant**'s edge to an **Event** in the **Arena**. Carries a short, second-person snippet ("you saw Vlad cut Hana off"). One Event can have many Recollections — one per Participant who remembers it, each with their own spin. If a recollection *changes how the Persona feels* about someone or something, that lives as a separate **Stance**.
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

> Note: **Stance** is append-only — every write appends a new edge with a timestamp, valence, and reasoning. The "current" stance is just the latest edge wins (per (Persona, Target)), unioned across Participant-scope and Persona-scope. No materialised projection table.

**Consolidation**:
The pass that moves knowing from the perspectival layer to the participatory: walks a **Participant**'s recent **Recollections** involving a target (a **Concept** or another **Participant**) and emits new **Stance** edges where a coherent belief has crystallised. Runs in rest, not in the moment — the analogue of memory consolidation in sleep — and is deliberately retroactive: what a moment *meant* is decided after the fact, and may differ from how it felt live.
_Avoid_: digest, summarisation, reflection, dream.

### Arena

The shared stage a **Party**'s cast acts on (term from Vervaeke's agent–arena coupling — deeper background outside this glossary): every entity the **Personas** can perceive, recall, and form **Stances** toward. One Arena per Party. Entities in the Arena live independently of any Persona — multiple Personas can attach different Stances (and other edges) to the same entity. Memory is each Persona's *relation* to the Arena — which is why the personal view lives on the edges and never on the entities.
_Avoid_: reality, world, environment, setting.

**Concept**:
A thing in the **Arena** — abstract or concrete — that a Persona can have a **Stance** toward. "Lisp", "Software", "kindness". Flat — no hierarchy. Auto-created on first reference.
_Avoid_: topic, tag, thing, subject.

**Event**:
A crystallized moment in the **Arena** that **Participants** may **Recollect**. An Event often *points to* a **Message** (or a span of Messages) when it happened in a Room — the Message holds the raw content, the Event is the anchor multiple **Recollections** hang off. Events can also represent backstory or non-Message happenings in the **Arena** (Scenario change, a Persona joining a Room) without a Message anchor. Events carry **objective** edges to the **Concepts** and **Participants** they are *about* (used by graph-walk recall) — these tags belong to the Event, not to any single Recollection of it. Subjective spin lives only on the Recollection edge.
_Avoid_: episode, fact, occurrence, snapshot.

### The Bench (development)

Tooling vocabulary — these terms describe how Proompting is *developed*, not what it is. See [ADR 0011](docs/adr/0011-bench-probe-runner.md).

**Bench**:
The headless console host (`tools/bench`) that runs **Probes** against the real grain graph — real router, real endpoint grains, in-memory storage, no UI. The place where LLM-driven features are pointedly exercised during development.
_Avoid_: test-harness, sandbox, playground, simulator.

**Probe**:
A pointed runner of one subsystem slice (a service, a grain, or a multi-grain interaction), written as a plain C# method. Observes rather than asserts — its deliverable is a **Probe Artifact**, not a pass/fail. Assertion-style coverage stays in `backend-test/`.
_Avoid_: test, spec, scenario-test, benchmark.

**Probe Artifact**:
The structured output of one Probe run: composed prompts (captured at the endpoint-grain boundary), urge breakdowns, parsed decisions, raw model output, attribution, timing. Rendered to console and written to `bench-runs/` as JSON so runs can be read by an agent and diffed across iterations.
_Avoid_: report, log, result, trace.

**Bench Session**:
A deliberate development mode in which a driver-user has set up reachable LLM providers (usually local Ollama) for the Bench and told the working agent so. Verified with `bench doctor`. Without one, Probes still capture composed prompts but get no model output.
_Avoid_: dev-mode, test-mode, live-session.

## Relationships

- A **Party** contains zero or more **Rooms**.
- A **Party** has zero or more **Participants**.
- A **Participant** refers to exactly one **Persona**.
- A **Persona** can be a **Participant** in zero or more **Parties** (one Participant per Party).
- A **Room** draws its cast from its **Party**'s **Participants**.
- A **Room** may carry zero or more **Driver overrides**, one per overridden Persona.
- A **Participant** has exactly one **Default Driver** (`User`, `LLM`, or `System`).
- The **Effective Driver** in a given Room is that Room's **Driver override** for the Persona if present, else the Participant's **Default Driver**.
- Every **Party** auto-carries the singleton **Narrator** as a **Participant** with **Default Driver** = `System`.

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
- ~~**`IsUser` flag** on `PartyParticipant` is the storage form of **Default Driver**~~ — **resolved by [ADR 0012](docs/adr/0012-driver-system-and-driverkind-migration.md)**: storage migrated to `enum DriverKind` (`User | LLM | System`) when the third kind arrived alongside the Narrator.
- **Persona deletion semantics** are not yet defined. If a Persona is deleted from the library, what happens to Participants referencing it (and to the messages they sent)? Soft-delete with tombstones? Hard delete with orphaned messages? Block deletion while participating? **Undecided.** Not blocking today — only set when this feature is built.
