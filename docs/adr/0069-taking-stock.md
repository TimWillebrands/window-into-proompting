# Taking stock: looking at the way you've been going about things

> Status: accepted. Builds on ADR 0018 (Initiative), 0019 (System One weighs, the LLM voices), 0047 (Lack) and 0066
> (work is remembered). Vocabulary in `CONTEXT.md`.

- **Nobody looked back.** A Life turn is System One picking an option, one call voicing `{thought, intent, decline}`,
  and the App acting. `WorldActivity.Thought` was stored and no prompt read it. Every turn saw the world through the
  way they'd been going at it, never that way itself.
- **What it did.** Two Cytheurgists asked the CDU twenty questions of the same shape in a row and concluded nothing
  from the answers.
- **What broke it.** One extra call, first person and free text ("look back over all of it together: what did it add
  up to, is this getting you anywhere, what would?") broke the loop in 25 of 25 samples on both models. Its notes,
  put in the state System One reads, moved the pick itself: p(ask) fell from 0.36 to 0.05 to 0.12 (`Cdu_FrameLoop`,
  `Cdu_FrameLoopPick` on the bench).
- **What didn't.** One remembered line that only described the habit did not help and raised p(ask). What is kept
  has to say what they'll do differently, not what they did.

In Vervaeke's terms this is the shift from transparency to opacity: looking *at* the frame instead of *through* it.
Rumination (abstract: how am I doing?) and constructive repetitive thought (concrete: what would get me somewhere?)
differ in exactly that question (Watkins 2008). And the feeling of grip, how fast things are working out, is what
tells a person it's time (Kiverstein, Miller & Rietveld 2019).

## Decision

- **A seventh instance of Relevance Realization: Taking stock.** Hours, backward, over their own deeds, in their own
  voice.
- **Weighed in the call Initiative already makes.** Beside Lack and Arena fit, System One gets one noul, and **Grip**
  is one minus it: how far what this Persona has been doing lately is getting them somewhere. It is asked as its
  loss ("{name} keeps doing the same thing and it isn't getting them anywhere"), because that wording separates
  stuck from going well best on the judge (`Stock_Grip`). First look only.
- **An opponent process, sampled.** The chance a turn becomes a stock-take is `(1 − grip)² × ripe²`, where ripe is the
  time since they last took stock over a window of 3 h. Squared, a day that is going fine rarely gets there and a stuck
  one does within a few turns once ripe. Having just taken stock, doing it again is expected and pulls
  little; the pull comes back over hours. There is no baseline: a Persona who is always stuck would have a low usual
  and never feel it.
- **Only when there's something to look back on, and nothing waiting.** At least three things lived since the last one
  (as the Diary counts them: resting, looking, writing and taking stock aren't), no nudge, no Distress, nothing
  pressing on the menu (an option at priority 3 or more that isn't for the body: someone or something waiting; Distress
  already speaks for the body, so "Turn in early" doesn't block it), and an answer from the judge. No answer is no
  view.
- **Voiced once, as free text.** The JSON shapes are part of what kept thought shallow. They get the world as the
  voicing call sees it and their own deeds since they last took stock, oldest first, with what they were thinking at
  each: the first reader `Thought` has, then what they could do from here (the options seated this turn and the rest
  line, as System One reads them). It ends with one line, "So: …": what they see now and what they'll stop doing,
  the kind of thing and not only the last case of it, never a plan or a time. That is the **Upshot**, and only its
  first sentence stands: a plan added after it steered one option for hours in the probe. "So: nothing" is the usual
  answer, and the brief says so.
- **The upshot breaks the frame; the Arena makes the next one.** When it was a plan, it named acts the world doesn't
  have (phoning, "the partner"), set new clock times that the next act promised again, and kept steering one option
  for its 8 h (`Stock_Take`). In Vervaeke's terms insight is breaking a frame and making a new one: they break it, and
  what comes instead is realized on the next pick, from the menu's Affordances, with the notes in view for that act.
- **A turn that takes stock picks nothing.** It's recorded as `stock` on `life`, the upshot as its thought.
- **What it leaves.** The notes are in Life's state for the next act only, with what's on their mind, so both System
  One and the voice read them; they go once one thing has been lived since, and after 2 h at the latest. The upshot is in
  "Right now" for 8 h, in every prompt that speaks as them. The upshot alone is kept as their own memory (source
  `stock`), never the notes.
- **Counter-pulls.** It reads deeds and what the Apps show now, not its own earlier notes. A stock-take that came to
  nothing doubles the window before the next, up to 12 h; one that came to something resets it. One whose upshot says
  what the one still standing says (the share of the shorter one's words the other has, `SameAt` 0.6) is expected and
  counts as nothing: nothing new is kept or captured. All the numbers sit together in `TakingStock`.

## Considered

- **The Stream** (`docs/rfc/2026-11-12-the-stream.md`): a day of thoughts replayed as one conversation, read back
  every turn. More context alone gave about half the effect in the probe; the look back is what moved it. And a day of
  the model's own words as its turns is a second identity nobody reviews.
- **A to-do or mission document.** ADR 0051 already learned that a want written down ahead of the world turns into
  invention. A stock-take reads what happened.
- **A baseline like Lack's.** Lack is a surprise against their usual. A Persona who is chronically stuck has a low
  usual grip and would never trip it, which is the case this is for.
- **An Affordance on the menu.** "Think things over" would compete with what they're stuck doing, scored by the same
  frame that has them stuck. Looking at the frame has to come from outside the menu.
- **Keeping the notes as memory.** The probe's descriptive memory fed the habit. Notes describe; the upshot says what
  they let go of.
- **An upshot that plans.** Tried first; see above.

## Consequences

- One more noul on a first look when a stock-take is possible, and now and then one more CharacterThoughts call
  instead of the voicing call.
- Without notes for one act, the pick after a stock-take collapsed onto what the upshot named (p 0.97 on the bench).
  Bounding them to one act keeps that to one pick; the upshot line still colours the next 8 h.
- An unanswered DM from a human who's at the keyboard is pressing, so a loop with a real person isn't looked at while
  they wait.
- The upshot is in Rooms, the Board and the Diary too: it's something of their own on their mind.
- A Persona who has never taken stock counts from their first deed in the last day, so the first one after a deploy
  comes soon for anyone with little grip.
- If the call fails, the turn goes on to the pick as before; nothing is recorded.
- The window and the count of fruitless ones live on the Self grain; the notes age from when they were written, so a
  fruitless stock-take leaves the last upshot standing until it expires.
