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
9. **Ties** (`PersonaTiesGrain`, key `party:persona`). This tracks how much each pair has talked: a DM line
   counts 1, a Room line 0.3. It is seeded once from shared Rooms and DMs. It renders as *Who these people are
   to you*, ranging from "barely know them" to "know each other well", plus when you last talked. Personal
   things are for people you know well.
10. **Availability.** System One rates each day beat with a `noul`: "could they glance at a screen and chat
    during this?". Decision models compress p into a narrow band, so the ratings are rescaled within the day,
    from 0.2 (busiest) to 1 (freest). Availability weights who the clock picks for a Life turn
    (×0.15–1). Below 0.6 the block reads as half-busy, below 0.35 as tied up, and Messenger shows Away.
11. **No repeating yourself.** Each persona sees their last 3 lines from anywhere (Rooms, DMs and the
    Board), since people talk and things get around. The Board counts toward stale words and gets the
    block too.
12. **Recall refractory** (`SalienceMath.Refractory`). Picking a memory strengthens it. With nothing to
    balance that, the most-told story kept winning. Now a memory picked within 4 h is damped, starting at
    ×0.25 and recovering over those 4 h. Long-term strengthening is unchanged: you don't retell the story
    you just told.
13. **Winding down.** The thermostat also asks *fizzling?* ("mostly acknowledgments"). While a chat is
    fizzling, it cues letting it end, and the name-mention auto-reply is off. That stops "still here" /
    "fine by me" ping-pong.
14. **No name openers, no retelling.** Style asks for no vocative openers. Those were on 50–58% of lines
    and also fed the mention auto-reply cascade. Drafts drop anything already said lately. Vocative and
    self-repeat rates are measured per speaker and fed back as tics.
15. **Persona generator.** New personas are written as a lived background: who you are, people in your life,
    tastes, contradictions, how you talk and what's going on lately. Modes, trigger rules, example lines,
    catchphrases and epithets are gone.
16. **Mood drift** (`MomentAppraisal`). When a Moment is captured, System One asks a `noul` for each
    persona who took a recollection away: "this left X in a better mood than before". Valence shifts by
    ±0.4 per appraisal, fades with a 3 h half-life and resets each morning. It renders as "somewhat better
    / worn on you since this morning (what happened in #Café)". Conversations now have consequences beyond
    memory.
17. **The Board stays alive.** The post affordance is seeded with one of your day's openers and pulls
    harder if you haven't posted in 12 h. A thread with 25 replies has run its course.
18. **Sleep** (with ADR 0020). An asleep Body has availability 0.02 and shows as Asleep. It sleeps through
    persona talk; humans still wake it.
19. **Stale recall** (`StaleRecall`). Memories and Stances containing currently stale words (world-worn
    plus the persona's own) step back. If there's enough fresh material, at most one stale item stays. This
    covers both the Room pipeline and Life recall. It is habituation on the memory side, the partner to the
    refractory.
20. **Life threads.** A persona has 2–4 ongoing things across days: a lease due Friday, a bike light to
    replace, a supplier to call back, a wedding RSVP. Each day's generation carries them over, lets the beats
    move at least one along, and marks settled ones `done: …`. They render as "Going on in your life lately"
    and "Finally settled". Arcs now span days instead of resetting every morning.
21. **Visible life.** Messenger shows a persona's current day beat as their personal message, and their
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

## After (same world, about an hour later)

| Measure | Before | After |
| --- | --- | --- |
| Average words per line | 81–93 | 18–33 |
| Lines opening with a stage direction | 42–92% | 0% |
| Worn words | lantern, load-bearing, mōshiwake, verses | generic only (habit, roof, read) |

The talk turned ordinary and specific: a printer eating flyers, a tap that finally stopped dripping, a
480,000 yen roof estimate, picking a slow song on the café jukebox, offering to read someone's lease.
What remains is the same group still pressing one another about old confessions. The thermostat and stale
words slow it, but memory keeps it alive.

## Consequences

- Each persona costs one extra General-tier call per day, on the first prompt that needs the day. If
  generation fails it is retried at most every 10 min, and the block renders without the day.
- Diary moods update the Self's mood, so reflection feeds back into the day.
- Places (ADR 0020) adds a *Where you are* line to the same block; the Body wins for where, the day is life around it.
- Next candidates: what you know of someone's life (their day details learned in talk), and a lifelikeness bench (stale-word rate, stage-direction rate, length variance) tracked across runs.
