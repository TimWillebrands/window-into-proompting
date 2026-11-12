# Reflex: a Persona notices things, and decides when to deal with them

> Status: accepted. Builds on ADR 0015 (recall as RR), ADR 0018 (Initiative), ADR 0019 (System One), ADR 0020 (one body, one place), ADR 0021 (lived self).

Personas had two tempos and nothing between them. Life runs on a clock, a turn every few minutes, with nobody asking anything. The response pipeline runs on every message, at once, with a gate made of a regex, a question mark and a random number. So a DM got an answer in seconds from someone whose Messenger said Away, and a person walking up to a persona got nothing until a Life turn happened to land, which at 45 s per tick across a cast is minutes. There was no "later" and no "not now". System One weighed plenty, but almost all of it after the fact.

The missing faculty is the one that sits between a stimulus and a response: did this reach me, what do I do about it, and when.

## Decision

Add **Reflex**, a stimulus-driven instance of Relevance Realization at the seconds timescale, Initiative's twin. Every modality reaches a Persona through one seam, `IPersonaReflexGrain.PerceiveAsync(Percept)`, and goes through the same stages. Each stage is a piece of cognitive science that earned its place by changing behaviour.

| Stage | Model | What it does here |
| --- | --- | --- |
| Focus | Global workspace (Baars) | A conversation you're in holds the workspace: its next line skips appraisal. The grain is not reentrant, so one thing is processed at a time (the psychological refractory period, Pashler). |
| Load | Capacity (Kahneman), multiple resources (Wickens) | Availability, shift, being out, what the Body is doing, a conversation elsewhere, combined as noisy-or. Load is per channel: a phone in hand costs the room around you, not the next message. |
| Filter | Attenuation (Treisman), perceptual load (Lavie), signal detection | Strength against a bar that rises with load, with noise near the bar. Own name adds a lot (Moray). |
| Habituation | Orienting response (Sokolov) | Repeats from one source fade and recover. What carries signal does not: being addressed nags. |
| Surprise | Predictive processing (Friston, Clark) | Arrivals scale by how unexpected they are: a colleague seen ten minutes ago is not news, a friend after three days is. |
| Weighing | Appraisal (Scherer), dual process | System One gets state and a `choice` over what fits the channel, and the pick is sampled, as in Initiative. |
| Conflict | Conflict monitoring (Botvinick), default-interventionist (Evans) | No clear winner means System Two decides: the Decision phase runs even where auto-respond would have. |
| Interaction | Civil inattention and access rituals (Goffman), negotiated interruption (McFarlane) | Options are the ones the setting offers: answer, a quick line, finish first, leave it; greet, nod, carry on, move away. |
| Timing | Online chronemics, heavy-tailed response times (Barabási) | Reading takes time, near nothing face to face. A newer line in the same chat takes over the read, so a burst gets one reply. Put-off times are lognormal. |
| Later | Prospective memory (Einstein, McDaniel), interruption at breakpoints (Iqbal, Bailey), Zeigarnik | An Intention comes back when due, waits for a breakpoint if busy, stays in their prompts until settled, and speaking in that chat settles it. |

Reflex only runs in a running World with an enabled judge and no scenario. A paused World, a scenario Room or a dead judge leaves the old path exactly as it was. So does a judge past its 1.2 s budget: Reflex fails towards replying.

> Amended by [ADR 0044](0044-system-one-required.md): the judge is required, so only a paused World or a scenario Room leaves the old path.

## A human is never left on read

Personas may leave each other unanswered, and an Intention nobody is owed can slip their mind after six hours. A human is different. `leave` has no weight for them, and what's put off is owed within 12 minutes, busy or not. Messenger says so: "saw this and will get back to you". Silence from a character can't be told apart from a broken app, and only one of those is lifelike.

Two rules from earlier ADRs stay as they were: a sleeping Body only wakes for a human (ADR 0020), and the DM round cap still ends AI ping-pong.

> Amended (#129). Production still left humans unanswered: 6 of 91 bursts got no line within 10 minutes. Two paths did it.

**`soon` sends the quick line too.** Toward a human, `soon` used to put the answer off by up to 9 minutes and send nothing. Now it sends the quick line, as `brief` does, and keeps the Intention for the proper answer. Between Personas `soon` is unchanged. A follow-up while it's held still only renews the Intention: the human has had their line.

**The Room settles a human's line.** The cold-open floor keeps Reflex off a human's first line in a quiet Place, but the Decision phase could still decline it, and every listener's turn is its own root, so nobody knew the others had declined too. The Room sees every turn end, so it settles there:

- A line is owed when a human typed it: one of `AccessRules.Humans` and not on auto. A Persona's line, or a Player on auto's, is never owed.
- Its listeners are the LLM-driven ones who heard it (Earshot). It settles when each of them has ended their turn on it: spoke, declined, skipped, or held by Reflex.
- Anything said after it that the human heard answers it, and so does a Reflex hold (`holding:`): its quick line is on the way.
- Settled with nothing said and nobody's turn still open, the Room asks the listener who wanted it most (highest urge, then Chattiness) to answer it with decline off. That turn skips the pre-gate and Reflex. The Decision phase still runs for the gut reaction and the memory, but its "no" no longer stands. In a DM this means: no decline on a human's line.
- A line is handed out once. The bookkeeping lives on the Room's activation, not in its journal, so a restart can drop a rerun but never doubles one. A failed turn (a stop pressed, a provider down) stops it: the rerun answers declines, it doesn't retry failures.
- The rerun is a turn like any other, so the stop-signal race applies: a newer line can cut it off, and that line is owed in its own right.

## The code does not lean on what System One can see

We first shaped System One's distribution with pulls for load, strain and battery. The calibration probe showed that was wrong. Those facts are already in the state and Jev reads them well: a casual DM in a client meeting gets `soon` 0.80 and `now` 0, and a hospital message in that same meeting lifts `now` to 0.19. The pulls counted load twice and crushed that to 0.04. An emergency looked like a meme.

So the landscape keeps only what the state can't carry: Impulsivity and Chattiness, neutral at 0.5, and the norm about humans. Earlier notes that decision models compress p hold for `noul`, not for these `choice` questions.

## Graph on the reflex path

Grain state answers most of what Reflex needs. What memory holds about a person is recalled with a 300 ms budget and cached for ten minutes per person, so a slow graph costs context, never a reply.

## Consequences

- One judge call per noticed Percept per listener, about 200 to 550 ms. Filtered, mid-conversation and held Percepts cost none. A System One "no" also saves the Decision call it replaces.
- Arrivals can now cascade, so spoken Reflex acts spend the hourly budget, except toward a human.
- Reminders live on the World clock and survive restarts; habituation and focus are in memory and don't need to.
- `reflex: …` reasons land in the thought log, live now, as a Room frame so a DM's stays private.

Not modelled: arousal as its own variable, learned expectations about where someone usually is, and topic relevance in the filter (a line about your own speciality is as quiet as any other until it's weighed). Each would need to show a behaviour it buys.
