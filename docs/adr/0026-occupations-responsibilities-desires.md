# Occupations, responsibilities and desires

> Status: accepted. Builds on ADR 0018 (Initiative), 0020 (Places), 0021 (Lived self), 0024 (economy) and
> 0016 (Stances, Promotion). Vocabulary in `CONTEXT.md` (§ Occupation, responsibilities and desires).

Personas have a day, a battery, a wallet and a body, but nothing they *have* to do and nothing they *want*.
Every Initiative turn starts from zero: which Affordance pulls hardest right now? Income is a flat daily
pay (ADR 0024, "no jobs yet"). Life threads (ADR 0021 #20) and the preoccupation are generated fresh each
morning and belong to no one. Gatherings and FollowThrough are the only things anyone holds a persona to.
So nobody is late, nobody saves up, nobody wants what their friend has.

## Basis

The pulls are not a new faculty. They are the slow landscape that Relevance Realization already works over.

- **Agent–arena (Vervaeke).** An arena gives you a role: you are the barista *at the Café*, the host *of
  your Home*. Responsibilities come from roles in Places, not from character traits.
- **Four kinds of knowing.** An Occupation fills the open Procedural slot (craft, work vocabulary). Roles
  and desires are Participatory: who you are relative to your world.
- **Having ⇄ being (Fromm, as Vervaeke uses it).** Having-desires want a thing, money, someone's attention.
  Being-desires want to become someone: better at a craft, back on good terms. Being-desires that come true
  are candidates for Promotion (Callard's *aspiration*: valuing something before you fully can).
- **Centre and margin (Pageau).** A Place has a purpose, a centre (owner, staff) and a margin (newcomers).
  Hosts owe guests. Gatherings are feast time against ordinary time.
- **Mimetic desire (Girard), capped.** People want what people near them want. That makes drama, and the
  same loop converged the world's register in ADR 0021, so it is rate-limited hard.
- Freud contributes only the tank mechanic (already used by the battery). Jung is left out, because
  archetypes pull prompts back into the literary register ADR 0021 removed.

**Rule: facts, not traits.** Prompts get "shift at the Café 9–17, you're 10 min late", never "you are
dutiful". The structure (roles, purposes, holders) lives in code and data. Priority foregrounds an option and
never forces it.

"Drive" stays banned: it reads as Urge and collides with **Driver**.

## Decision

1. **Place purpose.** Every Place gets a one-line `Purpose` ("where people get coffee and linger"). Authored
   for seeded Places, written by the designer for Made Places, and used in Situation and the Place's Life
   perception.
2. **Roles in a Place.** A Participant's standing in a Place, derived and not stored:
   - **Owner**: `OwnerId` or `MadeBy`.
   - **Staff**: has an Occupation there.
   - **Regular**: spent time there on 3+ of the last 7 days.
   - **Newcomer**: everyone else.

   Situation names the role in plain words ("you work here", "you're a regular here"). Staff and Owners
   get host Affordances (greet someone who just came in, tidy up). Spots can be marked `Staff` (behind
   the counter).
3. **Occupation** (Participant scope, Nurture). Job title, workplace, weekly hours, pay per shift.
   - **Seeded** the way Means is: the first time it is needed, it is read from the lived sheet (System One
     choice over the Party's Places plus "elsewhere"). Arrival drafts can include it. Authors can edit it.
   - **Onscreen** if the workplace is a Place in this Party. At shift start the Body walks there, like
     bedtime and Gatherings do, and stands at a Staff spot with Activity "working". While on shift it is
     half-available: it can chat in Place talk, and other Life options are damped. Customers meet the
     worker.
   - **Offscreen** otherwise. The shift becomes fixed beats in the Day, and Messenger shows Away. Where the
     Body waits is an open question (below).
   - **Pay** goes to the Wallet per shift worked, with a ledger line "pay from the Café". Means stays as
     the fallback for people with no Occupation. A missed shift pays nothing.
4. **Responsibility**: something someone holds you to. It always has a **holder** (a Participant, a Place,
   or an offscreen party such as "the landlord") and a **due** time. Sources:
   - Occupation shifts (holder: the workplace);
   - host duties toward guests in your Home;
   - FollowThrough promises and Gathering RSVPs (holder: the other person);
   - Life threads that name a holder.

   They render as a `# What you owe` block in Right now: at most 3 lines, the soonest due first. Keeping one
   shifts Tie warmth toward the holder. Neglecting one costs something visible: lost pay, friction with the
   holder, a Happening ("the Café was short-staffed this morning").
5. **Desire**: something a Persona wants. It has a kind (`having` / `being`), free text, and an optional
   target (a catalog item, a Participant, a Concept).
   - **Intrinsic Desires** are on the Persona: authored, or distilled from the lived sheet (1–2).
   - **Acquired Desires** are on the Participant and formed in play (see 6). Promotion works as it does for
     Stances.
   - Rendered as "What you want lately", at most 2 lines.
   - Having-desires steer the Wardrobe (name the item, save up rather than spend). Being-desires reach
     Initiative through one System One noul per turn: "doing this moves X toward what they want". It runs
     only for the top desire, to cap cost.
   - Consolidation marks desires fulfilled, faded (14 days untouched) or abandoned.
6. **Mimesis.** During Moment appraisal (ADR 0021 #16), System One asks: "X came away wanting something Y
   has or wants". At p ≥ 0.7, X gains an Acquired Desire whose source is Y. Caps keep the spice small:
   - only through a Tie of 10+ exchanges (you imitate people you know);
   - at most one mimetic desire per persona at a time, and one new one per persona per 3 days;
   - **Rivalry** happens when two people hold the same exclusive having-desire (one jukebox slot, one
     person's attention). It shows as Tie friction, and only one rivalry is live per World at a time.
7. **Initiative pulls.** New rows for ADR 0018's table:

   | Pull | ⇄ | Counter-pull |
   | --- | --- | --- |
   | want (desire) | ⇄ | should (responsibility) |
   | having | ⇄ | being |
   | now | ⇄ | later (the shift starts in 20 min) |
   | your own Place | ⇄ | someone else's |

## Slices

1. Place purpose, roles in Situation, and Occupation with onscreen and offscreen shifts and shift pay.
2. Responsibilities: the `# What you owe` block, keep and neglect consequences.
3. Intrinsic Desires: distilled from sheets, shown in Right now, and the Wardrobe and Initiative pulls.
4. Acquired Desires, mimesis and rivalry.
5. Later: Place reputation, consolidated from Events at its `place_id` (what the Place has become known
   for), and Made Places opened as workplaces ("open a bakery").

## Settled during build

- **A job is Party-scoped**: it happens in time, so it lives on the Participant (`OccupationGrain`, key `party:participant`). The Persona carries no trade.
- **Offscreen workers are away at a Locale** (ADR 0027): `LocaleAsync` resolves the workplace by name. At shift start `GoAwayAsync` sends the Body there, and `ReturnAsync` brings it back at the end. A workplace named like a drawn Place is onscreen and goes to its Staff spot. Opening and Closing keep the id (ADR 0027). The stored job follows the workplace's current kind and name at every shift start and end (`WorkRules.Sync`), and prompts and the API sync their copies on every read. An office Opened mid-shift therefore ends that shift onscreen, and one that's Closed sends its workers away again. Each offscreen shift start notes `Anchor` footfall on the Locale (`IGrowthGrain.NoteAsync`, at most once a day), so coworkers at one office can get it Opened.
- **The clock owns time**: each tick it steps every LLM participant's shift (start, end or miss). A shift can only be missed if the running clock saw it in progress; that rules out a paused world or a job set later that day. Shifts don't wrap past midnight in v1.
- **Pay**: per hour, at a rate where five 8-hour shifts equal a week of the old flat pay. Employed Wallets get no daily payday.
- **Responsibilities v1** (`Responsibilities`, read fresh on every prompt, never stored). Sources:
  - the shift (always the first line);
  - Gatherings in the next 24 h that you RSVP'd to or are hosting (holder: the host);
  - guests in your Home while you're there.

  FollowThrough promises are kept straight away, so they never sit as owed. Life threads with a holder come
  later. Consequences:
  - a missed shift moves the mood toward worse (p 0.25), and being more than 15 min late moves it a little (0.4);
  - when a Gathering at a Place starts, the host warms to persona attendees who came (0.65). No-shows cool both
    ways (0.3).
- **Desires live in `backend/World/Desires/`.**
  - `IntrinsicDesiresGrain` (key: persona id) distils one having-desire and one being-desire from the lived sheet, in the background. Authoring them stops the sheet from overriding.
  - `DesiresGrain` (key: party:participant) holds Acquired Desires, at most 3 active; the oldest fades. It also holds per-Party marks on intrinsic ones, so getting what you wanted in one Party leaves the Persona wanting it elsewhere.
  - Acquired Desires fade after 14 days untouched. Intrinsic ones never fade.
- **Wanting steers doing, not talking.** "# What you want lately" goes into the Initiative prompt only. Both the choice judge and the voicing see it there, so no extra noul per turn is needed. That replaces decision 5's per-option noul, which was more machinery for the same pull. Speaking prompts never see desires, so personas don't perform them (ADR 0021).
- **Having meets the Wardrobe.** A having-desire that names a catalog item gets its own buy option (priority 2) when you can afford it. Until then, buying anything else pulls 1 less ("saving up"). Buying the item, or being given it, fulfils the desire.
- **Being is reviewed in rest.** Writing the Diary runs `DesireReview` at most every 12 h. It recalls memories against the active desires and asks System One "got it" (≥ 0.7: fulfilled) and "closer" (≥ 0.6: touched).
- **Mimesis rides the Moment appraisal's existing System One call.** It adds one noul per pair that clears the caps, and the LLM only voices a hit. Caps:
  - a Tie of 10+ exchanges;
  - no active mimetic desire already;
  - 3 days between catches per persona (`MimesisGrain`, key Party);
  - only persona → persona.

  A rivalry starts when the caught desire targets the same person's attention as one the source holds. It means friction both ways (0.35), with one live rivalry per World for 3 days.
- **Roles** (`Roles`, derived each prompt): Owner (home owner or `MadeBy`), Staff (job there), Regular (3+ of the last 7 days, from `GetVisitDaysAsync`), Newcomer. "# Where you stand" renders in Life and Speaking, and stays silent at home and for newcomers. Staff on shift get "Greet X, who just came in" (priority 2) for anyone who arrived in the last 10 minutes; it speaks in Place talk.
- **Life on shift**: the Work App offers get on with work, head back, or leave early (paid for the time worked). Every other App pulls 1 less while on shift.

## Consequences

- Day generation gets fixed shift beats as input, so availability has structure instead of noise.
- Coworkers share a Place every day, so Ties form between people who'd never pick each other.
- Means as flat pay shrinks to the fallback case.
- Each Initiative turn with a being-desire costs one extra noul (System One only; skipped when no judge
  is enabled).
- Mimesis can reconverge the world. Track mimetic-desire count and rivalry rate in the lifelikeness bench
  next to stale words.
