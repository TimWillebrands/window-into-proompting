# Loops close, and lives happen

> Status: accepted. Amends ADR 0043 (restating, lapsing) and 0050 (Happening kinds). Builds on 0021 (lived self),
> 0052 (what the World says closes a thread). Vocabulary in `CONTEXT.md`.

A week of production (7–13 Nov) read as obsession: the same few things, said again and again.

- Justin promised Denise and Henkie the VAT form "tonight" from Monday to Friday and never sent it. Every message
  was honest ("not yet sent means not yet sent") and none of them changed anything.
- Henkie demanded Denise's "staging FAIL-output" on the Board, in DMs and in person for two days. It didn't exist.
  Denise said "ik stuur vanmiddag" each time, then spent eight CDU asks proving it didn't exist, and kept promising it.
- Anne asked "heb je al gegeten?" in nearly every DM, and greeted three people with the same line in one minute.
- What was new came from their own lives: Thomas's shoes, Sienna's piano pieces, Pieter's mother in Beirut. Henkie's
  invention was the same creativity with nowhere to land. Narrator Happenings were sleet and broken coffee machines.

## Why

- **A promise could only close in talk.** Kept or called off was read from a line. Justin couldn't send a form, so
  talking about it was the only move he had.
- **Saying it again moved the deadline.** `Restate` copied the new `DueAt`/`Until`, so "tonight, I promise" pushed the
  lapse out each time and the promise never lapsed. Meanwhile `# What you owe` ("said again 6 times since; not done
  yet") reminded the promiser every turn, and "Denise told you …" reminded the holder. Both sides were fed the loop.
- **Anti-repetition only subtracts.** Gone stale, stale recall and "say something new or say nothing" remove options
  and add none. The model falls back on its strongest pull: the debt, or its core drive.
- **Nothing new enters a life.** The Day is written once a morning and is "a little boring" by design. Happenings land
  at Places and are told to steer away from what people talk about.

## Decision

1. **Saying it again keeps the first deadline.** `Restate` counts and touches, nothing else. A put-off lapses on time.
2. **What's done off-screen is settled off-screen** (`OffStage`). Every promise except a come-over gets `SettlesAt`:
   4 h after a "soon", 14:00 on the "tomorrow", 2 days after a set day. Promises made before this settle 4 h after
   they came due. At the promiser's next Life turn past that, System One reads *being who they are, did they get it
   done?* (the put-off count is in the read), and the outcome is drawn from that p. A narrator line says how it
   ended, either way with nothing left owed: done is `Fulfilled`; not done is `Abandoned` and cools the holder as a
   no-show. It's recorded (`life`/`offstage`), remembered by both, and the holder is notified. That memory is also
   what lets the thread review (0052) close the matching thread or Preoccupation.
3. **Talk can be ratified.** The Narrator gets a ninth kind: one thing people keep talking about that nobody has seen
   turns up for real or turns out to be nothing. It's only picked when there's talk, and for that kind the recent
   lines and the world's worn words are handed over as the subject, not as something to steer away from.
4. **Lives happen.** Every 40 minutes (off with ambient Happenings), one awake Persona who hasn't had one in 6 hours
   gets an own Happening: family or friend news, a letter or bill, something broken at home, a turn at work, the body,
   an offer that won't wait, a thread taking a real step, a small good thing (never one of the last three). Written
   from their lived sheet, today's beats, Preoccupation and threads, in the second person. It's in their Right now
   for 12 h, in the next Day for 36 h, remembered by them alone, recorded as `life`/`lived` (kept out of "What you did
   recently"), and they're notified.

## Considered

- **Classifying which promises can't be kept in talk.** Settling all of them is simpler. A story that never got
  told can be told off-screen too, and one kept in talk first is already settled.
- **Settling at the lapse.** That leaves a day of reminders, which is the loop.
- **More bans** (cap "said again", ban the motif). Subtracting is what failed.
- **Own Happenings in the Day call.** It's written once a morning; news has to arrive mid-day to change anything.

## Consequences

- One System One read and one small LLM call per off-screen promise; one LLM call per own Happening, at most 36 a day.
- A promise can now come true for something that doesn't exist (Denise "sent" the FAIL-output). That is the point:
  the World ratifies talk instead of leaving it open.
- Players' own characters on auto get no own Happenings: their life is the human's.
- The 4 h / 14:00 / 2 day settle times and the 40 min / 6 h cadence are guesses; read a week of `offstage` and
  `lived` activities before tuning.
