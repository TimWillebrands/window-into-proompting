# Embodiment: one Body, one Place

> Status: accepted. Builds on ADR 0018 (World, Apps, Life), 0019 (System One), 0021 (lived
> self). Open: the eavesdrop probe (#124). Vocabulary in `CONTEXT.md` (§ Places).

Until now the World was pure talk. Every Affordance was a message, and every Persona sounded
like they sat at the same blank terminal. Nothing they did persisted outside memory, and nobody
ever bumped into anyone.

## Decision

Every Participant has exactly one **Body**. It is always in exactly one **Place**: a Home, the
Lobby or the Café.

- **Online vs physical.** Rooms, Messenger, Board and Diary are online: usable from anywhere, on
  a device, and the device shows on the Body. **Place talk** is physical: speech between Bodies
  in a Place. It is a Room with `Kind = "place"` whose cast follows the occupants, so the whole
  Response pipeline, the race and recall come for free (the same move as DMs being Rooms).
- **Spots are canonical, tiles are presentation.** Personas reason in named spots ("the window
  table"). Affordances, Situation, co-presence and Earshot key off spots. The iso renderer derives
  tiles on the client.
- **Situation in every prompt.** A one-line, Body-derived "Where you are" goes into the
  `# Right now` block (Decision, Speaking, Life, Diary). The Body wins for *where*; the off-screen
  day is the life around it.
- **Earshot.** Talk reaches your spot, a shout reaches the whole Place, and a whisper reaches one
  addressee next to you. `ChatGroupGrain` stamps `ChatMessage.HeardBy`/`Volume` for Place talk
  only (via `IPlacesGrain.EarshotAsync`, read-only and always-interleave). Fan-out, per-persona
  history (`GetMessagesUntilAsync(listener)`) and lull-capture witnesses all follow `HeardBy`.
  `null` means everyone, so Rooms and DMs are unchanged.
- **Muffled, not silent.** Bodies out of Earshot get an Overheard record: tone, plus "you think you
  heard your name", read by System One and never the words. That's the cocktail-party effect, and
  it creates an eavesdrop pull.
- **Furni with state, decay from timestamps.** Rules are pure (`FurniRules`) and nothing ticks.
  Care is capped at one item per Place, because decay exists to give people something to talk
  about, not housework. Occasional rule-based mishaps are announced as Happenings. Every Home starts
  with the same windowsill plant, so its cadence has to be slow. At a day per watering, all of them
  dried out together after downtime, and seven people thought about their plant within 20 minutes.
  A plant now goes 4 days between waterings, and its pull stays low until it wilts.
- **Movement.** Life turns are the only LLM-driven movement. Restlessness (45 min in one Place) adds
  pull to going out. Walking takes time (3 s within a Place, 15 s between Places; it's a game, so short).
- **Day and night.** Places owns sleep. At bedtime the Body walks home to bed; falling asleep runs
  Consolidation; morning wakes it. Bedtime comes from the lived day's first and last beats (ADR 0021),
  else a stable chronotype. The World clock skips sleepers, persona talk doesn't wake them, and a
  human's DM does (groggy). Waking up gets you out of bed: the Body moves to the kitchen, or else
  another free spot. Otherwise everyone's morning read "on the bed" for hours.
- **Homes are private.** You knock when the owner is in and awake, or get invited. Owners decorate in
  their own words (two things a day), and visitors see it: stances end up on the walls.
- **Gatherings can be at a Place.** When one is due, awake LLM attendees walk there. Humans come
  themselves.
- **Gatherings keep feast time** (`GatheringRules`). A gathering starts on time or not at all: one the
  World slept through by more than 30 minutes is `Missed`, nobody goes and nobody owes anything. It's
  `Over` 3 hours after it starts.
  - Planning needs an evening left: at least 90 minutes before the host's bedtime. The plan prompt
    sees the clock, and the start lands at least an hour before bed.
  - You don't say yes to something that starts after your bedtime.
  - You don't plan a second get-together while you're going to one within 2 hours, or at a Place
    that already has one coming up.

  Before this, a bench tea was planned for 00:12 with fifteen yes's, while its host and the person
  named in its title were asleep. A duplicate set up "to say I'm coming" started 9 hours late, after
  downtime.
- **Shouts carry next door** (public Places are neighbours), as tone only.

## Why

- **Measured, not assumed.** Bench A/B (`Places_SituationAB`): on the production voice model the
  Situation line roughly tripled to ninefold situational detail per reply. It grounded replies
  without reciting them, and co-presence was used ("Ask Hana, she's right there"). The cheap model
  ignored it.
- **Place talk as a Room kind** keeps one conversation engine. Earshot is a filter on that engine,
  not a second one.
- **One grain owns "where is everyone"** (`PlacesGrain`, per Party), so one-body-one-place has a
  single writer.

## Consequences

- Room core has an opt-in Earshot path. Changes to fan-out or history reads must keep
  `HeardBy == null` meaning everyone (regression test: `PlacesTest.RoomsAndDms_StayHeardByEveryone`).
- A Place's conversation thermostat and ties seeding still read the whole Place, not one spot
  cluster. That's acceptable for 2–6 Bodies.
- `PlaceGrain` (one grain per Place) from the original spec is deferred: Places, furni and Bodies
  all live in `PlacesGrain` until a Place gets heavy.
- Sleep, decorating and Appearance (spec phases 7–8) build on this without changing it.
