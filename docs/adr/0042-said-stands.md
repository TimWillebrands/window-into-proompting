# Said stands: consistency as the dampers' counter-pull

> Status: accepted. Builds on ADR 0015 (Recall), 0016 (Stances, latest wins), 0021 (Lived self: no repeating
> yourself, one story one slot, stale recall) and 0038 (the seed wins). Vocabulary in `CONTEXT.md`.

Prod, one night: Yusuf's diary had Amira off the train and hugging him at 03:39, while his Board said her train lands
at 2. His radiator went from "landlord says we'll see" to "visit booked" to "got its visit" to "clanks all night" in
five hours. He wrote about 40 diary entries before noon, each a retelling of the last. Meanwhile radiators, bread and
crisps spread from one persona to the next.

Every anti-repetition damper (ADR 0021 #11, #25, #33) says *you've told this*, and none says *so it's true now*.
The Board asked for "something genuinely new" on a topic already posted, so the model invented progress ("5am posts
are getting repetitive, so here's something new: amira's train lands at 2"). The diary never saw its own entries and
was asked "what happened today" at 03:39, before the day's first beat, so it narrated the day's plan as past. Its
inventions reached Life through the diary's perception line and went on to the Board. Novelty had no opponent.

## Decision

1. **Your own words are in view, with their age.** *What you said lately*, your recent Board posts and diary entries
   are shown wherever you speak or write, and every recalled memory carries how long ago it was (`RecallVariety.Aged`).
   No rule says what to do with them: the model sees what it said and what happened since, and reconciles them. The Board
   no longer asks for "something genuinely new". (Amended: an earlier "what you said stands" instruction was dropped;
   it would have locked in whatever was said first, invented or not.)
2. **The diary reads what it wrote.** The writer sees its last 3 entries, as the Board writer sees its last 5 posts.
   What happened is the day so far, what you did and what you remember. What's still coming up hasn't happened.
3. **One story, one slot, latest telling.** Near-duplicate memories still share one slot at the strongest one's place,
   but the slot now holds the newest telling: latest wins, as it does for Stances. The story is remembered as it was
   last told.
4. **Recall keeps both arms at any size.** "The now first" cut Life's recall to 4. The relevant arm kept its 5 slots,
   and Life anchors on the whole cast plus everything on screen, so the relevant arm took every slot: the strongest
   memories about whatever the feed was on about. The relevant arm now gets at most half the slots, rounded up
   (`RelevantSlots`). The recency arm ranks only the newest 3 per slot it fills (`RecentPool`, up to 50), so "recent"
   means the last few moments, not the strongest of three days.

   | Caller | Limit | Relevant / recent before | After | Recent pool before → after |
   | --- | --- | --- | --- | --- |
   | Room pipeline | 10 | 5 / 5 | 5 / 5 | 50 → 15 |
   | Room initiative line (uses only its Stances) | 5 | 5 / 0 | 3 / 2 | 50 → 6 |
   | Life, Diary, Reflex, DesireReview | 4 | 4 / 0 | 2 / 2 | 50 → 6 |

5. **The Day draws its own small things.** Independent calls can't see each other's dice (ADR 0038), so every Day in
   November Amsterdam had a radiator. Code draws where today's small annoyance and small good thing come from
   (`SelfMath.SmallThings`, stable per persona and date). Variance comes from the draw, not from naming the motifs to
   avoid, which would prime them. (Amended: replaces an earlier "everyone is on about…" line.)

## Why tuning, not a new path

Each change sits on a process that already exists, and instructions stay few: the model gets the facts (its words,
their age) and decides. Recall's two arms come back by fixing a quota, not by adding a stage. Latest-wins is the Stance
rule applied to the story slot. The Day's variance is a code-side draw, the simplest counter-pull to convergence. No new
state, grain, LLM call or Recall path.

## Consequences

- Room recall leans more on the last few hours: its recency pool is 15, not 50. Check `RecallScenarioProbes`.
- A persona's own words are still only as consistent as what they can see: the last 3 lines, 5 posts, 3 entries and 4
  memories. The Day's threads, their lasting self-knowledge, still aren't updated by what they say during the day.
- How often the diary is chosen (Initiative) and the overnight activity (sleep) are unchanged here.
