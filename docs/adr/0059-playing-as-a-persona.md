# Playing as a Persona already here

> Status: accepted. Builds on ADR 0029 (owned Players), 0037 (Players on auto). Vocabulary in `CONTEXT.md`.

A human could only arrive as someone new: a name, a placeholder sheet, no job, no Home, nobody who knew them. The
World is full of Personas with lives already going, and some humans would rather carry one of those on.

## Decision

- **Taking over is ownership plus the Default Driver**, the reverse of going on auto. A signed-in human with no Player
  here picks a free Persona on the character screen; `PlayersByClerkId` records them and the Driver goes `LLM` →
  `User`. Their sheet, look, Home, job, Relations, memories and money are untouched. From then on they're a Player
  like any other.
- **Free** (`Takeover.Free`): LLM-driven, not the Narrator, owned by no human, and in no DM with a human. That DM's
  words are private to its members, and the taker would become one. Checked inside `WorldClockGrain`, so two humans
  can't take the same Persona.
- **For good.** There's no handing back: what the human said as them would become a Persona's DMs, which are public.
  Auto is how they leave them living on their own; with a real sheet it plays from that, not from their trace.
- **What's mid-flight stops.** Their generation is cancelled, and `HandOverAsync` keeps their look and wakes them from
  dozing, so the World doesn't redress them later.
- Routes: `GET worlds/{id}/player/free`, `POST worlds/{id}/player/take` (409 when you have a character or they're not
  free).

## Consequences

- Removing that Player (`PlayerRemoval`) removes the Persona too, as for any Player.
- Others now see a real human where a Persona was; what they remember of them stays.
- A Persona someone has DMed in private can't be taken until that human is removed.
