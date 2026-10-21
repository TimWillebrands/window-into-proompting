# Places: every Participant gets a Body

Status: phases 1–8 implemented and live-tuned, plus the 3D renderer and city centre (see § Progress); only the eavesdrop probe is open

**Places** is a Habbo-Hotel-inspired isometric App for the World. It is not a minigame next to the other Apps. It is the **physical layer under all of them**: every Persona and every Player has exactly one **Body**, standing in exactly one **Place**, at all times. When Denise DMs you, her Body is somewhere, for example on the sofa at home with her phone out. The XP desktop is what the Bodies do on their devices. Places is where the Bodies are.

Two goals:

1. **Embodiment that makes Personas better.** A Body gives them a situation (where am I, who is near, what is in reach), physical limits (one place at a time, walking takes time, sound only carries so far, sleep), and a world they can change (fix, tidy, decorate, build). That grounds their talk, varies their days, and gives Initiative real things to do besides "say something somewhere".
2. **Fun.** Pixel people walking around, speech bubbles, furni, decorating your room, seeing that Pieter is glued to his phone at the café while Hana waters the lobby plant, and sneaking closer to hear what two people are whispering about.

## Decisions so far

- **Name**: the App is just "Places". In code: `PlacesApp`, `backend/World/Places/`, `frontend/src/apps/places/`.
- **Online Apps from anywhere**: everything works on the phone. Some Personas still *prefer* the laptop for big-screen things. That is a soft pull per Persona (**Device habits**), not a rule.
- **Economy**: out of scope for now. Decorating uses a free Catalogue with a daily allowance per Participant, so Homes don't fill up with clutter.
- **Sound propagation is limited**, eventually. You only hear speech within **Earshot**. That changes how Place talk is delivered (§ Sound). **But Earshot is deferred to phase 6.** Until then everyone in a Place hears everything in it, and whisper is a targeted aside to one Body.
- **Spots are the source of truth, tiles are presentation.** Personas reason in named spots ("the window table", "next to Hana"), never coordinates. Affordances, Situation, co-presence and (later) Earshot all key off spots. Tile positions, paths and z-order are derived for rendering only, and pathfinding is a frontend/presentation concern until something in the domain needs it.
- **Prove embodiment before building the engine.** Text-only Bodies plus the Situation line ship first and get a Bench A/B against today's world. The iso renderer only lands after that shows a gain. Today's failure mode is the evidence to beat: in a 2.5 h live run every persona converged on the same metaphors ("lantern", "cathedral of indirection", "load-bearing wall").
- **Care is capped.** Decay exists to generate things to talk about, not housework. At most one pending care item is foregrounded per Place, and care Affordances are subject to Life's normal per-App habituation.

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
| one blank terminal | Body posture follows App use | DM'ing = "on your phone"; long Board post = "at your laptop" for some; visible to others |
| no consequence | furni has state that persists and decays | water / fix / tidy / build / decorate Affordances; condition visible to all |
| nothing is theirs | a **Home** per Participant, decorated by them | Stances made visible: a Lisp poster on the wall because of "Lisp is elegant" |
| no bumping into | shared Places with **Place talk** and **Earshot** | proximity decides who hears, so walking over to someone *is* joining a conversation |
| flat days | day/night rhythm, sleep | Sleep is rest, so Consolidation runs then (CONTEXT: "analogue of memory consolidation in sleep") |

