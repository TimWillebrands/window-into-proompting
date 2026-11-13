# Promises, settled plans and Happenings at real Places

> Status: accepted; restating and lapsing amended by ADR 0055. Builds on ADR 0018 (Initiative, Happenings), 0019 (weigh ⇄ voice), 0021 #26 (settled plans),
> #29 (words become deeds), #30 (Settling), 0026 (Responsibilities, wanting needs a handle) and 0031 (Reflex).
> Vocabulary in `CONTEXT.md`.

A production night showed three failures with one root: what was said, agreed or seen never became something a
persona *knew*, so nothing pulled on what they did next.

- **Talk didn't become deeds.** Justin told Anne "ik kom er zo aan" twice, promised "the Debussy story" three times
  and never came or told it. He told Pieter "Give me tonight", "I'll write properly tomorrow", "Ik laat het niet
  liggen" about his father's work, and never answered. Henkie said he'd come once he found his socks.
- **Agreed things didn't count as settled.** Thursday supper was re-confirmed five times and asked about again the next
  morning; Saturday offerings four times; a bread-and-wind joke ran six Board replies.
- **The world didn't enter anyone's mind.** Six Narrator Happenings on invented streets; not one was mentioned.

## Why

- `FollowThrough` asked one question, "coming over now?" (p ≥ 0.6, in English over Dutch lines), once per line. Every
  other commitment was never registered, and a conditional "coming" (0.48) was lost. ADR 0026 listed FollowThrough
  promises as a Responsibility source, but they were "kept straight away", so nothing ever sat as owed.
- Reflex's `Intentions.Renew` replaced what you meant to answer with the latest line, so Justin's get-back answered
  "Fine. Take the night." instead of the question, and speaking (a deferral) then settled it.
- The thermostat reads one Room's last ten lines. Messenger's *Follow up* option never consulted it (Anne's morning
  re-ask), the Board had none, and nothing carried an agreement across channels or overnight.
- Happenings had no Place, and reached only Life as a block identical for everyone with no Affordance. ADR 0026 had
  already found that a want with no handle moved nobody; a Happening is the same.

## Decision

1. **A Promise is a Desire of the promiser's, source `Promise`.** Keeping your word is a being-desire about someone
   (ADR 0026), so it reuses Desire's storage, handle and lifecycle instead of a parallel store, as a Mission
   did (ADR 0037). `FollowThrough` stays the one reader of a persona's line (DMs, and Rooms toward whoever they were
   answering), with one System One call: *promised?*, *what* (come over or do), *when* (soon, tomorrow, a set day),
   *coming now?* (DMs, the walk as before), a set-day meet-up's *day*, *part of day* and *whose Home*, and per open
   promise *kept?* and *called off?*. Desire gains only what it lacked, generically: `DueAt` (presses from) and
   `Until` (lapses at), plus `Coming` and the restate count `Again`. Kept is `Fulfilled`, called off `Abandoned`,
   lapsed `Faded`. One open promise per person and kind; saying it again restates it and counts.
2. **One source of truth, known on both sides.** The promiser sees it in `# What you owe` (Speaking, Decision, Life),
   and not again in "What you want lately": promises are owed, not wanted, and stay out of the two-want cap. The
   holder's "Denise told you …" in *Who these people are to you* is read off the promiser's Desires; Ties store none.
   Due ones use the handle desires already have: `DesireRules.People` puts a due promise first, toward that person,
   their chat or (for a come-over) where they are, past the two-want cap; the option pulls one more and reads "(you
   said you would: …)". A come-over due outlasts Settling. Being there keeps a come-over; a due one left to lapse
   cools the holder's Tie (`ResponsibilityRules.NoShow`).
3. **A meet-up on a set day is a Gathering.** When the read is sure of the day and that it's at one of their Homes,
   the Calendar gets it (hosted by that Home's resident, both going), and the World clock walks them there; an
   existing one both go to within a day is the same plan. Otherwise it stays a Promise known for a set day, which never
   presses and lapses after 8 days.
4. **The thermostat reads every path that can re-ask.** Messenger's follow-up is dropped when the chat ended settled
   (unless you owe them), and Board threads get the same read: fizzling or re-confirming replies pull 2 less.
5. **Happenings land at real Places.** The Narrator picks a public Place, likelier where people are, and must set it
   there. The Place carries it for 45 minutes ("Just now here: …" in its Situation, so every prompt of whoever is there
   sees it), witnesses are notified, and only the Place's District remembers it. Life reads each one from where the
   persona is (here, nearby, across town). Witnesses get it as material on the talk and post options until they've
   used that channel since; people nearby get "go see what's going on".
6. **Reflex, minimally:** a follow-up keeps the gist you meant to answer, and a verdict weighed past `ReflexGate`'s
   deadline no longer acts (the reply already went ahead).

## Counter-pulls

Every promise is a self-reinforcing pull; each has its counter. Restating counts and shows ("said again 3 times"),
so a defer loop becomes visible to the persona as a fact, not a ban. Promises lapse (soon 24 h, tomorrow 36 h after
midnight, a set day 8 days). A Happening's hook ends once the witness has used the channel since, and it is
remembered only around its Place, so it can't spread through every diary.

## Consequences

- One System One call per persona line in Rooms with an addressee (DMs already had one), with a few more questions.
- Thresholds are unchanged: `Coming.Threshold` 0.6, `PromiseRules.Made`/`Settled` 0.6, thermostat 0.6/0.75. They
  need a bench probe against real lines before tuning.
- Settled promises are spent: only the latest ten stay in the Desires grain, since unlike a fulfilled want they're no
  candidate for Promotion.
- Not done: Happenings as Reflex Percepts.
