# A body that can break down: Strain and Ailing

> Status: proposed. Built and tested, not deployed. Builds on ADR 0055 (own Happenings) and 0060 (the lived body).
> Vocabulary in `CONTEXT.md`.

ADR 0060 gave a Persona a body that runs down, but nothing it did to that body lasted.

- A Persona could go famished for an afternoon or stay up all night, eat or sleep once, and be exactly as before.
  Neglect cost a bad hour and nothing after it.
- Every 40 minutes one awake Persona gets an own Happening, and one of the eight kinds is "their body: they're coming
  down with something, hurt something small, or can't shake a bad night". The writer said "you wake with a scratchy
  throat" and the Reserves stayed where they were. The body was only mentioned: propositional.
- Which kind came up was a random draw, unrelated to how they had lived.

For Vervaeke, relevance is realized only by a body that is precarious: it can fail, so neglect has to cost something
that lasts (Jaeger et al. 2024). A body that bounces back the moment it is fed has nothing at stake beyond the hour.

## Decision

1. **Strain** is what neglect leaves behind: a number 0..1 on the lived self (`SelfState.Strain`), never said in a
   prompt. It moves only on Life turns, by the same capped lived time that drains the Reserves (at most 75 minutes a
   turn):
   - in Distress (fed or rested under 0.15 after the step) it grows 0.25 per lived hour;
   - otherwise, and not ailing, it eases 0.02 per lived hour.
2. **Nights count.** Getting up after under 6 hours adds (6 − slept) × 0.08; 7 hours or more halves it. Only a night
   that follows a lived day counts (a Life turn in the last 26 hours).
3. **A strained body breaks down.** `Bio.Breaking`: Strain at least 0.5, not ailing, and at least 3 days since they
   last got well. Among those due an own Happening (the 6-hour rest and the exclusion of claimed Players stay), a
   breaking body is picked first, its kind is the body kind, and the writer is told how they have treated their body
   ("they've been running themselves down, going hungry for hours and short on sleep", `Bio.Lately`).
4. **Prose and body agree.** Whenever an own Happening of the body kind is written, forced or drawn, the Persona
   becomes **Ailing** (`IPersonaSelfGrain.AilAsync`): `AilingSince` is set and Strain drops to 0.2. Logged as
   `Ailing <name> (strain 0.62)`. In the 3 days after they last got well (`Bio.Mending`) `AilAsync` refuses, and the
   clock never draws the body kind for them (`IPersonaSelfGrain.MendingAsync`).
5. **Ailing takes more out of them.** Felt rested is capped at 0.38, so Energy reads low and tiredness is at least
   flagging; the stored value drains on underneath. Tiring runs at 1.5×. The Right now says "You're not well today:
   everything takes more out of you." A lie-down stands out at any hour: it can't lift what they feel, but it keeps the stored value from
   running out underneath, which would be Distress on top. The coffee doesn't stand out: it lifts nothing while they
   ail. Away from home "Head home (you're not well)" pulls 2 more and meets tiredness (the "dead on your feet" hook
   wins when both apply).
   Restlessness already stops when flagging.
6. **It ends.** A night of 7 hours or more, at least 12 hours after it began, makes them well; so do 3 days, checked
   on the next Life turn. `AiledAt` is set and logged as `Well again <name>`. Then 3 days before the next.
7. **It shows.** `SelfSummary.ailing`; the World app says "unwell" beside hungry or famished.

Strain also narrows: Initiative's menu in Distress has six options, four from Strain 0.25 and three once the body is
breaking (ADR 0060 point 8).

## Considered

- **Illness as a random event with no cause.** That is what the body kind already was: a draw that happened to anyone.
  Precariousness means neglect is what puts you at risk; a random cold teaches nothing about how you live.
- **A health bar.** A third number in the prompt would be talked about and gamed. Strain stays out of the prompt and
  shows only through what it leads to; a working body is absent.
- **Permanent conditions** (a bad back, a chronic cough). They would accumulate into a cast of invalids with no
  counter-pull. Every ailment ends and the next one waits 3 days.
- **Ailing straight from Strain, with no Happening.** Then the body breaks without a story and the narrator still
  writes body Happenings that change nothing. Tying both to the one Happening keeps one cause and one account.
- **Taking a Reserve to zero.** That would put an ailing Persona in Distress all day and narrow every menu. A cap at
  flagging lowers what they can do without taking over.

## Consequences

- One `BreakingAsync` call per due Persona every 40 minutes, one `MendingAsync` call for the one picked when not
  forced, one state write when someone ails. No new model call.
- On the wire: `SelfSummary.ailing`. In code: `Reserves.Ailing`, `SelfState.Strain`, `AilingSince`, `AiledAt`,
  `OwnHappenings.Body`, `Bio.Helps(Reserves, …)`.
- At deploy every body starts with no Strain and well.
- A body nobody lives in is untouched: a paused World clock or a Party that only uses Rooms never settles, so Strain
  never moves, nights after more than a day without a Life turn don't count, and ailing never makes an unfelt body
  felt. `AilAsync` on a body that was never lived does nothing.
- A drawn body Happening now ails someone who was fine. That's one kind in eight, at most one Persona every 40 minutes
  and each at most once per 6 hours, then 3 days immune: in those days the body kind isn't drawn for them.
- The rates (0.25, 0.02, 0.08, the 0.5 threshold, 0.38, 1.5×, 12 hours, 3 days) are guesses. Read a week of `Ailing`
  and `Well again` lines beside `Distress` before tuning.
- No world-level test drives the clock's own Happening: it fires only after 40 minutes of the clock's own time and
  `OwnHappeningAsync` is private. The pick, the forced kind, the material and `AilAsync` are tested on their own.

## Not yet

- Others seeing it: "looks unwell" at the same Spot, someone bringing soup.
- Ailing keeping them home from a Shift, or calling in sick.
- Different ailments (a cold, a sprain) that cost different Reserves.
- Strain from other neglect: hours of talk on an empty Social battery.