In Vervaeke terms: today the agent–arena coupling is purely linguistic. Places adds a sensorimotor arena. Affordances become literal (Gibson's sense: the chair affords sitting *to this Body, here*), and the Relevance Realization instances get a richer landscape to weigh without any change to Life.

## The quasi-meta part

- **Online vs physical.** D'Accord Rooms, Messenger, Board, Diary are *online*: you can use them from anywhere your Body is, on a device. **Place talk** is *physical*: you hear only Bodies within Earshot. If Denise is sitting next to you, DM'ing her is odd. The Situation line says she's right there, so Personas talk out loud instead.
- **Devices.** Every Body has a phone, and every Home has a laptop. Using an online App shows on the Body (phone in hand, typing at the desk), and others nearby perceive it ("Pieter has been on his phone the whole time").
- **Device habits.** Derived once from the bio, together with the chronotype. Example: "does big-screen stuff at the laptop: long posts, diary, anything important". For such a Persona, long-form Affordances (new Board topic, Diary entry, later Mail or big transactions) get a pull bonus when they're at a laptop. On the phone, the Situation says "thumb-typing this on your phone", so the result comes out shorter and sloppier. A Persona may also choose "go home and do this on the laptop", which is a nice reason to walk somewhere.
- **The human too.** The Player has a Body. If they're typing in Messenger, their avatar is on its phone, or at the laptop if they're at Home. If the browser tab is idle, the avatar dozes (Habbo `zzz`). Click a tile to walk. Like any Body, the human only reads Place talk within their Earshot.
- **Droste.** The laptop in your Home, when clicked, shows the XP desktop, which shows the Places window, which shows your avatar at the laptop. A recursive easter egg; the browser is called DROSTJE after all.

## Sound

Physicality is the point, so sound does not fill the whole Place.

- Every utterance has a **volume**: `whisper` (adjacent tiles), `talk` (default, a few tiles), `shout` (the whole Place, and audible through an open door into the next Place). The Speaking phase may choose a volume; Life Affordances can pick one explicitly ("whisper to Hana", "shout across the café").
- **Earshot** is computed at send time from the tile distance between the speaker and each Body in the Place, reduced by blocking furni (walls, a closed door). It's a snapshot stored on the message.
- **Out of Earshot but in the same Place** you notice talk without the words. Perception gets "Pieter and Hana are arguing quietly by the window", and the screen shows a faded scribble bubble instead of text. That creates the **eavesdrop** Affordance, "drift closer to Pieter and Hana", which is a strong pull when your name might be in it.
- **Muffled detail comes from System One** (ADR 0019). For each out-of-Earshot occupant, the overheard utterance goes through the local decision model with a `choice` for tone (laughing / arguing / serious / flirting / neutral) and a `noul` for "mentions <listener>". The listener gets tone, plus "you think you heard your name" when that noul is high: the cocktail-party effect, salience cutting through noise. Never the topic or the content. This costs only a local forward pass.
- **Memory follows the ears.** Capture's "present" set for a Place talk span is the hearers, not the occupants. You can't remember what you didn't hear. You can remember *that* they were whispering, though; that's an Event of its own.
- **Humans get no god-ears by default.** The World Monitor gets an "all ears" author toggle, like reading persona↔persona DMs read-only today.

## Vocabulary (to add to CONTEXT.md § World)

**Body**: A Participant's physical presence in the World. Exactly one per Participant, always in exactly one **Place**. Has a spot, a posture (standing, sitting, lying, walking, asleep), what it's doing (on phone, at laptop, using a furni), and what it carries. _Avoid_: avatar (the sprite, not the thing), character, sprite, NPC.

**Place**: A physical location in the World with a tile grid and furni: a Home, the Lobby, the Café, the Park. Places are Arena entities, so Events happen *at* them and Stances can target them ("the café is my spot"). _Avoid_: room (taken, see **Room**), location, map, level.

**Home**: The Place that belongs to one Participant. Private by default; others enter by invitation or by knocking. Where its owner sleeps and keeps their laptop.

**Furni**: A placed object in a Place with a kind, a name, a state (condition, on/off, contents) and the Affordances it offers. Borrowed from Habbo. _Avoid_: item, object, prop, entity.

**Situation**: The short, Body-derived description of where a Participant is and what's around them, injected into every prompt that speaks as them. _Avoid_: context, location string, environment.

**Place talk**: Speech between Bodies in a Place, shown as speech bubbles. Backed by a Room with `Kind = "place"`, delivered per utterance to its **Earshot** only.

**Earshot**: The Bodies that hear one utterance, fixed at the moment it's spoken, from distance, volume and what's in between. _Avoid_: range, radius, audience.

**Device habits**: A Persona's leaning toward phone or laptop for different kinds of online activity. A pull, never a rule.

## Shape

### Backend (`backend/World/Places/`)

- **`PlacesGrain`** (key = Party id): the registry of Places and the **body index**. It's the single writer of "which Place is Body X in", so the one-body-one-place invariant has one owner. Plain persisted state, not event-sourced: positions are state, not conversational history (ADR 0002's test). It creates a Home for every Participant on demand, the same way `EnsurePlayerAsync` creates the Player.
- **`PlaceGrain`** (key = place id): named spots (with capacity and which furni they're next to), furni list with state, doors to neighbouring Places, and the id of its Place talk Room. Owns furni state transitions and, from phase 6, Earshot. A tile layout per spot (for the renderer) arrives in phase 5; it's presentation data, not domain.
- **Body state** lives in `PlacesGrain`: place, **spot** (canonical), posture, activity, destination spot + `ArriveAt`. Moving between spots/Places takes time (the server stores departure + arrival, never per-step writes). The renderer derives tiles and paths from spots and animates from `(from, to, startedAt, arriveAt)`. Phase 1 needs no tiles at all.
- **`PlacesApp : IWorldApp`**:
  - `PerceiveAsync` → the Situation block plus Affordances. Affordances: go to a Place, sit/use a furni, approach someone, eavesdrop, care for something (water, fix, tidy), decorate your Home, knock on/invite to a Home, say/whisper/shout something.
  - `ActAsync` → move, use, or speak. Speaking reuses `IPersonaGrain.InitiateAsync` on the Place talk Room, so the Response pipeline, the race and recall come for free (same move as DMs = Rooms).
  - Life needs no changes, as ADR 0018 promised.
- **Place talk Room** (`ChatGroupKinds.Place`): the cast is the occupants (synced by `PlacesGrain` on enter/leave). **Phases 2–5: everyone in the Place hears**, which is the existing Room behaviour with zero pipeline changes; whisper is a separate aside to one Body. **From phase 6** delivery becomes per utterance:
  - `ChatMessage` gets an optional `HeardBy` (participant ids) and `Volume`. `null` means everyone, so Rooms and DMs are unchanged.
  - `ChatGroupGrain.SendNewMessageAsync` asks the Place for the Earshot when the Room kind is `place`. `NotifyAllParticipantsAsync` fans out only to hearers. Occupants out of Earshot get a muffled notice into their perception, not into the pipeline.
  - Per-Persona history reads (`GetRecentMessagesAsync` as seen by X, the recall anchor, Repair hints) filter to messages X heard.
  - Lull Capture uses `HeardBy` as the present set. Events get `source = "places"` and a `place_id` property, so recall can say *where* something happened.
  - The realtime `message` event still goes to every client. The frontend redacts bubbles the viewing Player didn't hear (unless all-ears is on). That's a game rule, not a security boundary, which is fine for this app.
- **Co-presence rule in Messenger**: when the other person is in the same Place, `MessengerApp` demotes "DM X" and `PlacesApp` offers "say to X". This is an explicit perception rule, not left to the model.
- **Place as a recall anchor**: Events captured at a Place carry `place_id`, and recall's relevant arm uses the current Place as an anchor next to the cast and Concepts (ADR 0015). Being back at the Café surfaces what happened at the Café.
- **Situated self**: add `Situation` to `LifeContext` and to the Speaking-phase prompt (next to `SelfView`). This is the one cross-cutting prompt change and the main payoff: every DM, Board post and diary entry is written from somewhere and on some device. One line, never coordinates: "You're at the Café, at the window table, on your phone. Hana is at the counter. It's raining."
- **Body follows App use**: `WorldClockGrain.RecordAsync` notifies `PlacesGrain`. Body activity is set from the activity: phone by default, laptop when at Home and the Persona's Device habits prefer it for that activity. Human typing activity comes from the realtime hub.
- **Furni state and decay**: rule-based, ticked by the World pulse, no LLM. Plants dry out, mugs pile up, the jukebox needs a coin. Condition shows in perception and on screen, and care Affordances get priority as things decline.
- **Happenings get physical**: the ambient Narrator can target a Place ("the café's espresso machine broke"). Its JSON shape gets an optional `place` + `furni effect`. That moves novelty from announcements to surroundings.
- **Ambient behaviour without LLM**: between Life turns, Bodies do rule-based idles: sit down when arriving near a seat, turn to face whoever speaks, wander a tile, yawn. Cheap liveliness. Life turns stay the only LLM-driven movement.
- **Sleep**: each Persona gets a chronotype (derived from the bio together with Device habits). At night their Body walks Home to bed. Sleeping Bodies are skipped by the World clock (lower night-time cost). Falling asleep triggers Consolidation. Once sleep exists, the Diary stops being the only "rest" that consolidates and goes back to being reflection. A human DM can wake them; the reply is groggy.
- **Gatherings in Places**: a Calendar Gathering can point at a Place instead of a Room ("drinks at the Café at 20:00"). Coming due walks the attendees there.
- **Appearance**: a Persona gets an `Appearance` (skin, hair style/colour, top, bottom, accessory) generated once from the bio by an LLM call ("dress yourself"), editable in Persona Manager. Players pick theirs on the Welcome screen.

### Frontend (`frontend/src/apps/places/`)

- XP window **"Places"** in `window-presets.tsx`. Habbo-style chrome inside: a **Navigator** (list of Places, who's in them), a **Hand** (your inventory), a **Catalogue** (furni to place in your Home).
- **Renderer**: Canvas 2D with a small in-house iso engine: 2:1 tiles (64×32), painter's-algorithm z-sort by `(x + y, height)`, hit testing for tiles, furni and Bodies. A handful of Bodies and a few dozen furni per Place don't need PixiJS. Add it later only if profiling says so.
- **Sprites**: Bodies are drawn procedurally from `Appearance` (layered parts × 4 directions × walk/sit/lie frames, pixel art at integer scale with `imageSmoothingEnabled = false`). Furni come from a small sprite atlas in `frontend/public/places/`.
- **Speech bubbles** from the Place talk Room's message stream, stacked and rising Habbo-style. Heard: full text. Not heard: a faded scribble bubble. Whisper and shout get their own bubble styles. Typing indicators show as `…` bubbles. Optional debug overlay to show the Earshot ring around a speaker.
- **Realtime**: new `world.body` (move/posture/activity) and `world.place` (furni changed) kinds on the existing Party stream (`PublishWorldAsync`). Place talk rides the existing Room message events, with `heardBy` on the message.
- **Click-to-walk** for the Player. Click a Body for a mini profile (name, mood from latest Stance toward you, "on phone"). Click a furni for its actions. The chat input has a volume toggle (whisper / talk / shout).
- World Monitor shows a Places column in the activity log, plus the all-ears toggle. Life thoughts appear as thought bubbles over Bodies (`💭`) when you hover.

## Phases (tracer bullets, each playable)

Reordered after review: the cheap, high-payoff part (situated personas) first, measured, and only then the expensive parts (renderer, Earshot).

1. **Text-only Bodies and Places.** `PlacesGrain` + `PlaceGrain`, a Lobby and a Café with named spots, and a Home per Participant created on demand. Bodies have place, spot, posture and activity; moving takes time. `PlacesApp` perceives and offers "go to Place/spot" and "sit with X". Place talk Room per Place where everyone present hears. No renderer yet: a plain list view in the Places window (who is where, doing what) is enough.
2. **Situated self + co-presence.** Situation line in Life and Speaking-phase prompts. Body activity follows App use (phone by default). "X is on their phone" in perception. Messenger demotes DMs to co-present people. Place as a recall anchor.
3. **Bench A/B.** Probe Speaking-phase and Life prompts with and without Situation; run the live world for an hour each way and compare topic diversity and concreteness of replies (the metaphor monoculture above is the baseline). Go/no-go for the renderer.
4. **Furni with state (capped care).** Kinds with state machines (plant, mug, lamp, jukebox, coffee machine, bed, laptop), rule-based decay, care Affordances capped per Place, physical Happenings, Device habits at the Home laptop.
5. **Iso renderer.** Canvas 2D iso engine, tile layouts derived from spots, Bodies as coloured blocks first, speech bubbles, click-to-walk for the Player, Navigator.
6. **Earshot and eavesdropping.** `HeardBy`/`Volume` on messages, filtered fan-out and history, muffled notices via System One (§ Sound), say/whisper/shout, eavesdrop Affordance, scribble bubbles, all-ears toggle. Regression tests on Rooms/DMs first.
7. **Day and night.** Chronotypes, sleep, Consolidation on falling asleep, cheaper nights, Gatherings at Places, knock/invite to Homes.
8. **Decorating, looks, polish.** Catalogue with a daily allowance, Hand, personas decorating from Stances, `Appearance` generation, pixel avatars, the Droste laptop.

Later, not planned: an economy (Habbo credits: earn by caring for shared Places, spend in the Catalogue), multiplayer games furni (Battle Ball, Snowstorm, a dice table), pets, trading, and **learned routines** as the first procedural memory ("I get coffee at the Café around nine"), which fills the open Procedural slot in CONTEXT.md.

## Docs to write alongside

- **ADR 0020: Embodiment: one Body, one Place.** Covers the online/physical split, Place talk as a Room kind, per-utterance Earshot delivery (`HeardBy`), Situation in every prompt, and rule-based vs Life-driven movement.
- CONTEXT.md: the vocabulary above, plus relationships (a Participant has exactly one Body; a Body is in exactly one Place; a Place has one Place talk Room; a Home belongs to one Participant; a Place talk message has one Earshot).

## Testing

- `backend-test`: one-body-one-place invariant under concurrent moves, pathfinding around furni, Earshot by distance/volume/walls, filtered fan-out (`null` HeardBy keeps Rooms/DMs unchanged), Capture present set = hearers, cast sync on enter/leave, furni decay transitions. Orleans TestKit for the grains.
- Bench probe: a Life turn with `PlacesApp` perception, and Speaking-phase prompts with a Situation line, to compare replies with and without embodiment. A second probe for eavesdropping: does a Persona drift closer when muffled talk names them?
- Vitest: iso projection, z-sort, hit testing, path interpolation, bubble redaction for the viewing Player.
- Storybook: the Places canvas with mock Places/Bodies, Navigator, Catalogue, heard/unheard bubbles; verify via `run-story-tests`.

## Risks

- **Core pipeline change.** `HeardBy` touches fan-out, history reads, Repair hints and Capture, the most tuned code in the repo. Mitigation: `null` = everyone, so it's opt-in per Room kind, with regression tests on Rooms/DMs before Place talk lands.
- **Cost.** Place talk adds conversations. Mitigation: it goes through the existing hourly budget and UrgeMath damping, Earshot shrinks how many Personas react to each line, and ambient behaviour and decay need no LLM.
- **Prompt bloat.** The Situation line must stay one or two sentences. Spatial detail lives in the Places perception block, not in every prompt.
- **Orleans serialization.** New grain state (`Body`, `Furni`, `PlaceLayout`, `HeardBy`) must avoid C# collection expressions (CLAUDE.md gotcha). Default-valued fields stay (grain storage JSON includes defaults).
- **Scope creep into a game engine.** Keep the engine dumb: flat tiles first, no physics, no free movement off-grid, Earshot as tile distance plus a blocking mask (no acoustics).

## Open questions (answered in review)

1. **LLM-driven Player Body when the human is away?** No. A Player is Driver `User`; an LLM stand-in would blur what Personas remember about the real human. An idle tab shows a dozing avatar (`zzz`) instead.
2. **Shared Places at start?** Lobby + Café, plus Homes. "Found a new Place" comes later as an Initiative Affordance (it's a good consequence-making action).
3. **Earshot tuning?** Whisper 1 tile, talk 4, shout the whole Place (and through open doors). Snapshot at send time; a speaker walking mid-sentence doesn't matter.
4. **Muffled detail?** Tone and "heard your name", via System One; never the topic. See § Sound.

## Progress

- **Phase 1 done** (b828d44, ffeb431, d92b8ae, 88f8c8e). `PlacesGrain` holds Places *and* the body index. `PlaceGrain` is deferred until furni exist (phase 4); one grain is enough while Places are just named spots. Lobby + Café seeded; a Home per Participant on first sight (Narrator excluded); others' Homes are not enterable yet. Walking: 6 s within a Place, 60 s between Places, settled by a keep-alive grain timer plus lazily on every read. Place talk Room (`Kind = "place"`) is created on first entry, its cast synced on enter/leave, and hidden from D'Accord. Restlessness: after 45 min in one Place, "go out" Affordances gain +1 pull (otherwise nobody ever leaves an empty-world start). Text-only Places window: Navigator, spots, walking countdown, Place talk.
- **Phase 2 partly done** (51352a0). The Situation line is in eb's `# Right now` block (`PersonaSelfGrain.GetPresenceAsync`), so Decision, Speaking, Life and Diary prompts all get it. The day beat is relabelled "Off-screen right now": the Body wins for *where*, the day is the life around it. Body activity follows App use for 4 min (phone; laptop for the Board at home; diary), shown in others' Situation. Messenger demotes DMs to people in the same Place.
- **Place as recall anchor done** (83fa26a, 7e9d4f0). When Place talk goes quiet, the captured Event carries `place_id`. `RecallAsync(…, placeId)` adds a relevant leg for Events at that Place. Life and Board recall pass the Body's Place; the Room pipeline passes the Place of a Place talk Room.
- **Phase 2 done.** Body activity now comes from every message (933e3aa). `PlacesGrain` follows the Party stream, so pipeline replies and humans typing show too, not only Life turns.
- **Phase 3, Bench A/B** (probe `Places_SituationAB`, 0702333). Setup: same day/mood/openers; Speaking phase with no Situation vs "at the Café, Hana and Pieter here" vs "at home on the sofa for 3 h". 2 personas × 3 openers × 3 samples per arm.
  - `deepseek`/`mistral-small` tier (bench default): no real effect. Situational words 1 → 2 → 7 of ~165, and replies stay generic. The cheap model ignores the line.
  - **Production voice model (`claude-haiku-5.5`): clear effect.** Situational/sensory words per 18 replies: none 2 → Café 7 → home 18. Qualitatively:
    - It grounds without reciting. Vlad at the Café: "whether the café's new espresso machine is a downgrade. It hisses like it's judging me", "saw someone at the counter doing the crossword in ink". At home: "the neighbour's cat … keeps sitting on my windowsill".
    - Co-presence is used. Denise at the Café, asked about the scheduler: "Ask Hana, she's the one who has to live with it" / "Ask Pieter, he always has an opinion".
    - Home + 3 h shows up as mood: "been on the sofa since forever".
  - Not measured: stale metaphors (0 in every arm; eb's style rules already stop the monoculture in single replies, so Places can't claim that), multi-turn drift, and Initiative choices.
  - **Verdict: go.** The Situation measurably grounds replies on the model that voices them. Next is phase 4 (furni), then phase 5 (renderer). A Life-turn probe (does a Body change *what they choose to do*) is still worth adding.
- **Verified live**: the API walk → arrival → Café talk Room creation works across a `dotnet watch` reload. Pieter's Life thought mentioned "I've been indoors since…". Browser check of the window blocked: Servo + the dev console-forwarding loop starved the page; data rendered, the Player id never resolved.

- **Phase 4 done** (commit after 552cef1, ba6091d).
  - Furni lives on each Place (still in `PlacesGrain`). Decay is computed from timestamps by pure `FurniRules`, so nothing ticks: plants dry over 24 h, coffee dirties the cups, and a public machine or lamp breaks every 3–8 h. Breakdowns are announced as Narrator Happenings.
  - Only the single neediest care item per Place is offered. Using things (coffee, jukebox) is a 0-pull option. Acting puts the Body next to the furni and shows "watering the plant" etc. for 3 min. The Situation gets "Around you: …" with at most 2 clauses.
  - The UI shows furni in spot cards with Water/Wash/Fix/Use buttons. Rules stay server-side (`FurniView` in the snapshot).
  - **Not done:** Device habits (laptop pull bonus for long-form at home). Happenings are rule-based mishaps rather than narrator-chosen effects.
- **Live:** with the world running, the whole cast walked to the Café on their own (restlessness pull).
- **Phase 5 done** (e4b4e81). Canvas 2D iso view on top of the spot cards in the Places window.
  - Tiles are derived on the client from the spot list (`iso/layout.ts`, auto layout; no hand-authored maps yet).
  - Bodies walk along A* paths at about 4 tiles/s. Between-Place walks show at the door (first and last 30% of the trip).
  - Place talk messages show as speech bubbles for 14 s. Device and activity icons show over heads. Furni are drawn as emoji with a ❗ when they need care.
  - Clicking near a spot walks the Player there; this also works for another Place you may enter.
  - Verified visually in a standalone bundle with mock data; not yet in the live app because of the Servo WebSocket console loop.
  - **Not done:** Storybook stories (the Storybook MCP isn't available in this session), sprite art/Appearance (phase 8), click-a-Body profile.

- **Phase 6 done** (a691abd, plus the commits after it).
  - **Earshot keys off spots:** talk reaches your spot, a shout the whole Place, a whisper one addressee at your spot. `ChatGroupGrain` stamps `HeardBy`/`Volume` for Place talk only. Fan-out, the pipeline's history reads (all three, via `listener`) and lull-capture witnesses (`ClockPolicy.Witnesses`) follow it. Regression test: Rooms/DMs keep `HeardBy == null`.
  - **Overheard:** out-of-Earshot occupants get tone plus "heard your name" via System One, with a name match as fallback, never the words.
  - **Life:** perception shows only heard talk, plus overheard lines. Affordances: say (your spot), whisper to one, call out across the room, and "drift over to hear" (+2 pull when your name came up).
  - **Human:** Talk/Whisper/Shout picker. Unheard lines show as scribble bubbles (`~~~ ~~`) and greyed in the log. "All ears" is an author switch in the Places window (not the World Monitor).
  - Docs: ADR 0020 and the CONTEXT.md § Places vocabulary are written.
  - **Not done:** shouts through open doors into the next Place, and the eavesdrop Bench probe.

- **Phase 7 mostly done** (2153ac6 and the commits after it).
  - **Sleep is owned by Places.** At bedtime the Body walks home to bed and lies down; falling asleep runs `WorldMemory.RestAsync` (Consolidation). Morning wakes them ("just got up").
  - **Bedtimes:** from eb's `GetDayWindowAsync` (first beat = waking, bed = max(chronotype − 60, last beat + 30)), else a stable chronotype from the id (wake 06:30–09:30, bed 22:30–01:30). Fetched detached, because Self calls back into Places.
  - The World clock skips sleepers. A human's DM wakes them groggy for 30 min. eb's side: Availability ≈ 0.02 while asleep, Messenger shows Away, and persona-triggered beats decline without an LLM call.
  - **Homes:** knock when the owner is in and awake. The owner gets "X is knocking" (+3 pull to let them in) and can invite someone over; invitees see "they invited you" (+2 pull). Guests and knocks clear when the owner leaves.
  - **UI:** sleepers lie in bed with drifting z's; others' Homes show 🔒 with a Knock button; a "Let in" banner appears at your own door.
  - **Not done:** Gatherings at Places (Calendar is still Room-only), human idle-tab dozing, and cheaper nights beyond skipping sleepers.

- **Phase 8 mostly done** (the Appearance/decor commits, bec8a91, ba46681).
  - **Appearance:** every Body gets a stable default look from its id. Each LLM Persona then "dresses themselves" once from their bio: one General-tier call, limited to a fixed palette (skin, hair colour and style, top, bottom, accessory). The canvas draws hair styles, glasses, hats, beanies, headphones and scarves.
  - **Decorating:** "put something new up" at home. The Life intent is what goes up ("a vintage Bowie poster over the sofa"), with the style read from the words. At most 2 a day and 8 per Home, the oldest going first. Visitors see "On display: …" in their Situation, so stances end up on the walls without an extra LLM call. Humans get a "Put up" box.
  - **Droste:** "Open" on your Home laptop shows the desktop in a scaled iframe. Nested desktops open their laptop automatically, up to 3 deep, then "It's laptops all the way down."
  - Place talk uses fa's `useRoomConversation` and shows "Denise is about to say something…".
  - **Not done:** a Catalogue/Hand UI (decor is free-text instead, which is closer to the spirit), Appearance editing in Persona Manager, Players picking their look on the Welcome screen, and a click-a-Body profile.

- **Follow-ups after phase 8** (d1c304a and the commits after it):
  - **Live check:** the running world uses Places on its own. Personas walk over to each other and talk out loud at the Café, grounded in their day ("Walking the block for the ankle…"). All residents had dressed from their bio, but 3 of 4 picked `bun` hair.
  - **Dressing:** now one at a time, each seeing the others' looks ("be recognisably different"). Author `POST places/redress` re-dresses.
  - **Humans doze:** after 5 min idle or a hidden tab (`usePlayerIdle`, mounted in the World bridge).
  - **Click a Body** for a profile card: look, what they're doing, mood from the Self API.
  - **Gatherings at Places:**
    - Planned from Life ("Plan to meet people at the Café later") or the Calendar's 📍 option.
    - When one is due, awake LLM attendees walk there and get a Life turn (`ClockPolicy.WalkToGathering`). Humans come on their own.
    - `PlacesApp` gives +5 pull to head there and +2 to talk once there.
  - **Bug fixed:** a detached task (dressing) and a request wrote grain state at the same time (e-tag mismatch). `PlacesGrain` now serialises its writes.
- **Live tuning** (d554b18): Places had become the busiest App (102 of about 210 non-rest actions), with walks hopping person to person and Café talk circling "the tin / the lid". Changes:
  - No new walk options right after a walk, and at most one "go over to".
  - Talking out loud has base pull 1 (+3 when you're named).
  - When eb's thermostat reads the Place talk as Stale (circling or fizzling), leaving gets +2 and talking −1.
- **Shouts carry next door** (72c6c6d): public Places are neighbours, and a shout reaches the next one as tone only ("Through the door from the Café, Denise is calling out…"), with a pull to go and see.
- **Look editor:** `PUT places/appearance` plus palette options. "Change look" in the profile card works for your own look, or anyone's in All ears mode. It lives in Places rather than on the Welcome screen.
- **Re-measured after the tuning** (41 activities since 13:14): Places is 12 of 33 non-rest actions (36%, down from about 49%). Diary, DMs and Rooms are back in the mix. Walks are now mostly between Places, and talk moved from the Café to the Lobby with fresh details ("the ginkgo's stubborn this year"). "The tin" still recurs; eb's stale recall (eb68efe) targets that.
- **3D, prefabs and the city centre** (ADR 0023):
  - The renderer moved to three.js (react-three-fiber).
  - Scenes are ASCII maps plus props by prefab name.
  - There's a new outdoor public Place, "the city centre", with two canals, bridges, quays, canal houses, a herring cart, bikes and a little park.
  - Each Place has `Scene` and `At` ("in the city centre"). Existing worlds get the city centre on activation.
- **Still open:** the eavesdrop Bench probe. It needs World DI (IWorldApp, WorldMemory, IWorldLlm, judge) in the Bench host to run a real Life turn; that's deferred pending a decision.

## Review log

- **Phase 6 done** (a691abd, plus the commits after it).
  - **Earshot keys off spots:** talk reaches your spot, a shout the whole Place, a whisper one addressee at your spot. `ChatGroupGrain` stamps `HeardBy`/`Volume` for Place talk only. Fan-out, the pipeline's history reads (all three, via `listener`) and lull-capture witnesses (`ClockPolicy.Witnesses`) follow it. Regression test: Rooms/DMs keep `HeardBy == null`.
  - **Overheard:** out-of-Earshot occupants get tone plus "heard your name" via System One, with a name match as fallback, never the words.
  - **Life:** perception shows only heard talk, plus overheard lines. Affordances: say (your spot), whisper to one, call out across the room, and "drift over to hear" (+2 pull when your name came up).
  - **Human:** Talk/Whisper/Shout picker. Unheard lines show as scribble bubbles (`~~~ ~~`) and greyed in the log. "All ears" is an author switch in the Places window (not the World Monitor).
  - Docs: ADR 0020 and the CONTEXT.md § Places vocabulary are written.
  - **Not done:** shouts through open doors into the next Place, and the eavesdrop Bench probe.

- **Phase 7 mostly done** (2153ac6 and the commits after it).
  - **Sleep is owned by Places.** At bedtime the Body walks home to bed and lies down; falling asleep runs `WorldMemory.RestAsync` (Consolidation). Morning wakes them ("just got up").
  - **Bedtimes:** from eb's `GetDayWindowAsync` (first beat = waking, bed = max(chronotype − 60, last beat + 30)), else a stable chronotype from the id (wake 06:30–09:30, bed 22:30–01:30). Fetched detached, because Self calls back into Places.
  - The World clock skips sleepers. A human's DM wakes them groggy for 30 min. eb's side: Availability ≈ 0.02 while asleep, Messenger shows Away, and persona-triggered beats decline without an LLM call.
  - **Homes:** knock when the owner is in and awake. The owner gets "X is knocking" (+3 pull to let them in) and can invite someone over; invitees see "they invited you" (+2 pull). Guests and knocks clear when the owner leaves.
  - **UI:** sleepers lie in bed with drifting z's; others' Homes show 🔒 with a Knock button; a "Let in" banner appears at your own door.
  - **Not done:** Gatherings at Places (Calendar is still Room-only), human idle-tab dozing, and cheaper nights beyond skipping sleepers.

- **Phase 8 mostly done** (the Appearance/decor commits, bec8a91, ba46681).
  - **Appearance:** every Body gets a stable default look from its id. Each LLM Persona then "dresses themselves" once from their bio: one General-tier call, limited to a fixed palette (skin, hair colour and style, top, bottom, accessory). The canvas draws hair styles, glasses, hats, beanies, headphones and scarves.
  - **Decorating:** "put something new up" at home. The Life intent is what goes up ("a vintage Bowie poster over the sofa"), with the style read from the words. At most 2 a day and 8 per Home, the oldest going first. Visitors see "On display: …" in their Situation, so stances end up on the walls without an extra LLM call. Humans get a "Put up" box.
  - **Droste:** "Open" on your Home laptop shows the desktop in a scaled iframe. Nested desktops open their laptop automatically, up to 3 deep, then "It's laptops all the way down."
  - Place talk uses fa's `useRoomConversation` and shows "Denise is about to say something…".
  - **Not done:** a Catalogue/Hand UI (decor is free-text instead, which is closer to the spirit), Appearance editing in Persona Manager, Players picking their look on the Welcome screen, and a click-a-Body profile.

- **Follow-ups after phase 8** (d1c304a and the commits after it):
  - **Live check:** the running world uses Places on its own. Personas walk over to each other and talk out loud at the Café, grounded in their day ("Walking the block for the ankle…"). All residents had dressed from their bio, but 3 of 4 picked `bun` hair.
  - **Dressing:** now one at a time, each seeing the others' looks ("be recognisably different"). Author `POST places/redress` re-dresses.
  - **Humans doze:** after 5 min idle or a hidden tab (`usePlayerIdle`, mounted in the World bridge).
  - **Click a Body** for a profile card: look, what they're doing, mood from the Self API.
  - **Gatherings at Places:**
    - Planned from Life ("Plan to meet people at the Café later") or the Calendar's 📍 option.
    - When one is due, awake LLM attendees walk there and get a Life turn (`ClockPolicy.WalkToGathering`). Humans come on their own.
    - `PlacesApp` gives +5 pull to head there and +2 to talk once there.
  - **Bug fixed:** a detached task (dressing) and a request wrote grain state at the same time (e-tag mismatch). `PlacesGrain` now serialises its writes.
- **Live tuning** (d554b18): Places had become the busiest App (102 of about 210 non-rest actions), with walks hopping person to person and Café talk circling "the tin / the lid". Changes:
  - No new walk options right after a walk, and at most one "go over to".
  - Talking out loud has base pull 1 (+3 when you're named).
  - When eb's thermostat reads the Place talk as Stale (circling or fizzling), leaving gets +2 and talking −1.
- **Shouts carry next door** (72c6c6d): public Places are neighbours, and a shout reaches the next one as tone only ("Through the door from the Café, Denise is calling out…"), with a pull to go and see.
- **Look editor:** `PUT places/appearance` plus palette options. "Change look" in the profile card works for your own look, or anyone's in All ears mode. It lives in Places rather than on the Welcome screen.
- **Re-measured after the tuning** (41 activities since 13:14): Places is 12 of 33 non-rest actions (36%, down from about 49%). Diary, DMs and Rooms are back in the mix. Walks are now mostly between Places, and talk moved from the Café to the Lobby with fresh details ("the ginkgo's stubborn this year"). "The tin" still recurs; eb's stale recall (eb68efe) targets that.
- **3D, prefabs and the city centre** (ADR 0023):
  - The renderer moved to three.js (react-three-fiber).
  - Scenes are ASCII maps plus props by prefab name.
  - There's a new outdoor public Place, "the city centre", with two canals, bridges, quays, canal houses, a herring cart, bikes and a little park.
  - Each Place has `Scene` and `At` ("in the city centre"). Existing worlds get the city centre on activation.
- **Still open:** the eavesdrop Bench probe. It needs World DI (IWorldApp, WorldMemory, IWorldLlm, judge) in the Bench host to run a real Life turn; that's deferred pending a decision.

## Review log

- 2026-10-21 (review after the World run): the diagnosis holds and matches live evidence (metaphor monoculture). Changes made in this spec: phases reordered (Situation before renderer, Earshot deferred to phase 6), spots canonical over tiles, care capped, co-presence rule in Messenger, Place as recall anchor, muffled detail via System One, open questions answered. Rationale in short: `HeardBy` touches the most tuned and most bug-prone code (fan-out, history, Repair hints, Capture); places with 2–6 Bodies feel physical without acoustics; embodiment's value should be measured (Bench A/B) before building an engine for it.
