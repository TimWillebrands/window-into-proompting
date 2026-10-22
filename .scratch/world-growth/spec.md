# World growth: Districts, Locales and Openings

Status: ready-for-agent
ADR: docs/adr/0027-world-growth-districts-locales-openings.md
Neighbours: ADR 0026 Occupations (proompting-75, branch `occupations`) calls Places' Locale API for offscreen shifts.

## Problem

The drawn world is fixed: three public Places plus Homes. The Day says "office in Zuid", but the Body stays
at the Café. New Places cost up to 3 General calls and only the author can make one. The world should grow
from what people do, with thresholds, so that one Persona's passing thought can't change it.

## Shape

Ladder: Locale → Sketched → Kit-drawn → Bespoke. Footfall per Locale, capped at 1.5 per Participant. Opening
needs footfall ≥ 3, from ≥ 2 Participants, across ≥ 2 days. Demand (a Gathering there, a FollowThrough, the
Player) opens to Sketched at once. At most one Opening per world-day. Dormancy after 7 quiet days. Generation
is choosing: piggybacked signals, a closed vocabulary, System One for the archetype, Kits, procedural
Districts, and a curated CC0 GLB catalogue.

## Decided (instincts, revisit if Tim objects)

- City: loosely real Amsterdam.
- A lone workplace stays a Locale until someone needs to go there.
- Sketched Openings are automatic, with an author veto. Bespoke is author only.
- Away from Day beats only for beats of 2 h or more with no conversation in progress. Shifts are driven by
  Occupation (ADR 0026).

## Slices

See `issues/`. Order: 01 (done) → 02 → 03 → 04 (dry run) → 05 → 06/07 (catalogue, kits) → 08 → 09.

## After review (2026-10-22)

Fixed:
- Visit openings now share the one-a-day budget, so one Persona's "on my way" can't open a place on demand twice a day.
- Anchor and Day stretch count as one presence per person per day, so a workplace isn't double counted.
- Day errands only send Bodies offscreen, never to drawn Places.
- District doors are keyed by Place id. They had never matched: the client looked them up by scene name.
- "Zuid" and "Amsterdam Zuid" resolve to one District, and the Day prompt lists the District names.
- The Player can Visit someone away, which opens the Locale.
- Footfall messages are culture-invariant.

Still open:
- Lives-in anchors from the lived sheet.
- Talk signals from Capture concept tags (today it's a whole-name match, so talk footfall will be low).
- Slotted kit scenes (every office looks the same).
- Gathering at a Locale.
- Tram-stop exit in the centre scene.
- Counting repeat catalogue misses.
- The Bench probe.
