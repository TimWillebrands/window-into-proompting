# Rooms weigh once: Reflex decides, the voice speaks and may pass

> Status: accepted. Supersedes ADR 0010 (two-phase response). Amends ADR 0001 (what the cheap phase is), 0015 (what
> counts as a pick) and 0031 (cold open, torn). Builds on 0019 (System One weighs, the LLM voices) and 0070 (Gravity).
> Vocabulary in `CONTEXT.md`.

- **Group talk was weighed twice.** Everything else a Persona does follows ADR 0019: System One weighs, the LLM voices
  and may decline. Life does. DMs did in practice, because being written to is urge 1.0 and the Decision phase took its
  shortcut. Group Rooms and Place talk did not: after Reflex let a line through, a second LLM call, the Decision phase,
  decided again whether to speak, picked a memory and wrote the line itself (`wouldSay`) as JSON, on the General tier,
  from the last eight messages. The voice then only reworded it.
- **What that meant.** The cheapest model with the least context wrote group talk. The calibrated weighing (Reflex) was
  done again by an uncalibrated one. DMs never had a memory picked at all: only the salience floor could put one in.
- **What the Decision phase carried that nothing else had.** Room pressure: how many lines Personas had traded, how much
  of the talk was this Persona's own, the thermostat's read. Those reached it only as a "(Room: …)" nudge. Reflex's judge
  saw none of it. A repair hint was rendered nowhere else.
- **A cold open skipped Reflex.** A human's line in a group never reached System One; each listener's Decision call
  decided, and in practice all four answered (`Room_Speech`, below). It was also true of any human line that was still
  the newest message, and an open slot from another listener hid it, so the urge (and whether they were named) was read
  off an empty slot.

The baseline on the bench (`Room_Speech`, seven scenes, four Personas, 4 samples, Haiku for every tier, the judge live):
a human's small talk and a human's big news each got four answers in every sample. Production runs the Decision phase on
a cheaper model than Haiku, so this baseline flatters the old path.

## Decision

- **One weighing, then one voice.** Every spoken reply in a group, a Place or a DM is: the Urge pre-gate (a cost filter,
  unchanged), Reflex (whether and when), the one System One call that already runs right before the voice (Gravity, now
  also the memory), then the Speaking phase, which voices from the conversation itself and may pass. No other LLM call.
