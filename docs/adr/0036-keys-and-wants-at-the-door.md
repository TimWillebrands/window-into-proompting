# Keys: wanting one forms at the door

> Status: accepted. Builds on ADR 0026 (Desires, Mimesis), 0035 (Relations, private Places), 0019 (System One).

A closed Place left people two ways in: be invited, or knock while the host is there. Nobody could work out
something better between themselves, and nothing would have made them try: an Acquired Desire only ever came from
Mimesis or an author. Experience never turned into wanting.

## Decision

**The door gets felt.** A **Doorstep** is a time someone had to deal with a closed door: knocked, needed inviting,
found it locked. People close to the host see it locked and can go by anyway; strangers never see it. Going to
someone behind a closed door knocks instead of quietly failing.

**A Key** lets its holder come and go, whoever's in. It's the host's to give, with no yes needed; the other can hand
it back. Asking for one is an Ask, so that needs the host's yes. It comes up in person, between people who are close:
a current Relation, or 10+ exchanges both ways, with no strain. Only the two of them are told.

**It survives a break-up.** Exes, or a sour Tie, get a strong pull to return it or take it back, from wherever they
are. Dropping it automatically would throw away the most interesting moment it has.

**Wanting forms at the door.** After a second Doorstep in a week, System One judges each side in character, and a hit
is voiced once in their own words, as an Acquired Desire. The visitor's is having, the host's is being. A reluctant
host simply doesn't get one. The Desire names its Place, as a having-desire names a catalogue item, so Relations can
foreground the act. The act is on offer at low pull without a want, so a model can have the idea itself.

## Why at the door, not overnight

Rest is where meaning usually forms here (ADR 0016), and it fits. But it only runs if the World is up at night, which
dev often isn't. The door is also where the thought would really come, and that keeps cause and effect traceable.

## Why not a threshold alone

Counting knocks makes everyone reach the same want at the same point. The count only decides when to ask; character
decides the answer. A free-form "what do you want now" can't be tied to an act at all.

## Consequences

- Keys live on the Place, so Places stays the one writer of who may enter. Prompts read them through `GetKeyLinesAsync`.
- Speech never sees the want (ADR 0026). It shows in thoughts and in what they do; the key itself is a plain fact in both.
- Players are left out: nobody asks a human, and there's no key UI.
- Models lean towards yes with a partner. If early Asks all land yes, move answering them to Reflex, which weighs what
  someone would plausibly do. Thresholds are set without live data; the Relations app shows the counts to tune them.
