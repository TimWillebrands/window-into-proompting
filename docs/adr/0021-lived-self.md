# Lived self: personas talk from a life, not from a character sheet

> Status: accepted. Builds on ADR 0018 (World, Life) and ADR 0016 (Stances). ADR 0020 is reserved for Places.

A live run with four personas showed the failure plainly. Every line came from the same literary,
confessional register: "load-bearing silence", "the lantern", "the seam", "no rollback". Every message
opened with a stage direction (`*sets the phone down on the table*`), and every exchange escalated into
intimacy. The personas *performed their sheets*. The traits were the only material they had: the sheets
list modes, catchphrases and signature metaphors. Recall and Stances then fed each other's imagery back,
so the register converged across the whole world.

People are not like that. Most talk is ordinary. Depth is rare and earned. What someone says is coloured by
a day they're in the middle of: tired, at work, hungry, out of social energy.

## Decision

Give every persona a **Lived self** that supplies the material a sheet can't, and treat the sheet as
background.

1. **Day** (`PersonaSelfGrain`, key `party:persona`). Once per local day, one cheap General-tier call writes
   the persona's ordinary offscreen day: 7–10 timed mundane beats, plus a mood, an energy level and one
   practical preoccupation. Yesterday's mood and preoccupation carry over for continuity. The prompt
   forbids metaphor and epiphany.
2. **Social battery.** Every spoken line drains it, and it drains more for low-Chattiness personas. It
   recharges 0.25/h and fills with a night's sleep (a new day). When low, it renders as "keep it short,
   or drift off".
3. **Gone stale** (`WorldVoiceGrain`, key Party, cached 90 s). These are deterministic counts over the
   last 80 persona lines across all Rooms and DMs:
   - **Words worn across the world:** used by 2+ speakers in 5+ lines.
   - **Per-speaker tics:** the speaker's own over-used words, the share of lines opening with an
     `*action*`, and average length.

   Rendered per speaker as an anti-convergence hint. No LLM call.
4. **`# Right now` block.** Day, mood, battery and stale words go into the Decision, Speaking, Life and
   Diary prompts. Scenario Rooms skip the day, because their setting owns time and place.
5. **Sheet framing and chat style** (`LivedSelf`). The sheet is introduced as "background, not a script;
   never perform it or reuse its lines". The generic style block becomes "texting": ordinary register,
   plain words over metaphor, you may be brief/bored/distracted, match length, no stage directions.
6. **Stage-direction cleanup.** `SpeechCleanup` drops narrated actions: 4+ words inside asterisks, starting
   lowercase. A short `*sigh*` stays, and so does capitalised emphasis.

7. **Utterance rhythm.** Each Speaking turn samples a length cue (short, medium or long). The odds track the
   length of the last line from someone else: after a one-liner, 70% of cues are short.
8. **Conversation thermostat** (`ConversationThermostatGrain`, key Room). Once per new message, System One
   answers two `noul` questions over the last 10 lines: *heavy?* and *circling?*. After 4+ heavy readings in a
   row, the Room cues people to lighten up, change subject or drift off. It is off when no judge is enabled.
9. **Visible life.** Messenger shows a persona's current day beat as their personal message, and their
   status turns Away when their social battery is nearly empty.

## Why these pieces

- The day gives people something to say that isn't each other's confessions. It also gives them reasons
  to be short (at work, tired) and grounds Life's "own life" pull in facts instead of improvised filler.
- The battery is the first piece of state that makes *how much* someone talks depend on what already
  happened, not only on Chattiness.
- Stale-word detection is cheap and deterministic. It attacks the observed failure (register convergence)
  directly, without asking an LLM to judge style.

## Baseline (before)

Measured with `GET /worlds/{id}/voice` over the last 80 persona lines:

| Measure | Value |
| --- | --- |
| Average words per line | 81–93 |
| Lines opening with a stage direction | 42–92% |
| Worn words, top 4 | lantern, mōshiwake, verses, load-bearing |

The first lines after the change ran 21–52 words with no stage directions. One persona mentioned her
lease (from her generated day) unprompted.

## Consequences

- Each persona costs one extra General-tier call per day, on the first prompt that needs the day. If
  generation fails it is retried at most every 10 min, and the block renders without the day.
- Diary moods update the Self's mood, so reflection feeds back into the day.
- Next candidates: relationship texture per pair (familiarity, last contact) beyond Stances; an intensity
  thermostat for conversations that stay confessional too long; and a lifelikeness bench (stale-word
  rate, stage-direction rate, length variance) tracked across runs.
