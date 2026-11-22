# The lived body: Reserves a Place fills back up

> Status: proposed. Built and tested, not deployed. Builds on ADR 0018 (Initiative), 0020 (Places), 0021 (lived self),
> 0024 (Wallet), 0031 (Reflex), 0049 (things to do) and 0057 (Arena fit). Vocabulary in `CONTEXT.md`.

A Persona had a body the way a chess piece has one: a Place, a Spot, a posture. Others could see it and Earshot ran
off it, but nothing was felt from inside it and nothing in it ran down.

- `Energy` was one of three words written with the Day each morning and printed unchanged until the next.
- "Make yourself a coffee" dirtied a cup and did nothing to whoever drank it.
- The Day listed breakfast, lunch and dinner. No Body ever ate.
- What pulled someone to another Place was people, restlessness, stale talk, a Feature or a Gathering. Nothing anyone
  *needed* was anywhere, so a kitchen, a café counter and a sofa afforded nothing.
- Hours of talk cost the Social battery and nothing else. No choice was ever forced by being finite.

Vervaeke grounds relevance realization in an autopoietic bio-economy: a body with finite energy and time that can
fail, so that every moment is a trade-off (Jaeger, Riedl, Djedovic, Vervaeke & Walsh 2024). A body that is only
*mentioned* is propositional. What it costs to be one was missing. Three more ideas shape the design:

- **A working body is absent** (Leder). It shows up when it stops working. So a Reserve in its comfortable middle is
  never said; a damper is an expectation.
- **Constraints select and enable** (Juarrero). Hunger takes options away. A kitchen, a counter and a sofa are what
  give them back, which is the agent–arena fit of ADR 0057 with something at stake.
- **Suffering is undergoing**: a loss of agency, not a level of pain. So an empty Reserve narrows what a Persona can
  consider. It never chooses for them.

## Decision

1. **Two Reserves beside the Social battery**: fed and rested, on the lived self (`Bio`, `SelfState`). Nothing ticks:
   like Furni they are read off timestamps. Fed drains 0.11 an hour, rested 0.036.
2. **Only time lived counts.** A Life turn settles the body, and nothing drains between turns: a night, a paused World
   clock or a Party that only uses Rooms costs nothing. The turn after a gap counts at most 75 minutes of it. Two
   hours without a Life turn and the body is not felt at all: no line, no pull. Twenty-six hours without one and it
   begins ordinary again. Scenario Rooms never see it.
3. **Sleep restores.** Getting up adds 0.14 rested per hour slept and leaves fed between 0.30 and 0.44, so people wake
   wanting breakfast. The night itself drains nothing. Woken before morning (a message, a walk) still gets them their
   rise when morning comes. A body nobody lives in doesn't rise. Up more than 26 hours means a rise was missed: the
   next Life turn restores rested as a night would and leaves fed alone.
4. **The Day binds the body, and the body beats the Day.** The morning's `energy` caps rested (low 0.58, medium 0.8,
   high 1), and the Energy shown is the lower of how they woke and how the day has gone. Away at a Locale, offscreen
   life feeds them: fed never drops below 0.6 there. But a meal the Day lists is a plan: while they're hungry the block
   says it hasn't happened, and "carry on with your day" drops its beat so it can't stand in for lunch.
5. **Felt as fact, off the middle only.** From fed 0.45 down: "You're hungry: nothing since 08:10", then "really
   hungry", then "famished … hard to keep your mind on anything else". Rested under 0.15: "dead on your feet, up since
   07:20". Nothing while they're fine.
