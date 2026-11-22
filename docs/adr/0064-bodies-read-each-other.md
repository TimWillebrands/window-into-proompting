# Bodies read each other: a Bearing, and turning in early

> Status: proposed. Built and tested, not deployed. Builds on ADR 0020 (Bodies, Spots, sleep), 0045 (Aura), 0060 (the
> lived body) and 0061 (Ailing). Vocabulary in `CONTEXT.md`.

ADR 0060 gave a Persona a body that runs down, but only from inside it.

- Someone could be famished or dead on their feet, and the person on the sofa next to them could not tell. The body
  was private data.
- So nobody could care for anyone. A host cooked only when they were hungry themself: a famished guest got fed only
  if the host happened to want lunch.
- Bedtime was the clock's (`Sleep.Window`). Spent at 20:30 with a bedtime of 23:45, a Persona could lie down once and
  then wait three hours to sleep.

For Vervaeke, participatory knowing is knowing by being in relation: bodies read each other before anyone says a
word, and that is what lets someone look after someone else. A working body is absent (Leder), so what shows is only
what has stopped working.

## Decision

1. **A Bearing is what a Body gives away** (`Bearing.Of(Reserves)`), one phrase or nothing:
   - ailing: "looks unwell";
   - else rested under 0.15 (Spent): "looks dead on their feet";
   - else fed under 0.15 (Famished): "looks like they haven't eaten all day".

   Only Distress and Ailing show. Hungry, very hungry and flagging give nothing away.
2. **It is kept on the Body** (`Body.Bearing`, `BearingUntil`) for 2 hours (`Bio.FeltFor`). A Life turn works it out
   right after settling the Reserves and writes it (`IPlacesGrain.SetBearingAsync`) only when it differs from what
   shows or has under an hour left. A Body that shows nothing and has nothing to show costs no call. Past
   `BearingUntil` it counts as absent everywhere, so a paused World shows no stale one. Meeting the need clears it
   at once: eating, or being fed as a guest, clears the famished one; a lie-down or turning in clears the spent one.
   Looking unwell stays until a Life turn finds it gone. If writing it fails, the turn keeps its Reserves.
3. **It reaches only the same Spot**, like an Aura: "Hana (right next to you, sitting on the sofa; up close: …; looks
   dead on their feet)". Nothing across the room, and nothing from someone asleep.
4. **It is something to ask about.** With nobody talking lately, the talk option for someone next to you with a
   Bearing who is awake is "Ask Hana if they're all right, right here (looks dead on their feet)". It replaces only the generic
   "Ask … what they're up to"; picking up what was just said, a witnessed Happening and an opener still come first.
5. **A host can cook for a guest.** At their own Home, not hungry themself, with someone in the Place awake, not
   walking and showing the famished Bearing: "Make something to eat for Hana (they look like they haven't eaten all
   day)", pull 2, no `Meets` (it isn't the host's Distress). It is the home-cooking fare, so it feeds everyone there
   as cooking always did. The cook eats too; the summary reads "makes lunch for Hana at home". The guest's Bearing
   clears, so the offer isn't made again.
6. **Bed follows the body.** At their own Home, awake, spent or ailing, from 20:00 to 05:00: "Turn in early", meets
   tiredness, pull 5 (`Bio.Seated`) when spent and 3 when only ailing. It walks them to bed and sets
   `Body.TurnedInUntil` to tonight's own bedtime plus 15 minutes, at most 6 hours away. Until then it is night for
   them: they fall asleep, and the morning wake leaves them be. If the 6 hours run out before bedtime, the evening
   gap still counts as theirs (`Sleep.Evening`: nearer bedtime than waking). They rise at their usual time and the
   rise clears it. Hours slept count from falling asleep, so 20:30 to 07:00 is ten and a half: a full night (7 hours)
   halves Strain and can end an ailment.
7. **Only lived bodies.** A Bearing comes only from a Life turn and fades; `TurnInAsync` refuses a human's Body, one
   walking, asleep or away from its own Home, and is reachable only from a Life turn.

## Considered

- **Showing every Reserve level to everyone.** "Looks a bit peckish" on every Body would be talked about all day and
  turn meals into the topic. A working body is absent; only the one that has stopped working shows.
- **A status icon in the UI only.** Players would see it and Personas wouldn't: the people who need to read each
  other most would be the ones who can't. It has to be in the Situation.
- **Moving the clock's bedtime itself.** Bedtime comes from the lived Day and is read by Gatherings and the Calendar
  for planning. A tired evening is a choice made that evening, not a new schedule: the Window stays, and a Body opts
  out of the evening.

## Consequences

- One Places call on the Life turns where a Bearing appears, changes, clears or is renewed (about once an hour while it
  lasts). No new model call.
- On the wire: `Body.bearing`, `bearingUntil`, `turnedInUntil`. In code: `Bearing`, `IPlacesGrain.EaseAsync`,
  `Hooks.AllRight`, `Sleep.TurnInUntil`, `Sleep.TurnedIn`, `Sustenance.Summary(…, forThem)`.
- A Bearing can now steer talk: "are you all right?" could become a motif in a house where someone is ill for days.
  Stale words should catch it; watch the first week of `say` labels.
- A host who keeps cooking for a famished guest keeps the guest from ever looking for food elsewhere. That is the
  care this was for; read a week of `eat` activities with `WithPersonaId`.
- Turning in early also skips the evening: the World clock skips sleepers. A Persona who is spent every evening will
  miss evenings. The counter-pull is that Ailing ends and a full night halves Strain.
- The 20:00 to 05:00 window, the 15 minutes and the 6 hours are guesses.

## Not yet

- Reflex noticing a Bearing on arrival ("Up close: looks unwell" when walking up to someone), as an Aura does.
- Bringing soup: carrying food to someone else's Home, or a Place selling to one person for another.
- A Bearing from other things: an empty Social battery, a bad mood, a Lack.
- Sleeping in when the night was short.
