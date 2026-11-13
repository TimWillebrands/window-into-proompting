# Own tempo and second thoughts

> Status: accepted; Happening kinds amended by ADR 0055. Builds on ADR 0018 (Initiative), 0019 (System One), 0021 (lived self) and 0043 (Happenings).
> Amends ADR 0047 (how a Lack is felt). Vocabulary in `CONTEXT.md`.

One day of production (9–10 Nov) showed Initiative missing the two things that keep relevance realization from
narrowing onto itself: a tempo that is the Persona's own, and a "no" that changes what stands out next.

- Pieter was the only one awake after midnight and got 29 turns in an hour. He wrote in his diary 28 times in 45
  minutes, the same loan in the same words. Anne got 36 turns in an hour lying next to a sleeping Justin.
- About 90 of 1,100 picks were thought better of (ADR 0034), sensibly and in character. Nothing remembered it: Eiko
  was offered the tram and refused it 8 times in 2.5 hours, Mara was offered a paid session with her own partner 4
  times. Pull only orders the menu, and the menu holds everything, so a refusal changed nothing System One saw.
- 29 high-fives in a day. Lena refused one as "a performance of closeness".
- 48 Happenings, nearly all an object on a surface (a coffee ring, napkins, a moth), ten of them at night with nobody
  up. People posted about them because nothing else was new, not because they mattered.

## Decision

1. **Tempo is your own.** The clock's draw includes the sleepers' share, and nobody takes that turn. Someone awake
   acts about six times an hour whether one or eleven are up. A turn asked for by hand still always lands.
2. **The veto teaches.** An Activity carries the key of the Affordance it came from (app, verb, target), also when it
   was thought better of. What is left of a refused option's pull starts at a tenth and comes back over an hour,
   slowly at first; each further refusal doubles the hour, up to four. That share scales System One's probabilities
   before the draw, and costs the option its seat on a full menu. Damped, never removed: people change their minds.
3. **Written down is put down.** The diary opens for a first entry, after a couple of things lived, or after six
   hours. Resting, looking and the writing itself are not things lived.
4. **A gesture answers a moment.** A high-five is offered when one of the two just got there or they just spoke. A
   passing gesture is not offered again with the same person within the hour. Holding hands is neither.
5. **Nobody hires their own partner.** A Practice is still listed, the booking is not offered.
6. **A Happening has a kind and a stake.** The clock picks the kind (weather, a stranger, something breaks, something
   left, an animal, word from the neighbourhood, something outside, an offer), never one of the last four. The brief
   asks for something to deal with, take up or do without, says the day part, and whether it happens in front of
   people or waits to be found. With everybody asleep or away from their screen, the Narrator waits.

## Considered

- **A minimum gap per Persona.** Stops the loop, but it is a second rule beside the weights. The sleepers' share is
  the same draw with the missing people put back.
- **Every pull term as a prior on the draw** (habituation, settling, shift damping). They have the same gap: they only
  order the menu. Held back because it changes how talk in a Place flows; refusals are the unambiguous case.
- **Dropping a refused option from the menu.** That forces. Priority foregrounds, it never forces.
- **Remembering refusals on the Life grain.** Gone at every deploy, and there were nine that evening.
- **Letting night Happenings run.** One box found in the morning made a good thread, the other nine made nothing.

## Cost

- `WorldActivity.Affordance` is on the wire and in the clock's state.
- A promise thought better of is damped like anything else, and pulls again within the hour.
- The key is the target Place, so the tram to a different busy Place is a different option.
- The kinds last used live on the clock's activation: a deploy may repeat one.
