# Places become yours

> Status: proposed. Built and tested, not deployed. Builds on ADR 0057 (Arena fit), 0060 (the lived body) and 0021
> (lived self). Vocabulary in `CONTEXT.md`.

A Place moved a Persona's mood when they arrived (Arena fit) and fed their body (Reserves), but nothing of that stayed
with the Place. The Café where they had forty good lunches was, to them, the same as a Place they had never seen. The
agent–arena relation had no history. In Vervaeke's terms this is the participatory layer: a body and its arena shape
each other over time, and what is relevant about a Place is partly what it has been to you. Without it the explore ⇄
exploit tension had nothing to stand on: nothing was known well enough to go back to, so nothing was new either.

## Decision

1. **Belonging is an edge on the Persona's side.** `SelfState.Places`: per Place a warmth in −1..1 and when it was
   last felt. The Place holds nothing. A **Haunt** is a Place at 0.4 or more; at −0.4 or less they are put off.
2. **It saturates and fades, like a Tie.** A good or bad time `better` (0..1, 0.5 neutral) covers
   `(better − 0.5) × 0.3` of the remaining distance to that side, so the next good lunch at a Haunt adds less than
   the first. A neutral time changes nothing. Warmth fades toward 0 with a 10-day half-life. At most 12 Places are
   kept; the weakest |warmth| goes.
3. **What feeds it is what already moved the mood there.** `FeelAtAsync(place, name, mood, belonging)` replaces the
   Place-sourced `ShiftValenceAsync` calls:
   - Arena fit: mood is the novelty-damped shift as before, belonging the raw fit. A Place that suits you every
     time builds belonging after it stops surprising you. Mood moves by surprise, belonging by accumulation.
   - A Take from a Feature and a meal (a Feature that feeds, a fare): the same relief for both.
   - A conversation in a Place's talk room: the Moment appraisal's "how did this leave their mood", for both. DMs and
     Rooms are unchanged. This one needs no Life turn: a Player chatting in Place talk with the clock paused still
     counts, since it happened there.
4. **Said as a fact, where they are.** Right after "Where you are": "This has become one of your places." at a Haunt,
   "You haven't had good times here lately." where they are put off. Nothing in between, nothing without a day
   (scenario Rooms).
5. **The landscape.** In Places' go-options, read once per perception:
   - A Haunt reads "Go to the Café (one of your places)". It gets one more pull only when they are run down (valence
     −0.2 or lower, battery under 0.5, Flagging or worse, or ailing): a depleted body goes back to what it knows.
   - When they are well (valence 0.2 or more, Fresh or Fine, at most peckish, not ailing), one public Place they have
     no visit day at in the last week and no belonging to, picked at random, reads "(you've never been)" with one
     more pull.
   - A Place they are put off by loses a pull and says nothing.
   - The body's own hooks (something to eat, dead on your feet, not well, peopled out) win: on those labels neither
     Haunt nor never-been is added. Put off still costs its pull.
6. **Staying.** At a Place with warmth above 0, restlessness waits `Restless(fit) × (1 + warmth)`, up to twice as long.
7. **A Persona's own Home is never a Haunt.** Its warmth may be recorded but gives no line, hook, pull or longer stay:
   it is already theirs.
8. **Shown.** `SelfSummary.haunts`: up to three names, strongest first. The World app's Life row shows
   "haunts: the Café, the Tuin".

## Considered

- **A counter of visits.** It exists as Role (Regular: 3 of the last 7 days) and says nothing about how it went. A
  Persona who keeps going to the office canteen and hating it is a Regular, not at home there.
- **A Stance in the memory graph** ("the Café is my place"). The right home for a belief about a Place once
  consolidation forms them, but it forms in rest from recollections, slowly and in words. Warmth is cheaper, numeric,
  read on every perception, and can feed a Stance later.
- **A pull toward Haunts at all times.** Self-reinforcing: going makes it warmer, warmer pulls harder, and the cast
  settles into one café each. The pull is the exploit side only when run down; well, the hook is a reminder and
  the explore pull points elsewhere.
- **A score on the Place.** Meaning is in the fit (ADR 0057). The same Café is a Haunt for one and put-off for another.

## Consequences

- Counter-pulls: the 10-day fade, saturation, the exploit pull only when run down, the explore pull only when well,
  the cap of 12, and Arena fit's novelty damper untouched.
- A daily good lunch alone settles near 0.5 (relief about 0.7 a day against the fade); with Arena fit on arrival and
  good talk it goes higher. A Place visited once a week stays near 0.1.
- One more grain read per Places perception, a visit-days read only when well, and one Places lookup per presence
  (a snapshot only when the line would be said, to rule out Home). No new LLM call.
- On the wire: `SelfSummary.haunts`. In state: `SelfState.Places` (id 45). Existing Personas start with none.
- A neutral Arena fit, Take or talk does nothing, so a Place gains a tie only once something there was good or bad.

## Not yet

- Belonging consolidating into a Stance, and recall weighting Moments from a Haunt.
- Haunts shaping who you meet: a regular at your Haunt known by sight.
- The fixed +0.6 lift for a Take still could fold into Arena fit (ADR 0057).
