# RFC: what a Persona thought today stays with them (was: the Stream)

> Status: signed by the council (three of three seated, round 3). Awaiting the author's decision; if accepted it
> becomes ADR 0053.
> Builds on ADR 0018 (Initiative), 0019 (System One; its 2026-11 amendment lets System Two decline), 0026 and 0043
> (Desires, Promises), 0050 (own tempo, second thoughts) and 0052 (what the World says closes a thread).
> Vocabulary in `CONTEXT.md`.

## What happened to the first draft

The first draft proposed **the Stream**: a grain holding one waking day of a Persona's thoughts, shown to the LLM as one
conversation (what reaches them as `user` turns, what they thought as `assistant` turns), ended by sleep. Three
reviewers read it against the code and all three rejected it. A fourth seat (Mistral Large 4) could not be filled.
Their findings were checked and hold:

- **The lead evidence did not show the gap.** Denise asked the CDU for the same thing at 17:19 that she had dropped at
  17:08. But the desk already shows the last four exchanges, the CDU's answer included (`CduScreen.cs:51,110-111`), so
  the "no" was in her prompt. The Preoccupation pulled harder, and ADR 0052 fixed that. A Stream would not have.
- **It failed its own test.** The draft rejected a rolling summary as "a second, unreviewed identity", then proposed
  replaying about a hundred LLM-written thoughts as the model's own turns. Calling them entries does not change what
  the model reads. Past `assistant` turns also work as examples to imitate: declines invite declines, a phrasing used
  once converges. This project already fights that in speech (stale words, ADR 0021).
- **The seam and the routing don't fit it.** `IWorldLlm` sends one system and one user message
  (`WorldServices.cs:80-84`), and a refused answer goes to the next of three providers (`WorldServices.cs:29,65-104`),
  so another model would continue "its own" turns as a matter of routine. The cache argument was void: a Persona acts
  about every ten minutes or less often (the World budget is 40 actions an hour, `WorldClockGrain.cs:472`), and the
  draft's own collapsing rewrote the prefix.
- **Sleep was the wrong reset.** `ThreadReview` runs on rest turns, at most every 3 h, not at sleep
  (`PersonaLifeGrain.cs:57-59`). A Body without a Home never sleeps (`PlacesGrain.cs:235-236`).
- **A plan that lives in a thought moves nothing.** System One picks from the menu. Text in a prompt gives an
  Affordance no pull.

So the conversation is not built. What the reviewers found underneath is smaller and is what this draft proposes.

## Where we are

A Persona's identity is state; the LLM is a driver. No Persona runs a loop: a Life turn is a fixed chain (System One
weighs, the LLM voices or declines, the App writes; a Look repeats the first two once), with no iteration. Much of what
the first draft called missing is already there:

- The immediate outcome of an act is in the next prompt: `ActOutcome.Summary` is stored (`PersonaLifeGrain.cs:233`)
  and rendered under "What you did recently" (`:158-161`).
- What an App holds is perceived again each turn: the desk's last exchanges, a DM's badge, a Board reply.
- A refusal is remembered and damps its option (`SecondThoughts`, ADR 0050).

Three narrower gaps remain.

1. **A thought is written and never read.** `WorldActivity.Thought` holds why they did it. The next prompt prints
   `action + summary` only (`PersonaLifeGrain.cs:161`).
2. **A late answer is not recorded as theirs.** An ask at the desk is recorded as "asked the CDU … a question",
   without the question (`CduApp.cs:61`). The answer is recorded as a clock activity with no `PersonaId`
   (`Cytheurgy.cs:177-181`), so it never appears among the asker's own deeds. It is in view on shift, among the
   desk's last four exchanges, and the next morning's Day is written with the desk's last three answers (ADR 0052,
   `CduScreen.DayFacts`). An answer that arrives later the same day, off shift, reaches them through neither.
3. **A Persona cannot set themselves a plan.** What spans turns exists only where code reads it off speech: a Promise,
   an Intention, a Gathering. "Finish this, then call Lena" has nowhere to live and pulls on nothing.

There is no production evidence yet that closing these changes behaviour. That is why slice 1 below is nearly free and
measured before anything else is built.

## Proposal

No new grain, no conversation, no tool calls. Three changes to state and to one prompt, each kept only if it earns it.

### Slice 0: a baseline

Before any change, from what is already recorded (every LLM call is a PostHog `$ai_generation` with tokens and timing;
the clock's activity log; the desk's exchanges):

