# Relations: what people are to each other, and sharing a Home

> Status: accepted. Builds on ADR 0020 (Homes), ADR 0021 (Ties).

A Tie says how one Persona feels about another: how much they've talked, how warm it is lately.
Nothing said what two people *are*. Justin and Anne are married, Justin and Denise dated in high
school, and neither fact can be read off a talk count: a married couple who haven't spoken yet read
as strangers.

## Decision

A **Relation** is a fact about a pair, stored once per Party, so both sides agree. It has a kind, an
optional story in the pair's own words, and may be current or past. Past ones stay on record; a pair
can have several. Ties are untouched: a Relation is the fact, a Tie the feeling.

**Forming in play takes a yes.** A Persona asks in person, at the same Spot: go out, move in, marry.
The other answers on a later turn; no answer within a day lapses, and a pair can't ask the same
thing again for three days. Ties gate what's offered, both ways. Breaking up takes one person, once
things have gone sour. Asking is private (the feed says "has a quiet word"); only a yes is public.

**Personas don't ask humans**, like intimate poses. Authors can declare any Relation, humans included.

**Current Relations are public, past ones stay between the pair**, so Anne can find out about Denise
through talk, as a Recollection, rather than being told by the prompt.

**Sharing a Home:** the mover becomes a Housemate of the host's Home, and their own Home is kept
empty (Vacated). After a split it comes back as it was, decor included. Everyone who lives in a Home
gets the owner's standing there. `OwnerId` stays: it says whose Home it was first, and who stays
after a split.

**A built Home replaces what they had.** An author can have a couple's Home designed from a description
("een statig herenhuis"), with the Place designer. The Homes they had are removed, not Vacated: that's the
point of building one. It keeps its given name, and whoever leaves after a split gets a plain Home.

## Why not put it on the Tie

Ties are per Persona and asymmetric, one grain each. A marriage on two Ties can disagree. Consent
also needs one writer for the pair.

## Why keep the empty Home

Removing it means rebuilding it after a split, and losing what they put up. An empty Home costs one
flag, checked wherever Homes are listed.

## Consequences

- RelationsGrain calls Places and Ties. Ties reads back from it during prompts, so `GetAsync` must
  stay `[AlwaysInterleave]` or the two deadlock.
- `LiveTogether` on the Relation mirrors Places. All moves go through RelationsGrain to keep them in step.
- Partners skip the talk-earned gates on touch, since a couple may have no history in this World.
- Family and Friends are authored only; nothing forms them in play.
- Players can't ask or answer yet. Authors act for them.
- A Home needs spots `sofa`, `desk`, `kitchen` and `bed`: Furni seeds on them, and sleep walks to `bed`. A design without them is sent back.
- `SyncCast` makes a Home for anyone living nowhere, so it must ask where people live, not what they own.
