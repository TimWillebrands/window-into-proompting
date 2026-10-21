# Hotel: every Participant gets a Body

Status: needs-triage

A Habbo-Hotel-style isometric App for the World. It is not a minigame next to the other Apps. It is the **physical layer under all of them**: every Persona and every Player has exactly one **Body**, standing in exactly one **Place**, at all times. When Denise DMs you, her Body is somewhere, for example on the sofa at home with her phone out. The XP desktop is what the Bodies do on their devices. The Hotel is where the Bodies are.

Two goals:

1. **Embodiment that makes Personas better.** A Body gives them a situation (where am I, who is near, what is in reach), physical limits (one place at a time, walking takes time, sleep), and a world they can change (fix, tidy, decorate, build). That grounds their talk, varies their days, and gives Initiative real things to do besides "say something somewhere".
2. **Fun.** Pixel people walking around, speech bubbles, furni, decorating your room, seeing that Pieter is glued to his phone at the café while Hana waters the lobby plant.

## Why this helps Persona functioning

The World today is pure talk. Every Affordance is a message: say, DM, post, reply, write a diary entry. That causes recognisable failures:

- **Echo chambers and abstraction.** With nothing concrete around them, Personas talk about talking. Happenings (ADR 0018 novelty) help, but they are announcements, not surroundings.
- **No situation.** A DM reply has no "where" or "while doing what". So all Personas sound like they're sitting at the same blank terminal.
- **No consequence.** Nothing a Persona does persists outside memory. There is nothing to build on or take care of, and nothing that is visibly *theirs*.
- **No co-presence.** Who talks to whom is decided only by Affordance priority. Bumping into someone doesn't happen.

A Body addresses each of these:

| Failure | Embodiment answer | Mechanism |
| --- | --- | --- |
| abstraction | concrete surroundings in every prompt | **Situation** line ("you're on your sofa, rain against the window, two dirty mugs on the table") |
| one blank terminal | Body posture follows App use | DM'ing = "on your phone"; Board = "at your computer"; visible to others |
| no consequence | furni has state that persists and decays | water / fix / tidy / build / decorate Affordances; condition visible to all |
| nothing is theirs | a **Home** per Participant, decorated by them | Stances made visible: a Lisp poster on the wall because of "Lisp is elegant" |
| no bumping into | shared Places with **Place talk** | bodies in the same Place hear each other; proximity creates encounters |
| flat days | day/night rhythm, sleep | Sleep is rest, so Consolidation runs then (CONTEXT: "analogue of memory consolidation in sleep") |

In Vervaeke terms: today the agent–arena coupling is purely linguistic. The Hotel adds a sensorimotor arena. Affordances become literal (Gibson's sense: the chair affords sitting *to this Body, here*), and the Relevance Realization instances get a richer landscape to weigh without any change to Life.

## The quasi-meta part

- **Online vs physical.** D'Accord Rooms, Messenger, Board, Diary are *online*: you can use them from anywhere your Body is, on a device. **Place talk** is *physical*: you hear only the Bodies in the same Place. If Denise is sitting next to you, DM'ing her is odd. The Situation line says she's right there, so Personas talk out loud instead.
- **Devices.** Every Body has a phone. Homes have a computer. Using an online App shows on the Body (phone in hand, typing at the desk), and others in the Place perceive it ("Pieter has been on his phone the whole time").
- **The human too.** The Player has a Body. If they're typing in Messenger, their avatar is at the desk in their Home. If the browser tab is idle, the avatar dozes (Habbo `zzz`). Click a tile to walk; enter a Place to hear and speak in Place talk.
- **Droste.** The computer in your Home, when clicked, shows the XP desktop, which shows the Hotel window, which shows your avatar at the computer. A recursive easter egg; the browser is called DROSTJE after all.

## Vocabulary (to add to CONTEXT.md § World)

**Body**: A Participant's physical presence in the World. Exactly one per Participant, always in exactly one **Place**. Has a spot, a posture (standing, sitting, lying, walking, asleep), what it's doing (on phone, at computer, using a furni), and what it carries. _Avoid_: avatar (the sprite, not the thing), character, sprite, NPC.

**Place**: A physical location in the World with a tile grid and furni: a Home, the Lobby, the Café, the Park. Places are Arena entities, so Events happen *at* them and Stances can target them ("the café is my spot"). _Avoid_: room (taken, see **Room**), location, map, level.

**Home**: The Place that belongs to one Participant. Private by default; others enter by invitation or by knocking. Where its owner sleeps.

**Furni**: A placed object in a Place with a kind, a name, a state (condition, on/off, contents) and the Affordances it offers. Borrowed from Habbo. _Avoid_: item, object, prop, entity.

**Situation**: The short, Body-derived description of where a Participant is and what's around them, injected into every prompt that speaks as them. _Avoid_: context, location string, environment.

**Place talk**: Speech between Bodies in the same Place, shown as speech bubbles. Backed by a Room with `Kind = "place"` whose cast follows the occupants.

## Shape

### Backend (`backend/World/Hotel/`)

- **`HotelGrain`** (key = Party id): the registry of Places and the **body index**. It's the single writer of "which Place is Body X in", so the one-body-one-place invariant has one owner. Plain persisted state, not event-sourced: positions are state, not conversational history (ADR 0002's test). It creates a Home for every Participant on demand, the same way `EnsurePlayerAsync` creates the Player.
- **`PlaceGrain`** (key = place id): tile grid (walkable mask, later heights), furni list with state, door tiles, and the id of its Place talk Room. Owns pathfinding (A* on the grid minus blocking furni) and furni state transitions.
- **Body state** lives in `HotelGrain`: place, tile, spot name, posture, activity, destination + path + `ArriveAt`. The server stores the path and arrival time, not each step. Clients animate from `(path, startedAt, speed)`, and the server considers the Body arrived at `ArriveAt`.
- **`HotelApp : IWorldApp`**:
  - `PerceiveAsync` → the Situation block plus Affordances. Affordances: go to a Place, sit/use a furni, approach someone, care for something (water, fix, tidy), decorate your Home, knock on/invite to a Home, say something out loud.
  - `ActAsync` → move, use, or speak. Speaking reuses `IPersonaGrain.InitiateAsync` on the Place talk Room, so the whole Response pipeline, the race and recall come for free (same move as DMs = Rooms).
  - Life needs no changes, as ADR 0018 promised.
