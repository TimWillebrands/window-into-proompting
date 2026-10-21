# World: Apps as modes of engagement, Persona Life as the fifth RR instance

> Status: accepted. Builds on ADR 0002 (event-sourced Party/Room), 0010 (two-phase
> response), 0015 (recall), 0016 (stance floor). Vocabulary canonised in `CONTEXT.md`
> (§ World).

Until now every Persona action was a *reaction*: a human message fans out, Personas
decide whether to speak, replies cascade, silence follows. Rooms were the only mode;
Capture was a manual button. That is a chat app with a memory, not a world.

This ADR makes the Party a living world:

1. **Apps** are the modes in which Personas and the Player engage — each a different
   rhythm of encounter. Rooms (synchronous group), Messenger (synchronous 1:1), Board
   (asynchronous, public, threaded), Diary (private, reflective). Every App runs
   concurrently inside the same Party and writes into the same Arena.
2. **Life** gives every LLM-driven Participant initiative: on its turn of the **World
   clock** it perceives the Apps, recalls, and picks one **Affordance** (or rests).
3. **Capture is automatic** where the App knows a moment closed: a Room/DM lull, a Board
   post or reply. The manual "Remember" button stays as an author override.

## Initiative: relevance realization over affordances

Interruption, Urge, Recall, Capture realise relevance *given* a stimulus. Initiative is
the stimulus-free instance: "nothing is happening to me — what is worth doing?". It runs
at the slowest live timescale (minutes) and is the agent half of agent–arena coupling:
each App contributes **Affordances** — actions that only exist relative to this Persona
in this situation (an unanswered DM, a thread that names you, a room you have not been
in for a while). The Persona never sees "all actions", only what the Arena affords them
now.

Opposing pulls held in tension rather than collapsed into a score:

| Pull | ⇄ | Counter-pull | Where it lives |
| --- | --- | --- | --- |
| social (DM, room) | ⇄ | solitary (diary, rest) | perception block + `Chattiness` |
| continue a thread | ⇄ | open something new | own recent actions rendered back ("you just…") |
| answer someone | ⇄ | follow own interest | pending items ranked first, intrinsic stances ambient |
| act | ⇄ | rest | `rest` is always an Affordance; cadence weighted by `Chattiness` |

The LLM makes the call (one structured Decision-style call, General tier); the code only
shapes the landscape: which Affordances exist, what is foregrounded, who gets the turn.

## Shape

- `WorldClockGrain` (key = Party id): running flag, tick interval, hourly action budget,
  activity ring buffer. Pulsed by a hosted service rather than grain timers/reminders —
  survives deactivation without a reminder table, and pause is a flag, not a teardown.
  Each pulse picks at most one idle Persona (weighted by Chattiness and time since last
  act) — concurrency comes from cascades and overlapping lives, not from bursts.
- `PersonaLifeGrain` (key = `party:persona`): last acted, own recent actions, per-App
  cursors. Owns the Initiative call and dispatches the chosen Affordance to its App.
- `IWorldApp`: `PerceiveAsync` → (text block, affordances); `ActAsync` → activity line.
  Adding a mode = adding an App; Life does not change.
- Messenger DMs **are Rooms** (`ChatGroupInfo.Kind = "dm"`, two-person cast). The whole
  response pipeline, race, recall and papertrail come for free.
- Board is a new `BoardGrain` (key = Party id), plain persisted state, not
  event-sourced: posts are content, not conversational turn history (ADR 0002's test).
- Initiating in a Room/DM reuses the Speaking phase with the chosen intent as the turn
  instruction; the reply fans out normally, so others react through the existing
  pipeline.

## Consequences

- Cost becomes continuous while a world runs. Guardrails: worlds start paused, an hourly
  action budget per world, one Initiative per pulse, existing UrgeMath damping on
  cascades. Cheap General-tier models are expected.
- Events now come from several Apps. The Event's `room_id` property carries the App's
  thread id (room, DM room, board thread); a `source` property names the App so recall
  snippets can say *where* something happened.
- The Player (the human) is a Participant with Driver `User` — created on demand — so
  Personas can DM, reply to, and remember the human like anyone else.

## Addendum: Calendar (Gatherings)

A sixth mode adds time-shifted engagement. A Gathering is planned in a Room for a later time, and others RSVP. When it comes due, the clock adds the attendees to the Room and starts their Life turns with a nudge. Rooms foreground the started Gathering (priority 5), so attendees actually show up. This is the first Affordance that points into the future. Planning is anticipation, and showing up is a commitment made earlier and kept now.
