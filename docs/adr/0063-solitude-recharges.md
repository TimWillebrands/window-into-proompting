# Solitude recharges the Social battery

> Status: proposed. Built and tested, not deployed. Builds on ADR 0021 (lived self, point 2) and 0060 (the lived
> body). Vocabulary in `CONTEXT.md`.

The Social battery is the oldest Reserve, and the only one that didn't care where you were. Talking drained it and it
came back at a flat 0.25 an hour, alone at home or pressed into a full café alike. So being peopled out had nothing to
do with Places: nobody ever needed to be alone, and a Persona could recover from a long talk on the sofa next to the
person they'd been talking to. Company and solitude are a tension (Vervaeke's opponent processing), and only one
side of it was in the body.

## Decision

- **The rate follows who is around**, as last seen on a Life turn. Alone in the Place (no other Body there that is
  awake and not walking through): 0.25 an hour, as before. Others in the Place but nobody at your Spot, or away at a
  Locale: 0.12. Someone at your Spot: 0.04.
- **The fact goes in the state.** `SelfState.BatteryRate` (default 0.25). `LiveBodyAsync(away, company)` first banks
  the battery up to now at the old rate, then stores the new one. Life works `company` out from the Places snapshot
  it already has.
- **Only while the body is lived.** The stored rate counts for time up to `LivedAt + Bio.FeltFor` (2 hours); after
  that it recharges at 0.25 again. A paused clock, or a Party that only uses Rooms, has no Life turns, and there the
  battery behaves exactly as it did.
- **The rest bonus is solitude's.** A rest, diary or nap turn still gives +0.25 when alone, and +0.1 in company.
- **Home pulls when you're peopled out.** Under 0.25 (the "peopled out" line), away from home and with company where
  you are, "Head home" reads "(you're peopled out)" with one more pull. Not Distress: it meets nothing and doesn't
  narrow the menu. Dead on your feet and not well win; they don't stack.
- Unchanged: the drain per spoken line, the reset on a new day, the presence lines and their thresholds.

## Considered

- **A separate "solitude" Reserve.** A second number for the same thing. The battery already is what company costs;
  it only lacked the counter-pull.
- **Draining just by being near people.** It would make an evening with friends cost the same as a day of small talk,
  and punish the café for being a café. Talking stays the cost; company only slows the return.
- **A rate per Place** (a quiet library, a loud bar). Not without designers saying it. Who's there is the fact we
  have.

## Consequences

- Low-Chattiness Personas feel it most: a spoken line costs them up to 0.1, so beside someone it takes two and a half
  hours to win one back, against 24 minutes alone. Expect them to drift home, or to a quiet corner, after a long
  talk. Chatty ones (0.04 a line) win one back in an hour even beside someone.
- Personas who live together are alone at home only while the other is out or asleep; otherwise their battery comes
  back at 0.12 or 0.04 there. The new-day reset still fills it.
- The battery scales how inclined a Persona is to answer (`SelfMath.Inclination`), so a battery that stays low in
  company keeps them quieter there, which is the point.
- No new LLM call, no new HTTP surface.

## Not yet

- Wanting company back: loneliness as a pull after hours alone, the other half of the tension.
- Ties shaping it: a close friend at your Spot costing less than a stranger.
- A Persona's own temperament (introvert, extravert) setting the rates rather than only Chattiness setting the drain.
