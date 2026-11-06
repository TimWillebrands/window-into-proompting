# 0037. Players on auto

Date: 2026-11-06

## Status

Accepted

## Context

A human should be able to leave their character running, living exactly as a Persona does, and take it back later.
Most of the code asks `Driver == User` to mean two different things: "the agent stack leaves this one alone" and "a real
person owns this, so protect them".

## Decision

**Auto is the Default Driver, nothing more.** Going on auto flips the Player's Participant from `User` to `LLM`. Life,
Reflex, the Response pipeline, Memory, sleep and Relations all follow without knowing. There's no auto flag: on auto
means owned and LLM-driven.

**Ownership decides what's a human's, the Driver decides what's played.** `PlayersByClerkId` survives the flip, and
`AccessRules.Humans` counts it. So DMs stay private, nobody else may speak as them, and `PlayerRemoval` still applies.
Evicting one from the World Monitor is a removal, or their Clerk id would point at nobody. Other Personas treat them as
one of their own: not "(a real human)", and Relations and intimate Touch are on offer.

**The sheet comes from their trace.** The character already exists, so nobody is asked who it is. New Players have a
placeholder sheet ("A human visiting this world."), which is nothing to play from. The first time on auto writes one from
their look, job and recent public lines, or one plain line for distilling to flesh out if there's nothing to go on or no
model answers. It's kept for next time. Anyone can read it, as with any Persona, so DMs never feed it.

**A mission is an Acquired Desire.** The human can give an optional line to go after. Source `Mission`, otherwise a
Desire like any other: it's in Life turns as what you want lately, reviewed as fulfilled, fades after a fortnight, and
shows in Work & Wants. A new one replaces the running one; taking over abandons it. Speech doesn't see it (ADR 0026), so
it steers what they do, not each line they say.

**One hand at a time.** While on auto, acting as your own Player is a 409, the same check for every action. Reading isn't
affected, and an admin may still act as them. Taking over cancels whatever the agent is saying.

**Their look stays theirs.** `Appearance.Kept` stops the once-per-Persona dressing from bio replacing what they picked.
A job is seeded on the way in if they have none, since a human's seeding was skipped. Means aren't: they stay as first set.

## Consequences

- Any new check that means "is this a real person's" must use `AccessRules.Humans`, not the Driver.
- A DM between a Player on auto and a Persona stays private, unlike Persona-only DMs in ADR 0029. It's still someone's.
- Everything the character did stays when the human takes over: posts, Relations, memories, money spent.
- Auto turns draw on the World's hourly action budget, so more Players on auto means fewer turns each, not more spend.
  Chat replies aren't under that budget, as for any Persona.
