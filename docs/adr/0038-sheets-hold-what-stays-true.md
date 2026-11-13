# Sheets hold what stays true

> Status: accepted. Builds on ADR 0021 (Lived self), 0022 (Arrival), 0026 (Occupations, Desires) and 0035 (Relations).

The persona generator dates from when the sheet did everything. The runtime now owns most of that: Day writes
today, Threads carry what's ongoing, Desires hold wants, Occupation and Means hold work and money, memory holds what
happened, Relations and Ties hold who people are to each other. Anything time-bound left in a sheet goes stale and
then contradicts live state. Every distilled sheet had a frozen `## Going on lately` ("avoiding the call for nine
days", for good).

Generated people also came out alike: guarded, wry, solitary, with details lifted from the prompt's own examples.
That starves the thing the World is for, strangers becoming something to each other.

## Decision

1. **A sheet holds what stays true for years.** History, habits, lasting lacks. No open threads, no "lately". Age,
   work, money and household are stated flatly, because the World reads them once and takes over.
2. **`## Going on lately` is gone.** Threads are invented from the sheet on day one and carried by Day.
3. **`## With other people` is new.** What someone does toward strangers, acquaintances and friends, plus concrete
   hooks that give a stranger a way in: things they know, fix, lend, cook or keep track of. Behaviour, not adjectives.
4. **The brief is built in code** (`PersonaBrief`): the seed, a random Draw, and who and what is already here.
   Independent calls can't see each other's dice, so asking for variety in the prompt doesn't work. The seed wins.
5. **`## Card` is read, not kept.** It carries the bio and the dials, and is stripped from the sheet. Generated
   Personas no longer all get 0.5 / 0.5.
6. **The bio is plain third person with looks in it.** A Figure is drawn from the bio alone, and memory and Reflex
   read it as fact, so it can't be a character card.

Distillation follows the same rules, so legacy sheets and generated ones end up in one shape.

## Consequences

- Bumping `LivedSheets.Version` redoes every distilled sheet and, through the sheet hash, every Intrinsic Desire
  nobody authored. It went to 6 with the first Move (ADR 0056), so residents of the fresh World start from fresh sheets.
- Distillation only sees the authored sheet, so a job set later in a Party (Pieter is a trader now, his sheet says
  startup) stays contradicted. No prompt fixes that.
- Arrival (ADR 0022) is still unbuilt. A shared past between new people is a Relation's Story, not memory.
- The city is hard-coded in `PersonaBrief` and in the distill prompt.
