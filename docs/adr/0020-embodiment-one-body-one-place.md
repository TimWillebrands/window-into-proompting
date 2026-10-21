# Embodiment: one Body, one Place

> Status: accepted. Builds on ADR 0018 (World, Apps, Life), 0019 (System One), 0021 (lived
> self). Spec and progress: `.scratch/places/spec.md`. Vocabulary in `CONTEXT.md` (§ Places).

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
  about, not housework. Occasional rule-based mishaps are announced as Happenings.
- **Movement.** Life turns are the only LLM-driven movement. Restlessness (45 min in one Place) adds
  pull to going out. Walking takes time (6 s within a Place, 60 s between Places).

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