| Measure | How it is counted |
| --- | --- |
| Repeat asks | A desk exchange whose question near-duplicates an earlier one by the same Persona the same day, after that one was answered. Per 100 exchanges. |
| Repeated thoughts | Pairs of a Persona's thoughts within one day with token-set overlap of 0.8 or more. Per 100 thoughts. |
| Declined picks | Share of Life picks thought better of. Was about 8% (90 of 1,100, ADR 0050). |
| Fizzles | Share of Life turns ending in `fizzle` or `error`. |
| Voicing call | Input tokens and p95 latency of the Life voicing call (`PersonaLifeGrain.cs:207`). |

### Slice 1: the voice reads what they were thinking

The voicing call, and only that call, prints each recent deed with the thought that led to it:

```
- 10 min ago: say told Lena you'd eaten (you were thinking: she keeps asking, so something is up)
```

- **Only after the pick.** "What you did recently" sits in the block that System One reads as well
  (`PersonaLifeGrain.cs:158-161`, `state` at `:174`). That block stays `action + summary`. The thoughts go into the
  voicing call's user message (`:207-209`), where the option is already decided. System One's state gains nothing:
  it already sees the deeds, refusals are already damped, and prose about them would count them twice (ADR 0031 found
  exactly this with load).
- **So slice 1 cannot change what is picked.** It can change what they mean to say or do with the pick, and whether
  they decline it. Moving the pick is slice 3.
- **Which thoughts.** Those of the deeds printed today: the last five (`:91`, `:161`), under an hour at six turns an
  hour. From the existing activity log; nothing new is stored. Only since local midnight of their `DayKey`
  (`PersonaSelfGrain.Whereabouts.cs:13`), which is a new filter: the read at `:86` has none. Someone awake at 00:30
  starts clean, slept or not, Home or not.
- **Not for a pick they thought better of.** Its deed line stays as it is ("thought better of it (…)"). Its thought
  is not printed: the option is already damped, and a refusal read back invites the next one.
- **As reported text**, inside the one user message. Never as an `assistant` turn. Clipped, and the stale-words list
  applies as it does to speech.
- **A prompt filter for what was private.** New, in Life; it borrows the classes `AccessRules.CanSeeActivity` uses for
  the feed. A thought from a DM with a human (the human is `PersonaId` or `WithPersonaId`) is printed only when the
  decided option is that same DM. A thought from a Diary turn (signed-in viewers only) is printed only on a Diary
  turn. Otherwise a thought formed over something private could be quoted in a thought that is published with a
  public activity.
- A turn that timed out or errored has no thought and adds none.
- **A second rendering, not an edit.** The deeds block is shared with System One (`world`, `PersonaLifeGrain.cs:165`).
  The voice gets its own rendering of the same deeds with their thoughts; the shared block is not touched.
- **Also logged, with no effect:** the voicing JSON's `next` and `about` (slice 3), to get the rate at which they act
  toward that person unaided.

Behind a per-Party switch. Kept only if, against slice 0: repeated thoughts do not rise, declined picks stay under
12%, fizzles do not rise, and voicing input tokens grow by less than 15%. It is worth having only if the Bench shows
follow-through.

Bench Probe: one scripted day, two arms (off, on), at least ten seeds per arm, paired by seed since picks are
sampled, reported with the paired confidence interval. Follow-through is counted: the script plants a reason in a
thought ("Lena said she is moving on Friday") at most four turns before a turn whose option the reason bears on, and
counts whether that turn's intent names it.

### Slice 2: a late answer is recorded as theirs

- The summary of an ask carries its substance: "asked the CDU …: are there staging numbers?" (clipped).
- The record of an answer meant for one person gets that person's `PersonaId`. It is the existing record
  (`Cytheurgy.cs:177-181`); the asker is already known (`word.Only`, from `CduGrain.cs:136`). No second activity. It
  then appears among their own deeds, off shift too, in the block System One reads.
- That record is not a use of the desk and not a thing lived: its action is left out of habituation
  (`PersonaLifeGrain.cs:104`) and of the Diary's `Lived` (`BoardApp.cs:453`).
- It also becomes the asker's latest line in the feed. It was public by name already.
- `ThreadReview` keeps reading Recollections only. The desk's answers are already captured as work memory
  (`Cytheurgy.cs:187`).
- Other Apps are added only where perception does not already show the outcome. Messenger and the Board do.

Kept if repeat asks fall against slice 0.

### Slice 3: a plan is a Desire they gave themselves

Only if slice 1 shows follow-through in the Bench.

