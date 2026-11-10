# Shares: the pool is split by persona, and a persona stays put

> Status: accepted. Amends ADR 0032 (how the pool is shared). Builds on ADR 0040. Vocabulary in `CONTEXT.md`.

Equal-priority providers shared the pool by load: whichever was least busy took the next call. An author could not say
"about half on Haiku, half on Nemotron", and one Persona's calls hopped between models. Every hop is a prompt-cache miss
on a prefix the other model had warm, and a voice that changes model mid-conversation.

## Decision

1. **A provider has a Share.** A whole number, 100 unless set, weighed against the other pool providers at its Priority.
   50 and 50 is half each; so is 1 and 1. Control Panel shows it as a percentage per tier.
2. **A Share splits Personas, not calls.** The router ranks a tier's providers per Persona with weighted rendezvous
   hashing over (Persona, provider), so a Persona keeps one provider. The hash leaves the tier out: a Persona lands on
   the same model for every tier that model handles.
3. **The split is roughly the Shares, never exactly.** It is a hash over a few dozen Personas and it counts Personas,
   not calls or tokens. Cache use and one voice matter more than the ratio, so nothing rebalances.
4. **Change moves as few Personas as possible.** A new provider or a changed Share only takes Personas for the provider
   that grew. A Benched or Offline provider's Personas go to their next-ranked one, and come back when it does.
5. **Priority still goes first, health before that.** Healthy before Benched, lower Priority before higher, then the
   Persona's rank. Shares only matter between providers at one Priority.
6. **Work nobody owns goes to the biggest Share**, first listed on a tie: the narrator, memory extraction, Places,
   System One about a group. One model for it, and the author picks which by making it the biggest.
7. **The router no longer asks providers how busy they are.** Routing is in-memory and makes no grain calls.
8. **Decisions take turns instead.** A System One call has no prompt cache and no voice, so nothing is gained by a
   Persona staying put. Each call takes the next provider of the best step, in proportion to Share (smooth weighted
   round-robin), whoever it is about. There the Share is a part of the calls, and the ratio is exact.
9. **A Persona Route is a pin on top of the split.** Unchanged from ADR 0032, and one screen (Control Panel → Language
   Models) shows the providers, the split, and where every Persona's work goes now, pins included.

## Considered

- **Weighted random per call:** the exact ratio, and every Persona on every model. Loses the cache and the voice.
- **Exact quotas (sort Personas, cut by cumulative Share):** hits the ratio, but one new Persona shifts the ranks and
  moves several others to another model.
- **A percentage per tier per provider:** lets Voice be 70/30 while Thoughts is 50/50, at the price of a Persona
  speaking through one model and thinking through another. A second provider entry does that when it is wanted.
- **Keeping load as the tie-break:** two equal Shares would flip with every busy moment.

## Cost

A slow provider keeps its Personas: nothing spills to a quieter one unless it is Benched or Offline. With few Personas
the split can be lopsided (7 and 3 for 50/50), which is why the screen shows the count beside the percentage.

One Persona's decisions now come from every decision model in turn. Their numbers only compare within one judge, so
each per-judge baseline (ADR 0047) fills more slowly.