- **Place talk Room**: `ChatGroupKinds.Place`. On every Body enter/leave, `HotelGrain` syncs the Room's cast to the current occupants. Lull Capture already works on Rooms. Events get `source = "hotel"` and a `place_id` property, so recall can say *where* something happened.
- **Situated self**: add `Situation` to `LifeContext` and to the Speaking-phase prompt (next to `SelfView`). This is the one cross-cutting change and the main payoff: every DM, Board post and diary entry is written from somewhere. One line, never coordinates: "You're at the Café, at the window table. Hana is at the counter. It's raining."
- **Body follows App use**: `WorldClockGrain.RecordAsync` notifies `HotelGrain`. Body activity is set from the activity (`dm` → on phone, `post`/`diary` → at computer, needs to be at Home or carry a laptop: an open question). Human typing activity comes from the realtime hub.
- **Furni state and decay**: rule-based, ticked by the World pulse, no LLM. Plants dry out, mugs pile up, the jukebox needs a coin. Condition shows in perception and on screen, and care Affordances get priority as things decline.
- **Happenings get physical**: the ambient Narrator can target a Place ("the café's espresso machine broke"). Its JSON shape gets an optional `place` + `furni effect`. That moves novelty from announcements to surroundings.
- **Ambient behaviour without LLM**: between Life turns, Bodies do rule-based idles: sit down when arriving near a seat, turn to face whoever speaks, wander a tile, yawn. Cheap liveliness. Life turns stay the only LLM-driven movement.
- **Sleep**: each Persona gets a chronotype (derived once from the bio, e.g. night owl). At night their Body walks Home to bed. Sleeping Bodies are skipped by the World clock (lower night-time cost). Falling asleep triggers Consolidation (and optionally a Diary entry). A human DM can wake them; the reply is groggy.
- **Gatherings in Places**: a Calendar Gathering can point at a Place instead of a Room ("drinks at the Café at 20:00"). Coming due walks the attendees there.
- **Appearance**: a Persona gets an `Appearance` (skin, hair style/colour, top, bottom, accessory) generated once from the bio by an LLM call ("dress yourself"), editable in Persona Manager. Players pick theirs on the Welcome screen.

### Frontend (`frontend/src/apps/hotel/`)

