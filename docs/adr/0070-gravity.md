# Gravity: how much a moment asks of the one it reaches

> Status: accepted. Tunes ADR 0021 #7 (utterance rhythm). Builds on 0019 (System One weighs, the LLM voices), 0031
> (Reflex) and 0044 (the judge is always there). Vocabulary in `CONTEXT.md`.

- **The dice were blind.** `UtteranceRhythm` drew every spoken line's length from the other person's word count alone:
  after a short line, 70% of cues said "under 12 words", and nothing ever got more than 60. It never looked at what
  was said.
- **What it did.** A human asked Denise "how do you feel about your life, your job, what has happened in recent
  times". She wrote that she wanted to answer honestly and not in one sentence, came back two minutes later, and sent
  one sentence.
- **What is right about it.** Small talk getting a short line is right ("hey, hoe gaat het?", "druk hier, maar goed.
  jij?"). The length rule is not wrong; it is a fixed rule standing in for a judgement.

How much a moment asks of someone is a judgement of relevance, and relevance is relational: not in the line, but in
the line as it meets this person now (Vervaeke). Grice's maxim of quantity says the same from the other side: say as
much as the moment requires, and not more.

## Decision

- **Not a new instance: the existing draw made relational.** The length is still drawn; what it is drawn from now
  includes the moment.
- **Weighed where all speech converges.** Every spoken line goes through the Speaking phase: replies and owed answers
  from `TurnAsync`, Life's lines from `InitiateAsync`. Right before the Speaking call, the judge gets one noul about
  this Persona: "What {name} is about to say needs more than a line or two." That is **Gravity**. The state is facts
  only: their name and short bio (the lived sheet's, else the cast bio, else the sheet's first lines), what kind of
  conversation and with whom, the last six lines (clipped), the line being answered, what they mean to get across
  when the turn has an instruction or a Life intent, and for an owed answer that they put it off earlier to answer it
  properly. The wording separated the light scenes from the real ones best of five on the judge (`Gravity_Wording`:
  a gap of 0.29; the others were below 0.1 or inverted).
- **A short budget, out of the way.** 1200 ms, the same as Reflex's judge call on this path, linked to the turn's own
  cancellation. The weighing starts with the Speaking phase and runs while its presence lookups do. On the bench it
  answered in 160 to 460 ms, none of 96 over budget. A missing or failed answer is no view: today's draw, unchanged.
- **Gravity shifts the odds, the dice still roll.** With no view the draw is exactly today's. Gravity is first
  measured from what the judge gives a line that asks nothing: `asked = (g − Light) / (1 − Light)`, clamped to 0..1,
  with `Light` 0.25. The judge never goes below about 0.2 (light moments sit at 0.20 to 0.28), so read raw, every line
  would already ask something and small talk would lose its short lines. Then a new length above Long, **Full**, comes
  up with chance asked^1.5; otherwise today's table, with the short share scaled by (1 − asked) and medium and long
  keeping their ratio. At asked = 0, which is every light moment, that is today's draw exactly. All the numbers sit in
  `UtteranceRhythm`.
- **What it buys.** Over 200 rolls on the bench, small talk, the practical question and the light intent draw Full 0%
  and keep today's shares (short 0.66 to 0.67); a real disagreement draws Full 29%, the production question 35%, bad
  news 43%, Life's substantive intent 59%, the owed answer 85%, and one-liners there fall to 1 to 17%. Raw gravity
  squared drew Full for 4 to 9% of light moments (a 70 to 88 word report of the day on "hey, hoe gaat het?"), and raw
  gravity cubed cost the real moments about a quarter of their real answers.
- **The cue.** Full says: "this one deserves a real answer, so take the room: a short paragraph, under 70 words. Say
  what you actually think or feel about it: the one or two things that matter most, not a tour of everything. Nothing
  made up to fill it out, and you don't have to hand the question back. Write it the way you'd write it in a chat, not
  as an essay." An earlier cue asked for "something specific of your own" and got it made up (a parent in hospital last
  year in most of Pieter's answers to bad news, product facts nobody gave Denise), a job, family and self tour, and
  "what made you ask?" at the end of nearly every one. With this one, 2 of 17 real answers state a personal fact the
  scene never gave, 4 hand the question back and 3 range over three parts of life. Haiku still writes 47 to 116 words
  under it.
- **The style notes stay.** `ChatStyle` and `InPersonStyle` still say short and reactive, a one-liner gets a one-liner.
  They are the default, and when Full is drawn the cue wins on the bench: no change was needed there.
- **Logged.** Every spoken line logs its gravity (or "unweighed") and the length drawn, at Information; the speaking
  span carries both.

## Considered

- **Asking Reflex.** Reflex already weighs a line when it arrives, but it is skipped for the conversation they are in
  the middle of (Focus), which is where depth happens, and it never sees Life's lines. Gravity has to be asked where
  every line passes.
- **Letting the LLM choose its own length.** The style notes and the history already pull it short, and it can't see
  its own habit; the production case is that choice. Weighing is System One's, voicing the LLM's (ADR 0019).
- **A special case for owed answers.** An owed answer is a fact in the state, not a rule: the judge reads "put this off
  earlier to answer it properly" and weighs it at 0.93. A rule would make every owed answer long, including the ones
  that were only put off because they were busy.
- **A threshold.** Gravity over some bar means a long answer. That makes the same moment always get the same length and
  folds the tension between matching their length and meeting the moment into one switch. `Light` is not one: it is
  where the judge's scale starts, and below it the length is still drawn, the way it was.

## Consequences

- One more noul per spoken line, on the judge, concurrent with lookups the phase already makes.
- In a DM the Decision phase is almost always the shortcut (being written to directly is urge 1.0), so for DM replies
  the judge sees the line but no intent. Life's lines and Decision-phase turns in rooms carry one.
- A real answer can still reach for something nobody gave them (a client's rough patch, a father's health last year),
  and in a disagreement for product facts; the cue asks them not to, it can't stop them.
- If the judge's floor moves, `Light` moves with it; `Gravity_Replies` shows the shares and the replies,
  `Gravity_Wording` the separation.
