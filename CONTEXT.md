# Proompting

The domain language of Proompting (aka Partytown): a Windows-XP-themed desktop where humans and AI **Personas** live together in a **World** — chatting in **Rooms**, messaging privately, posting on a **Board**, keeping **Diaries** — and remember what happened.

## Language

**Party**:
A tenant boundary. Hosts a set of **Rooms** and a list of **Participants** (the **Personas** present in this Party). Each Party is a self-contained universe with its own cast, its own Rooms, and its own per-Persona **Memories**. When it is running, a Party is a **World** (see § World) — same entity, the word used when talking about its clock and Apps; API routes `/worlds/{partyId}`.
_Avoid_: server, workspace, tenant, desktop.

> Note: today the frontend always opens the default Party (id = `Guid.Empty`, exposed as `ROOT_PARTY_ID`). The API is fully multi-party; that surface is intentional, not dead code.

**Room**:
A named conversation thread inside a **Party** that a small cast of **Participants** talks in. Has its own message log, optional **Scenario** text, a subset of the Party's **Participants** as cast, and zero or more **Driver overrides**.
_Avoid_: chat-group, chat-room, channel, thread.

> Note: the backend code currently spells this `ChatGroup` (`ChatGroupGrain`, `ChatGroupEvent`, `chatGroupId` in API paths). That spelling is legacy — treat `Room` as the canonical name in all new code, comments, issues, and docs. Existing code can be renamed opportunistically.

**Persona**:
A character with a name, bio, system prompt, and behavioural dials (**Chattiness**, **Impulsivity**). Lives outside any Party — Personas are library entries that can join multiple Parties (eventually) and accumulate per-Party **Memories**. A Persona is a definition, not a membership.
_Avoid_: character, agent, bot, NPC, user.

**Nature**:
The half of a **Persona** that is who they are, wherever they go: Bio, system prompt, dials, **Intrinsic Stances**. Lives on the Persona and travels into every **Party**.
_Avoid_: personality, traits, sheet, template.

**Nurture**:
The half of a **Persona** that is what happened to them: **Events**, **Recollections** and **Acquired Stances** in one **Party**. Lives on the **Participant**; begins at **Arrival** and grows through play.
_Avoid_: backstory, history, lore, memories (too broad).

**Narrator**:
The singleton library **Persona** representing un-personed speech in a **Room** — narration ("Vlad enters the room"), scenario-voice, ambient description. Joins every **Party** as a **Participant** with **Default Driver** = `System` so the **Response pipeline** never auto-generates from it. Authored content (scripted scenes) attributes to the Narrator when no specific Persona owns the line. Accumulates **Memories** like any other Participant — narrator-as-observer remembers what happened, available to future scripted narration.

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

> Code home: `RoomCast` (`IChatGroupGrain.GetCastAsync`) — the present cast with Effective Drivers resolved. Consumers ask it, never re-derive.

**Scenario**:
Free-text in-fiction setup for a **Room**: where we are, what's going on, what the mood is. Visible to (and influences) every Persona in the Room. Optional — Rooms can have no Scenario.
_Avoid_: prompt, setting, premise, context.

**Arrival**:
A **Persona** joining a **Party** with their **Nurture** seeded, so they don't arrive as a blank slate. An agent works a **Seed corpus** into an **Arrival draft**; the author revises it by prompting and accepts; only then is it committed. Author-driven only. See [ADR 0022](docs/adr/0022-arrival-replaces-import.md).
_Avoid_: import, onboarding, spawn, seeding (as a noun).

**Seed corpus**:
The free-text material an **Arrival** works from: vibes, rough ideas, concrete pointers ("Denise knows Vlad from five years ago, when he lived in Bangladesh"), pasted stories or chat logs. No format.
_Avoid_: import file, backstory doc, prompt.

**Arrival draft**:
The dry-run result of an **Arrival**: the newcomer's Events, Recollections and Stances, plus additions to existing **Participants**' **Nurture** where the corpus touches them. Revised by prompting; nothing reaches the memory graph until accepted.
_Avoid_: preview, proposal, staging.

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
- **Initiative** (minutes, stimulus-free) — with nothing asking anything of me, what is worth doing? See § World.

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
Crystallizing a present moment into memory: one marked **Message** becomes an **Event** (with its **Concept** tags) plus per-**Participant** **Recollections**. Capture writes the propositional and perspectival layers only — never the participatory. Beliefs don't crystallise *during* an experience but in the rest after it, so **Stances** form retroactively via **Consolidation**. A Capture may be triggered by an author ("Remember"), by a lull closing a Room/DM conversation, or by a Board post (ADR 0018).
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

### World

See [ADR 0018](docs/adr/0018-world-apps-and-persona-life.md).