- **Reflex gets the room as facts.** Its state now says how many lines Personas have traded here with nobody else in
  between and who, how many of the last eight lines were this Persona's, whether the line names them, and the
  thermostat's cue when it has one. Facts in the state, not pulls (ADR 0031): the options and the trait shaping are as
  they were. The thermostat is not computed for Reflex: it peeks at the last read (interleaving, so concurrent
  listeners don't queue), which may be one line behind; a read of talk older than 20 minutes, or of a room that now has
  fewer than four lines, is no read. The reads themselves are computed where they were, when someone speaks.
- **A human's line goes through Reflex like any other.** The cold-open bypass is gone. That a human is answered lives in
  one place, `OwedAnswers` (ADR 0031): every listener's end of turn counts, a Reflex "didn't notice" or "listen" or a pass
  included, and if nobody said anything the keenest answers. `leave` toward a human who named or wrote to them is now
  impossible rather than nearly so: an option weighed at exactly zero is never drawn. The urge is read off the line being
  answered, not whatever is newest, and a first name counts as naming them ("Pieter, ..." names Pieter Voss).
- **Holding is per person.** An Intention someone put off holds the next lines from the person it is owed to, in that
  room, and settles their line for the Room. A line from anyone else in that room is weighed like any other and neither
  renews nor settles it. Before, one held Intention marked every later line in the room as answered, so a second
  person's question could go unanswered by everyone.
- **Which memory surfaces is System One's.** The Gravity call gets a second question: which of the recalled memories, if
  any, is on their mind in this moment, the candidates and "none" as options. It is sampled at 0.8, as Life samples,
  and an option the judge weighs below 0.05 (`Gravity.RecallFloor`) is not drawn, the way a 0 is not: the judge's faint
  tail is noise, and a pick is strengthened.
  A sampled memory reaches the voice as a Decision pick did ("# A memory surfacing for you") and is strengthened at once,
  as a pick was. "None" is a view: nothing surfaces. Only when the judge gives no view does the salience floor decide, as
  it did before. DMs get this too. The papertrail's `recall` log keeps its shape.
- **The voice may pass.** Where passing is allowed the last guidance line is "If, being who you are right now, you'd let
  this one go by after all, write only: (pass)". Allowed: a group or Place line that didn't name them, any line Reflex
  was torn about, and a conversation the thermostat reads as winding down. Never: an owed answer, or a human who wrote to
  them directly or named them. A pass ends the turn as declined ("Let this one go by."), sends `declined`, and counts
  for `OwedAnswers` like any decline. A pass is the whole line or nothing: only when the line, stage directions
  stripped, is exactly the bracketed mark, in any casing, with any surrounding punctuation or markdown. "Pass.",
  "Passed!", "(passing)" and "Math (passed), physics..." are lines. A "(pass)" among other words is stripped and the rest
  published ("Sure (pass)" says "Sure"). The stream holds back whatever could still become the mark, so it never reaches
  the room. The whole mark where none was offered is retried, never published. A line that comes out empty sends
  `declined` too, and so does a turn the race cancels before any text was released.
- **Focus yields to a Persona's line in a conversation that is winding down.** A conversation they are in still holds
  the workspace and its next line skips Reflex, unless the thermostat reads it as winding down and the line is a
  Persona's: then Reflex weighs it, with that read in its state. A human's line keeps Focus, so a human's closing "haha
  ok, thanks!" still gets one reply at once, not an ack and a get-back. This was not in the first cut; the bench showed it
  was needed (below).
- **What else the Decision phase carried.** A repair hint is one guidance line in the voice ("Just before you spoke, Mira
  said: ... You hadn't seen it when you wrote your last line; pick it up if that's natural."). It lasts ten minutes
  (`InFlightStore.RepairHintLasts`), since an older one would land in an unrelated turn. Gut reaction and
  `wouldSay` are gone for replies; Life's lines keep their thought and intent. Torn and winding down only open a pass. An
  owed answer goes straight to the voice. Sleeping still declines the turn.
- **Events, the stored shape, the race.** `attend` at the start, `appraisalComplete` when the voice starts, `declined`
  on a pass. The appraisal keeps `instruction` (null for replies), `reason` (the Reflex note, the pass reason, or
  "owed: nobody else had answered it") and `recall`. Everything before the voice starts is still the cheap phase
  (`InFlightPhase.Weighing`) that a newer line cancels outright. During the voice the race now weighs the line being
  answered and the text so far, instead of a gut reaction and planned line that replies no longer have.

## What the bench showed

`Room_Speech` before (`tools/bench/bench-runs/Room_Speech/20261126-083715.json`) and after
(`20261126-090214.json`), speakers per sample:

| Scene | Before | After |
| --- | --- | --- |
| G1 a human's small talk, quiet group | 4 4 4 4 | 3 2 2 3 |
| G2 a human's big news | 4 4 4 4 | 3 3 2 3 |
| G3 one Persona asks another by name | 2 2 1 2 | 1 1 0 1 |
| G4 two traded five lines, a banal next one | 1 1 1 1 | 1 1 1 1 |
| G5 a fizzling conversation | 0 0 0 0 | 1 1 0 0 |
| G6 a stranger walks up in person | 2 2 2 3 | 2 3 2 2 |
| G7 a line one of them remembers something about | 2 2 2 1 | 1 1 2 2 |

- **The pile-on on a human's line is gone without anyone being left out.** Vlad, the quiet one, now listens to small
  talk; nobody was ever owed an answer in either run, someone always spoke.
- **The voice almost never passes.** 0 passes in 57 voicings with a pass on offer in the first after-run, and in
  `Room_Pass` (three wordings, nine Persona-scene pairs, 8 samples) 5 of 216, all Vlad facing the stranger. Wording did
  not change it. So the pass is a safety valve, and Reflex does the weighing.
- **Which is why Focus had to yield.** Without that change the two in the fizzling conversation were in Focus, skipped
  Reflex, and reconfirmed "Ja, zaterdag 17:00." in every sample (2 to 3 speakers per sample, where the Decision phase had
  declined all 8). With it Reflex reads the thermostat and lets it go 6 of 8 times.
- **Memory picks look right.** In G7 the judge puts 0.83 on the plainly relevant memory for Pieter and 0.92 to 0.96 on
  "none" for the two with nothing relevant; across the two after-runs Pieter's voice got the relevant memory 4 of 5
  times and a weaker one once (sampled at 0.08).
- **Cost.** One LLM call per spoken line instead of 1.54, three System One calls instead of 1.88 (Reflex now weighs
  every listener, the human's line included). The Gravity call with the memory question answered in a median of about
  240 ms, none over its 1.2 s budget. Wall time per spoken line in G2 fell from 3.96 s to 1.62 s, in G7 from 4.67 s
  to 2.26 s.

## Considered

- **Keeping the Decision call but dropping `wouldSay`.** It would stop the cheap model writing the line, but keeps the
  weighing done twice, once uncalibrated, and the extra call and its latency on every group line.
- **A pass as a second System One call.** A noul before the voice ("would let this one go by") is calibrated and cheap.
  It would weigh whether to speak a second time, right after Reflex weighed it once, with the same facts. Given how
  rarely the voice passes, this is the lever to try next if Rooms prove too talkative; Focus yielding to the thermostat
  covered the case the bench found.
- **Keeping the cold-open bypass.** Its reason, that nobody answering a human reads as broken, is already guaranteed by
  `OwedAnswers`, in one place, for every path. Keeping it was what made all four answer every time.
- **Strengthening a memory only when it is spoken.** A pass would then leave the memory untouched. Kept as a pick was,
  strengthened when it surfaces, so the meaning of `recall_count` does not change under the salience math.

## Consequences

- A busy Persona asked by first name is now seen as named. In G3 Pieter, at his desk with a file open, put Denise's
  question off ("soon") in all four samples: he means to get back to her, which the bench does not follow, and in one
  sample nobody said anything right away. Before, the name went unseen and he answered at once.
- Reflex sees the thermostat as of the last time someone spoke, so a line is weighed against the read before it. One
  line stale is enough to tell that a conversation is winding down.
- A turn cut off mid-voice now ends with a done event after its emote, so the room stops showing them as typing.
- The window in which a newer line cancels a turn outright is shorter: recall, presence and one judge call, not a
  Decision call. A first reply that lands while another listener is still reading stops that one ("read on"); one that
  lands while another is speaking goes to the race, as before.
- Focus in a winding-down conversation costs a Reflex call it used to skip, and can now go unnoticed under load.
- `PersonaDecisionService`, `ShouldRespondResult`, `LlmJsonParsing` and the `appraisal` stream event are gone. The bench
  probes `Decision_ColdOpen` and `Decision_DirectMention` are retired; `Room_Speech` covers both cases on the new path.
- In a DM the judge now weighs a memory where only the salience floor did, so callbacks in DMs become more likely.
