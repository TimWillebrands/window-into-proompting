# Arena fit: where you are shapes how you are

> Status: accepted. Builds on ADR 0018 (Initiative), 0020 (Places), 0021 (lived self), 0047 (Lack). Vocabulary in
> `CONTEXT.md`.

Personas didn't live anywhere in particular. The Situation named a spot, the people and what was broken, but not the
light, the air or what filled the room, and nothing about a Place ever moved a mood. A bare flat and a lamp-lit
record shop were the same to everyone. The body didn't count as an event either: Justin walked over to Medevortius's
tower and, standing next to him, insisted "I didn't come, I'm still in my chair". His last words about himself were the
DM he sent from home, and nothing said he'd moved since.

In Vervaeke's agent–arena terms, meaning is in the fit between an agent and its arena, not in either one. So a room
has no score: the same bare room is a monk's peace and a craftsman's irritation. A room that offers nothing is boring,
and boredom is a pull to look elsewhere (Danckert & Eastwood), not a slide into gloom.

## Decision

- **The facts go in the Situation.** One feel phrase from what the Place already holds: its light and ambience from
  the scene, and up to five things in it, "bare" when there are three or fewer. To-dos and decor keep their own lines.
  Your own Home also says what it is ("a small apartment"), and "Nobody else is home" replaces "here".
- **Getting somewhere is in it too.** A Body remembers the Place it last walked away from. For 30 minutes the
  Situation says "You came over from your place 3 min ago" or "You got home from the Café just now". Moving between
  spots in a Place doesn't count.
- **The fit rides Initiative's call**, like the Lack. A three-way choice beside the menu: does this place, as it is,
  lift them, do nothing, or wear on them, being who they are. Asked on the first look after an arrival, or when the
  Place has changed (things added or removed, something gone wrong), never on a follow-up after a Look.
- **Felt as surprise, not level.** The answer shifts valence by `0.5 + (p − 0.5) × novelty`. Novelty is 1 for a place
  they haven't been to in the last week and 1/(1+days) after that, so home after a week of daily visits counts for
  an eighth. A known place that changed counts at least half. The Place is the valence source when the shift is big
  enough to name.
- **Restlessness follows the fit.** Getting out pulled after a fixed 45 minutes. Now it's
  `45 min × 2^((p − 0.5) × 2)`: about 22 minutes where the place wears on you, 90 where it lifts you. Still one more
  pull on the menu, still sampled.

## Consequences

- One more question in a System One call that already runs, once per arrival. No new LLM call.
- The bench probe `Places_ArenaFit` puts two opposite tastes in a bare, a furnished and a neglected room. The judge
  ranked them furnished > bare > neglected for one and bare > furnished > neglected for the other (13 Nov).
- The judge is polar (0.01–0.98), so a first visit can move mood as far as a conversation does (±0.4). Valence's
  3-hour half-life and the novelty damper keep it from compounding.
- What was felt is kept per Life activation, like the Lack baseline. A deploy forgets it, so each Persona takes in
  where they are once more. Home is damped to near nothing, so that costs little.
- Not yet: attachment to a Place (the "third place"), fed by good Moments there, as an edge on the Persona's side. And
  the fixed +0.6 lift for a Take could fold into this appraisal.