**World**:
A **Party** seen as a living place: its **World clock**, its **Apps**, and the **Lives** of its Participants. Not a separate entity — "World" is the Party in motion.
_Avoid_: simulation, sandbox, game.

**App**:
A mode of engagement inside a World — a distinct rhythm in which Personas and the Player meet. Today: **Rooms** (synchronous group, D'Accord), **Messenger** (synchronous 1:1), **Board** (asynchronous, public, threaded), **Diary** (private, reflective), **Calendar** (future-facing: plan and attend **Gatherings**). All Apps run concurrently and write into the same **Arena**. In code, `IWorldApp`: perceive → **Affordances**; act → activity.
_Avoid_: feature, channel, module, tool.

**Messenger thread** (DM):
A two-person **Room** (`ChatGroupInfo.Kind = "dm"`). Everything a Room has — Response pipeline, race, recall — applies.
_Avoid_: private room, whisper.

**Gathering**:
A get-together planned in a **Room** for a later time, on the World's **Calendar** App. Personas and humans plan them and RSVP. When one comes due, the **World clock** adds the attendees to the Room and gives each a **Life** turn pointed at it. It gives Initiative a future-facing arm: anticipation.
_Avoid_: event (taken — see **Event**), meeting, appointment.

**Board**:
The World's public forum: threads with replies. Each post or reply is **Captured** for the thread's Participants.

**Diary**:
A Persona's private journal in one Party. Writing in it is rest: **Consolidation** runs alongside, so the Diary is the visible face of beliefs settling.

**Player**:
A human's **Participant** (Driver `User`), created on demand, so Personas can DM, answer, and remember the human like anyone else.
_Avoid_: user (ambiguous with Driver `User`), admin, curator.

**World clock**:
Per-Party heartbeat. While running, each tick hands one idle LLM Participant a **Life** turn (weighted by **Chattiness**, time since they last acted, and whether a **Notification** landed since), within an hourly action budget, and has the lull capture close quiet conversations into memory. Worlds start paused.
_Avoid_: scheduler, game loop, cron.

**Life**:
A Persona's autonomous existence in one Party. One Life turn = glance at every App, recall, make one **Initiative** call, act on one **Affordance** (or rest). If the Affordance was a **Look**, a second choice follows over what is now in view.
_Avoid_: agent loop, AI turn, routine.

**Initiative**:
The fifth, stimulus-free instance of **Relevance Realization**: "nothing is asking anything of me — what is worth doing?". Minutes timescale. The code shapes the landscape (which Affordances exist, what is foregrounded); the Persona chooses.
_Avoid_: planner, scheduler, motivation.

**Attention**:
What a Persona has in mind of an App, as opposed to what exists in it. Ambient Apps (Places, Together, Diary, Wardrobe) are perceived in full. Screen Apps are only *glanced* at: a **Notification** or badge for what concerns you, and the full lines only when they are **in mind** — a chat you're mid-exchange in, or something you **Looked** at in the last 15 minutes. The same window governs speaking: a reply sees the live exchange and the few lines on screen above it, not the whole transcript. See ADR 0018 § Attention.
_Avoid_: context, visibility, read state.

**Look**:
An Affordance that only brings something into view — open the Board, scroll back through a chat, catch up on a Room. Costs no LLM voice; Life follows it with a choice over what is now in mind.
_Avoid_: browse tool, fetch, search.

**Notification**:
What reaches a Persona from a screen App without opening it: "Ana: 2 new messages", "Bo replied to you in …". Board posts that concern a Persona also make the **World clock** likelier to give them the next turn.
_Avoid_: alert, ping, event.

**Lived self**:
What a Persona brings into every prompt besides their sheet. It covers today's ordinary offscreen **Day** (timed mundane beats, mood, energy, one practical preoccupation), their **Social battery** (drained by talking, recharged by time and sleep), the words that have **gone stale** in the world or in their own mouth, their **Ties** (how well they know each person present, and when they last talked), and the Room's **thermostat** (a System One read of whether the talk has been heavy and circling too long). The sheet is background, not a script. See [ADR 0021](docs/adr/0021-lived-self.md).
_Avoid_: persona state, stats, needs.

**Wallet**:
A participant's money in a Party: a balance in credits paid once a day by their **Means** (tight, getting by, comfortable), a ledger, and a **Wardrobe** of clothes they own or were given. Buying, changing clothes and giving presents happen through the Wardrobe app. See [ADR 0024](docs/adr/0024-micro-economy.md).
_Avoid_: account, inventory, currency (the unit is just "credits").

**Affordance**:
An action an App offers *this* Persona *now* — "reply to Denise's DM", "say something in #kitchen". Relational, like Salience: an unanswered DM is an Affordance only for its recipient. Priority foregrounds; it never forces.
_Avoid_: action, option, tool, command.

**System One judge**:
The fast, calibrated *weighing* half of a Relevance Realization call: state + typed questions → probabilities, no prose (TypeSafe Jev, or an LLM emulation). The *voicing* half stays with the LLM. Initiative samples from its distribution rather than taking the top pick. See [ADR 0019](docs/adr/0019-system-one-judge.md).
_Avoid_: classifier, scorer, router.

**Activity**:
One line in the World's activity log: who did what in which App, with the Initiative thought that led there.
_Avoid_: event (taken — see **Event**), log entry.

### Places

See [ADR 0020](docs/adr/0020-embodiment-one-body-one-place.md).

**Body**:
A Participant's physical presence in the World. Exactly one per Participant, always in exactly one **Place**, at a named **Spot**, with a posture and maybe something it's busy with ("on their phone, texting").
_Avoid_: avatar (the sprite, not the thing), character, sprite, NPC.

**Place**:
A physical location in the World: a **Home**, the Lobby, the Café, the city centre (outdoors). Has named **Spots** and **Furni**. Events captured there carry its `place_id`, so being back there brings them to mind.
_Avoid_: room (taken, see **Room**), location, map, level.

**Spot**:
A named place to be inside a Place ("the window table"). Canonical: Personas reason in Spots, never tiles; the iso renderer derives tiles from them.
_Avoid_: tile, position, seat.

**Home**:
The Place that belongs to one Participant. Private: others knock, or are invited, and get let in as guests (cleared when the owner leaves). Where its owner sleeps and decorates.

**Furni**:
A placed object with state (a plant that dries, cups that pile up, a jukebox that plays). Decay is computed from timestamps. Care is capped to one item per Place.
_Avoid_: item, object, prop.

**Situation**:
The one-line, Body-derived "Where you are" in every prompt that speaks as a Participant. The Body wins for *where*; the off-screen day is the life around it.
_Avoid_: context, location string, environment.

**Place talk**:
Speech between Bodies in a Place. A Room with `Kind = "place"` whose cast follows the occupants, delivered per line to its **Earshot**.

**Earshot**:
Who hears one line, fixed when it's spoken: talk reaches your Spot, a shout the whole Place, a whisper one addressee next to you. Stored as `HeardBy` on the message; `null` means everyone (Rooms, DMs).
_Avoid_: range, radius, audience.

**Asleep**:
A Body in bed at night. The World clock skips it and persona talk doesn't wake it; a human's message does (groggy for a while). Falling asleep runs **Consolidation**.

**Appearance**:
How a Body looks: named choices from a fixed palette (skin, hair, style, clothes, accessory). Personas dress themselves once from their bio; Players pick theirs.
_Avoid_: avatar, sprite, skin (as a whole).

**Overheard**:
Talk in your Place that you were out of Earshot of: you get the tone and maybe your name, never the words.
_Avoid_: eavesdrop log, muffled message.

**Scene**:
How a Place looks in 3D: an ASCII map plus **Prefabs** by name, pinning the Place's Spots and Furni to tiles. Presentation only; Places nobody drew get a generated one. See [ADR 0023](docs/adr/0023-places-scenes-and-prefabs.md).
_Avoid_: level, map (the ASCII is one part of it), room layout.

**Pose**:
How a Body is hanging out at its spot: leaning, lounging, perched, dancing, or a pair pose with someone next to you (a hug, a slow dance). Spots list the solo poses they afford; walking off ends a pose. See [ADR 0025](docs/adr/0025-places-making-and-hanging-out.md).
_Avoid_: animation, emote (taken: race-cancelled lines), stance (taken).

**Made Place**:
A public Place designed by the LLM from a description (spots plus a scene), checked before it exists; has a `MadeBy` instead of an owner.
_Avoid_: custom room, user level.

**Prefab**:
A named, reusable 3D thing built from coloured primitives (a sofa, a canal house, a herring cart), with seats, tints and a variant seed; Furni state can restyle it.
_Avoid_: model, asset, mesh.

### Occupation, responsibilities and desires

See [ADR 0026](docs/adr/0026-occupations-responsibilities-desires.md). Rule: these reach prompts as facts ("shift at 9, you're late"), never as traits.

**Purpose**:
What a **Place** is for, in one plain line ("where people get coffee and linger").
_Avoid_: telos, theme, description (that's the look).

**Role**:
A Participant's standing in a **Place**, derived and not stored: Owner, Staff (works there), Regular (there on 3+ of the last 7 days), Newcomer. Owners and Staff get host Affordances.
_Avoid_: rank, permission, membership.

**Occupation**:
What a Participant does for a living in one Party: a job title, a workplace, hours and pay per shift. Onscreen when the workplace is a Place in the Party (the Body goes to a Staff spot), offscreen otherwise (the Body is away at a **Locale**, and the shift is a fixed beat in the **Day**). Started and ended by the **World clock**, not chosen. Pays into the **Wallet**; **Means** is the fallback for people with no Occupation.
_Avoid_: job (fine in prose), career, class.

**Responsibility**:
Something someone holds a Persona to. Always has a holder (a Participant, a Place, or someone offscreen) and a due time. Today it comes from a shift, a Gathering you RSVP'd to or are hosting, or guests in your Home. It renders as "# What you owe" in Right now. Keeping one warms the Tie; neglecting one costs something visible.
_Avoid_: task, duty, chore, quest, obligation.

**Desire**:
Something a Persona wants, either *having* (a thing, money, someone's attention) or *being* (becoming someone: a craft, a mended tie). An **Intrinsic Desire** is on the Persona (authored, or distilled from the lived sheet). An **Acquired Desire** forms in play on the Participant, fades after two untouched weeks, and can be **Promoted**. Desires steer Initiative only; Speaking never sees them.
_Avoid_: goal, need, drive (collides with **Driver**), motivation, objective.

**Mimesis**:
An **Acquired Desire** caught from someone through a **Tie**: wanting what they have or want. Capped hard: a Tie of 10+ exchanges, one at a time, and a 3-day gap between catches. Wanting the same person's attention as the one you caught it from becomes a **Rivalry**, shown as Tie friction, with at most one live per World.
_Avoid_: envy, contagion, imitation.

### World growth

See [ADR 0027](docs/adr/0027-world-growth-districts-locales-openings.md) (proposed). Rule: fidelity follows use; draw where people meet, not where people are mentioned.

**Locale**:
A named **Place** that exists only offscreen ("the office in Zuid", "Ana's work"): never drawn, no Place talk, not somewhere you walk to. A **Body** is *away* there (`AwayAs`: "at work") and comes back. Keeps its id when **Opened**.
_Avoid_: elsewhere (that's the Life prompt's list of other Places), offscreen place, stub, location.

**District**:
A part of the city that holds **Places**; the old centre is the first. Walking within one takes seconds; between Districts it's a short tram ride.
_Avoid_: zone, area, region, neighbourhood (fine in prose).

**Whereabout**:
A stretch of a Persona's **Day** spent away from home ("09:00–17:30 at the office in Zuid"), written with the Day and resolved to a **Locale**. One kind of **Footfall**.
_Avoid_: location, schedule entry.

**Errand**:
A **Whereabout** long enough (2 h+) that the **Body** actually goes away for it, once, and comes back after. Shifts are the **Occupation**'s, not errands.
_Avoid_: trip, outing.

**Anchor**:
A **Participant**'s standing tie to a **Locale** or **Place**: lives, works, studies, regular. Read from the **Occupation**, the lived sheet and visit days; never stored twice.
_Avoid_: home base, affiliation.

**Footfall**:
The decaying pressure on a **Locale** from anchors, Day beats and talk, capped per **Participant**. An **Opening** needs enough of it, from at least two people, over at least two days.
_Avoid_: popularity, heat, score.

**Opening**:
A **Locale** becoming a drawn **Place**, at the lowest fidelity it has earned (sketched, kit-drawn, bespoke). Concrete plans (a Gathering there, the Player visiting) open it at once.
_Avoid_: spawn, generate, promotion (taken: **Promotion**).

**Kit**:
A validated archetype scene (office, bar, shop, park…) a Place is drawn from by filling slots, not designed.
_Avoid_: template (fine in prose), preset.

**Dormant**:
An opened **Place** that lost its **Footfall**: kept, but not foregrounded in Life.

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
- A **World** (Party) has one **World clock**, one **Board**, one **Diary** per Participant, and any number of **Rooms** and **Messenger threads**.
- A **Player** is a **Participant** with **Default Driver** = `User`.
- A **Participant** has exactly one **Body**; a **Body** is in exactly one **Place**, at one **Spot**.
- A **Place** has one **Place talk** Room; a **Home** belongs to one **Participant**.
- A **Place talk** message has one **Earshot**.
- A **Body** is in exactly one **Place**, which may be a **Locale** (then it's away).
- A **Place** belongs to one **District**; an **Opening** turns a **Locale** into a drawn **Place** with the same id.
- A **Persona**'s **Nature** is shared by all its **Participants**; each **Participant** has its own **Nurture**.
- A **Place** has one **Purpose**; a **Participant** has one **Role** per Place they relate to.
- A **Participant** has zero or one **Occupation**, whose workplace is a **Place** in the Party or offscreen.
- A **Responsibility** belongs to one **Participant** and has exactly one holder.
- A **Persona** has **Intrinsic Desires**; each **Participant** may add **Acquired Desires**.
- An **Arrival** seeds one **Participant**'s **Nurture** and may add to other **Participants**' **Nurture** in the same **Party**.

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