- The voicing JSON's `next` ("what you mean to do after this, if anything") and `about` (the name of who it concerns,
  or nobody; the shape Mimesis uses) become an Acquired Desire with a new source, `Plan`.
- It is state, and it pulls through what exists: it is rendered with their wants, and an Affordance toward its person
  gets +1 (`ctx.WantAbout`, `PersonaLifeGrain.cs:112`). That holds while it is one of the two wants on their mind
  (`DesireRules.OnMind`, `Desires.cs:87-91`). A cast member named in the text pulls even when `about` is nobody
  (`Desires.cs:121-123`); a plan that names no one is rendered and pulls on nothing.
- A new plan is the most recently touched want, so it usually takes one of the two lines on their mind and pushes an
  older want, with its pull, out of them. That is a cost, and it is counted: how often a plan displaces a want.
- At most one open at a time; a new one replaces it. It is kept when they act on an Affordance toward its person, and
  it lapses when their Day ends. Nothing crosses the night as text: what should outlast the day is a Promise or a
  Desire they already have.

Kept if plans are kept more often than the unaided rate logged in slice 1, and declined picks do not rise.

## What does not change

System One, its sampling and its state. Reflex. The Decision and Speaking phases; they do not read thoughts. Recall and
strengthening (ADR 0015: only a Decision pick strengthens). Look, which keeps its second choice inside the turn so it
lands while the thing is in mind. The Day, which stays sealed (ADR 0052). Who gets a turn and when.

## Not doing

- **The Stream as a conversation** (chat turns, a day of history, a new grain). Reopened only if slice 1 is kept and
  the measures still show thoughts being lost, and then first as a Bench arm against slice 1, with prior thoughts as
  reported text and never as `assistant` turns.
- **Tool calls or an agent loop for Personas** (ADR 0030). The CDU stays the only agent.
- **Wondering** (asking memory a question). Recall already runs inside the turn. A step at the clock's tempo would
  take ten minutes to remember something and spend the action budget. A same-turn second call may be proposed
  separately; it must not strengthen until the refractory effects are measured (`SalienceMath.cs`).
- **Thoughts in System One's state.**
- **Carrying anything across the night as text.**
- **A thought-first order** (think, then match the want to an Affordance). It would move the pick to the LLM and undo
  ADR 0019.

## Risks

- **Self-imitation.** Even as reported text, five earlier thoughts are examples. The repeated-thoughts measure is the
  check, and the switch is the way back.
- **A thought becomes a fact.** A wrong guess read back reads as known. Thoughts are printed as "you were thinking",
  next to the deed and its real outcome, never alone.
- **Plan spam.** A model asked for a `next` will always have one. One open plan, replaced by the next, lapsing with the
  Day; and slice 3 waits on slice 1.
- **Privacy today.** A thought is stored on the activity and served with it (`WorldController`, `FeedPrivacy`). A
  thought formed with a DM in view and attached to a public activity may already leak, before this RFC. That deserves
  its own look; the filter in slice 1 only keeps this change from adding to it.
- **The window is short.** Five deeds, under an hour. A morning's reason does not reach the evening. A longer span
  means a store per Persona, which is a new decision.

## Settled by the council

1. Private thoughts: read back only after the pick, in the voicing message, and only into the same DM or the Diary.
2. The 12% and 15% bars stand as tripwires. Slice 3's bar is the unaided rate, not a guessed share.
3. A plan is a Desire with source `Plan`, not an Intention: an Intention is a reply put off in a Room and comes back
   through Reflex.

## Council

Seats: Claude Opus 5.5, Muse Spark 1.3 (contributor), Grok 4.7. Mistral Large 4 was invited and could not be reached
(no balance on that provider) in either round.

| Round | Draft | Opus 5.5 | Muse Spark 1.3 | Grok 4.7 |
| --- | --- | --- | --- | --- |
| 1 | the Stream | reject | reject | reject |
| 2 | second draft | yes, with edits | yes, with edits | no |
| 3 | third draft | sign | sign | sign |

Round 2 edits, all taken into this draft: the window is five deeds and the Day begins at local midnight (Opus); the
late answer's side effects on habituation and the Diary, and a base rate for plans (Opus); Diary and DM visibility
stated as they are, and the filter described as a prompt filter (Muse); thoughts only in the voicing message after the
pick, never for a refused pick, the desk's reach off shift stated correctly, the existing record reused, and an
uncited figure dropped (Grok).

Round 3 raised no blockers. Three notes from Opus were added afterwards and change no decision: the voice needs its
own rendering of the deeds, a late answer shows as the asker's latest line in the feed, and a plan displaces an older
want.