6. **Places give Sustenance**, read off what a Place is and holds (`Sustenance`):
   - your own Home: cook, free, at the kitchen Spot; so does a public Place with a kitchen in its scene, unless it
     sells food. Where food is sold nobody cooks for free;
   - where food is sold: a meal for 9 credits, a light one for 6, something small for 3, from the Wallet. What they
     can't pay for isn't offered to order. Sold is the kit's archetype (restaurant, café, bar); a Place without one
     sells by a venue noun as a whole word in its name ("Café Brecht", "the Rooftop Bar") or a whole phrase in its
     Purpose ("go out to eat", "get coffee", "go for a drink"). A Purpose that only mentions food ("no food or
     drinks", "eat their lunch on the grass") sells nothing, and other archetypes (office, park) never do. Since ADR
     0062 the words are only the fallback: what a Place sells is judged once and kept on it;
   - things that feed (herring cart, ice-cream cart, vending machine), listed under `feeds` in `library.json`, for
     nothing. While a thing's Feature is fresh for them, the Feature is the one offer ("Get a herring at the cart"),
     and doing it feeds. After that it's "Get something to eat at the herring cart";
   - a lie-down at Home, once per waking stretch (+0.3 rested); a coffee lifts rested 0.15 for two hours, nothing once
     they're spent, and nothing is paid back;
   - a walk costs 0.01 rested, a tram ride 0.02.
7. **The landscape shifts with the need.** Eating where they are is offered from peckish, pulls 2 when hungry, 3 when
   very hungry, 5 when famished, and its label carries the fact ("nothing since 08:10"). With nothing to eat here,
   the Places nearby that would feed them say so and pull: "Go to the Café (to get something to eat)", in place of the
   Feature hook. That trip is an option of its own: second thoughts about the Café for company (ADR 0050) don't put
   it off for lunch. A full stomach is offered no food. A lie-down and the coffee stand out between 06:00 and 17:00 when
   flagging; once spent only the lie-down does, at any hour, and Home pulls from elsewhere. (The first evening live,
   the coffee was relief at any hour too: Yusuf, dead on his feet, made six coffees between 18:03 and 19:37, each one
   seated first and lifting nothing.) A tired body explores less: restlessness stops
   pulling once they're flagging.
   From hungry on, the best way to eat always has a seat on the menu: on the first day live it was missing on 69% of
   hungry turns (three seats per App, and talk took them), though the judge put 0.59–0.80 on it when it was there.
8. **Distress narrows.** With a Reserve under 0.15, what would relieve it is seated first, in its app and on the menu,
   and if anything does, Initiative's menu shrinks from ten options to six. With nothing that relieves it, the menu
   stays whole. Reflex has 40% less free attention. Still sampled, still free to decline. It is logged as
   `Distress <name>: hunger|tiredness`.
   The longer it goes unanswered, the less else there is: six options, four once Strain (ADR 0061) passes 0.25, about
   an hour of it, and three once the body is breaking.
9. **Relief is felt by how much it was needed.** Eating appraises at `0.5 + 0.3 × need × amount`: nothing on a full
   stomach, +0.14 valence for a meal when hungry, +0.22 when famished (a Take gives +0.08). The Place is the source.
   Eating by way of a Feature is felt as its Take or as the relief, whichever is more.
10. **Cooking at home feeds whoever's over.** They get an `eat` activity of their own.
11. **It shows.** The Body is "making lunch", "having lunch", "having a lie-down". `GET selves` gives fed, rested and
    the live Energy; the World app says hungry or famished.

12. **A shift has a break in it.** On the first day live, Eiko kept her shrine from morning into the afternoon at fed
    0.00, picking "Get on with work" turn after turn: where nothing feeds, the way to eat read as walking off the job,
    and "that wins over whatever your day had planned" settled it. So on shift a trip to eat is a break ("Take your
    break at the Café", "Go home for your break"), the shift line adds "A break to eat is part of a shift: you go, you
    eat, you come back", and the shift doesn't damp it. "Head back to work" was already there for after. And a "no"
    said earlier (ADR 0050) no longer damps what would relieve a Distress: second thoughts are for what they can
    afford to refuse.

## Measured

Probe `Body_Reserves`, 21 Nov, Jev through OpenRouter, 3 samples: two people at home on the same seven-option menu.
Only the Reserves differ, and so only the body's line and the label.

| | fine | hungry | famished | spent |
| --- | --- | --- | --- | --- |
| Inês, p(make lunch) | 0.01 | 0.29 | 0.80 | 0.00 |
| Pieter, p(make lunch) | 0.04 | 0.44 | 0.64 | 0.00 |
| Inês, p(lie down) | 0.03 | 0.04 | 0.01 | 0.93 |
| Pieter, p(lie down) | 0.01 | 0.01 | 0.00 | 0.75 |

- The need is read off the state. Nothing was added to System One's probabilities.
- It is relational. Pieter "would rather be out among people" and keeps the Café at 0.33 when hungry and 0.28 when
  famished.
- Two things were fixed on the way. As "carry on with your day (…)", rest took 0.82 from a hungry Inês and lunch
  0.04: the Day stood in for eating. As "Do nothing for now" it took 0.36. And before the label carried since-when,
  hungry read 0.05 and 0.22: System One picks from labels (ADR 0021 #17).

Probe `Body_OnShift`, 22 Nov, same judge, 4 samples: on shift where nothing feeds, p(go and eat) by wording.

| | as it was | break on the labels | and in the shift line |
| --- | --- | --- | --- |
| Eiko (dutiful), hungry | 0.13 | 0.26 | 0.47 |
| Eiko, famished | 0.43 | 0.64 | 0.82 |
| Pieter, hungry | 0.58 | 0.81 | 0.88 |
| Pieter, famished | 0.84 | 0.95 | 0.96 |

"Leave work early" stayed at 0.00 throughout: a break is not leaving.

Probe `Body_SpeakingAB`, same day, Haiku as the voice: 2 people × 3 openers × 3 samples per body, one reply each.

| | fine | hungry | famished | spent |
| --- | --- | --- | --- | --- |
| words per reply | 18.2 | 17.1 | 18.5 | 13.4 |
| replies bringing up food | 5/18 | 6/18 | 14/18 | 0/18 |

- Hungry colours without becoming the topic: it came up when asked how the day was going, never on the other two
  openers. The fine arm already talks about lunch at 13:20.
- Famished takes over ("ask me after I've eaten"): attention is what Distress costs. It should not last long: the
  same state reads 0.64–0.80 for eating at the next turn.
- Spent shortens by a quarter and says little about it. "Running on fumes" came from both voices, six times in
  eighteen: one for the stale words to watch.
- One reply in eighteen still squared the Day with the body ("only just got to eat lunch, and I'm starving still").

## In production: the first Sunday

PostHog holds every Life voicing call, judge call and spoken line. Sunday 22 Nov, 07:00–14:36 UTC, against the three
weekend mornings before, same hours.

- **Meals.** In the fourteen days before: none (ten coffees, at most fifteen herrings). In 7.6 hours: 15 meals by 12 of
  13 Personas: 8 ordered at the Café, 6 cooked at home, 1 from a vending machine. Eleven breakfasts between 08:18 and
  10:59, five lunches. Both trips made to eat ended in a meal within nine minutes.
- **What it cost.** Spoken lines held (361 against 372). The Board fell from 16.1% of voiced turns to 7.6% and the
  Calendar from 7.8% to 3.7%; DMs rose from 20% to 26%. Declines stayed at 12.6%; walks were declined 44% of the
  time, 54% before.
- **Speech.** Food came up no more often (13.0% of lines before, 9.1% after). "Starving", "famished" and "running on
  fumes" were said by nobody. No line contradicted the speaker's own hunger line. Three meals in talk were still never
  eaten by a Body.
- **Hunger.** 16 episodes, 15 ended in a meal: 48 minutes and five turns at the median, 96 minutes at most. The wait
  was the menu, not the judge: hence the seat in point 7.
- **Eiko.** Famished from 11:06 with food on her menu on 36 of 37 turns: 0.04 from the judge against 0.91 for work.
  Hence point 12, and the narrowing that grows in point 8.
- **Energy** read low in 62% of morning prompts (14% before): a new body started from yesterday's energy and was
  capped by today's as well. It now starts neutral; from the second morning the rise decides.
- **The judge is two judges.** Jev answered 98% of that morning, d1:3b 86% of the turns after the next restart, and
  d1's probabilities are flat: the same break read 0.82 in the probe (Jev) and 0.15 live (d1). Every probe number here
  is Jev's.
- One Sunday morning against three weekend mornings. No evening, dinner or bedtime in it yet.

## Considered

- **Meals from the Day's beats.** Then the offscreen day feeds everyone and no Place is ever needed. The Day is the
  plan; the Body does it somewhere.
- **A pull without a fact.** Counting hunger in priorities only would hide it from System One and from speech. Facts
  go in the state; priority only seats the option.
- **More bars** (thirst, hygiene, fun). Two are enough to force a trade-off, and both have Places to meet them.
- **Dishes from every meal.** Shared housework: every plant once dried out on the same afternoon (ADR 0020).
- **Eating for them when famished.** Priority foregrounds, it never forces.
- **A flagging line every evening.** Tired at night is expected. The Energy word carries it.

## Consequences

- A state write or two per Life turn and a Wallet read when hungry. No new model call. About three turns a day per Persona
  go to meals, inside the same hourly budget.
- On the wire: `SelfSummary.fed` and `.rested`, `Body.asleepSince`. In code: `Affordance.Meets`, `LifeContext.Reserves`.
- At deploy every body begins ordinary on its first Life turn (fed 0.7, rested by the hour).
- Players: a human driving their own Body has no Reserves. A Player on auto lives like a Persona (ADR 0037), Reserves
  and meals from their Wallet included.
- A Lack (ADR 0047) can now be bodily: hungry, broke and nowhere that feeds reaches the CDU desk as a felt need.
- Meal talk could become a motif. Stale words should catch it; watch the first week.
- The Bench could not reach a model: its host never registered `IAiObservabilityCapture`. It has a no-op now.
- The rates, costs and the 75 minutes are guesses. Read a week of `eat` and `nap` activities and `Distress` lines
  before tuning.

## Not yet

- Breaking down: see ADR 0061.
- Others seeing it, and turning in early: see ADR 0064.
- Counters with someone behind them who earns.
- Attachment to the Place where you eat (ADR 0057's open end): see ADR 0065.
- The Social battery recharging by who is around: see ADR 0063.