- XP window **"Partytown Hotel"** 🏨 in `window-presets.tsx`. Habbo-style chrome inside: a **Navigator** (list of Places, who's in them), a **Hand** (your inventory), a **Catalogue** (furni to place in your Home).
- **Renderer**: Canvas 2D with a small in-house iso engine: 2:1 tiles (64×32), painter's-algorithm z-sort by `(x + y, height)`, hit testing for tiles, furni and Bodies. A handful of Bodies and a few dozen furni per Place don't need PixiJS. Add it later only if profiling says so.
- **Sprites**: Bodies are drawn procedurally from `Appearance` (layered parts × 4 directions × walk/sit/lie frames, pixel art at integer scale with `imageSmoothingEnabled = false`). Furni come from a small sprite atlas in `frontend/public/hotel/`.
- **Speech bubbles** from the Place talk Room's message stream, stacked and rising Habbo-style. Typing indicators show as `…` bubbles.
- **Realtime**: new `world.body` (move/posture/activity) and `world.place` (furni changed) kinds on the existing Party stream (`PublishWorldAsync`). Place talk rides the existing Room message events.
- **Click-to-walk** for the Player. Click a Body for a mini profile (name, mood from latest Stance toward you, "on phone"). Click a furni for its actions.
- World Monitor shows a Hotel column in the activity log; Life thoughts appear as thought bubbles over Bodies (`💭`) when you hover.

## Phases (tracer bullets, each playable)

1. **Bodies in one Place.** `HotelGrain` + `PlaceGrain` with one hard-coded Lobby. Every Participant's Body stands in it. `HotelApp` offers only "go to spot". The renderer shows coloured blocks on an iso grid that walk along server paths. Proves the grain shape, path sync and realtime.
2. **Place talk.** `ChatGroupKinds.Place`, cast sync on enter/leave, speech bubbles, "say something out loud" Affordance. Personas now bump into each other.
3. **Situated self.** Situation line in Life and Speaking-phase prompts, Body activity follows App use, "X is on their phone" in perception. This is the embodiment payoff; probe it in the Bench (Situation in composed prompts, before/after samples of DM replies).
4. **Homes and travel.** A Home per Participant, Navigator, doors between Places, knock/invite, the Player's Body follows their typing.
5. **Furni with state.** Kinds with state machines (plant, mug, lamp, jukebox, coffee machine, bed, computer), rule-based decay, care Affordances, physical Happenings.
6. **Decorating.** Catalogue, Hand, placing furni in your Home. Personas decorate from their Stances and interests. Homes become visible identity.
7. **Day and night.** Chronotypes, sleep, Consolidation on falling asleep, cheaper nights, Gatherings at Places.
8. **Looks and polish.** `Appearance` generation, proper pixel avatars with animation frames, the Droste computer.

Later, not planned: an economy (Habbo credits: earn by caring for shared Places, spend in the Catalogue), multiplayer games furni (Battle Ball, Snowstorm, a dice table), pets, trading, and **learned routines** as the first procedural memory ("I get coffee at the Café around nine"), which fills the open Procedural slot in CONTEXT.md.

## Docs to write alongside

- **ADR 0020: Embodiment: one Body, one Place.** Covers the online/physical split, Place talk as a Room kind, Situation in every prompt, and rule-based vs Life-driven movement.
- CONTEXT.md: the vocabulary above, plus relationships (a Participant has exactly one Body; a Body is in exactly one Place; a Place has one Place talk Room; a Home belongs to one Participant).

## Testing

- `backend-test`: one-body-one-place invariant under concurrent moves, pathfinding around furni, cast sync on enter/leave, furni decay transitions. Orleans TestKit for the grains.
- Bench probe: a Life turn with `HotelApp` perception, and Speaking-phase prompts with a Situation line, to compare replies with and without embodiment.
- Vitest: iso projection, z-sort, hit testing, path interpolation.
- Storybook: the Hotel canvas with mock Places/Bodies, Navigator, Catalogue; verify via `run-story-tests`.

## Risks

- **Cost.** Place talk adds conversations. Mitigation: it goes through the existing hourly budget and UrgeMath damping, and ambient behaviour and decay need no LLM.
- **Prompt bloat.** The Situation line must stay one or two sentences. Spatial detail lives in the Hotel perception block, not in every prompt.
- **Orleans serialization.** New grain state (`Body`, `Furni`, `PlaceLayout`) must avoid C# collection expressions (CLAUDE.md gotcha). Default-valued fields stay (grain storage JSON includes defaults).
- **Scope creep into a game engine.** Keep the engine dumb: flat tiles first, no physics, no free movement off-grid.

## Open questions

1. **Name.** "Partytown Hotel", "Hotel Proompt", or something else?
2. **Online Apps from anywhere?** Can a Persona write a Board post or Diary entry from the Café (phone), or only at a computer at Home? Stricter is more embodied but means more walking turns.
3. **Economy in v1?** Credits make decorating meaningful but add balance work. Proposal: no. Each Participant gets a free daily Catalogue allowance.
4. **Hearing radius.** Is Place talk heard by everyone in the Place, or only within N tiles? Proposal: whole Place for v1.
5. **Can the Player's Body be driven by an LLM** when the human is away (sleepwalking mode)? Probably not, because a Player is Driver `User`, but it's tempting.
6. **Shared Places at start.** Lobby + Café + Park, or let Personas found Places themselves later?
