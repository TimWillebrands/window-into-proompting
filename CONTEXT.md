# Proompting

The domain language of Proompting (aka Partytown): a Windows-XP-themed app where one human and several AI **Personas** hang out in **Rooms** and talk.

## Language

**Party**:
A tenant boundary. Hosts a set of **Rooms** and a list of **Participants** (the **Personas** present in this Party). Each Party is a self-contained universe with its own cast, its own Rooms, and its own per-Persona **Memories**.
_Avoid_: server, workspace, tenant, world, desktop.

> Note: today the frontend always opens the default Party (id = `Guid.Empty`, exposed as `ROOT_PARTY_ID`). The API is fully multi-party; that surface is intentional, not dead code.

**Room**:
A named conversation thread inside a **Party** that a small cast of **Participants** talks in. Has its own message log, participant list, and optional **Scenario** text.
_Avoid_: chat-group, chat-room, channel, thread.

> Note: the backend code currently spells this `ChatGroup` (`ChatGroupGrain`, `ChatGroupEvent`, `chatGroupId` in API paths). That spelling is legacy — treat `Room` as the canonical name in all new code, comments, issues, and docs. Existing code can be renamed opportunistically.

**Persona**:
A character with a name, bio, system prompt, and behavioural dials (**Chattiness**, **Impulsivity**). Lives outside any Party — Personas are library entries that can join multiple Parties (eventually) and accumulate per-Party **Memories**. A Persona is a definition, not a membership.
_Avoid_: character, agent, bot, NPC, user.

**Participant**:
A **Persona**'s membership in a **Party**. The link object that says "Persona X is in Party Y, driven by Z." Rooms draw their cast from the Party's Participants. Removing a Persona from a Party deletes the Participant; the Persona itself remains in the library. The Participant owns the **Driver** — the same Persona can be User-driven in one Party and LLM-driven in another.
_Avoid_: member, attendee, party-member.

**Driver**:
The thing currently operating a **Participant**. Today, one of two kinds: `User` (a human types the messages) or `LLM` (the agent stack generates them). Driver lives on the Participant, not the Persona — so a Persona can have different Drivers in different Parties.
_Avoid_: operator, pilot, controller, agent.

> Note: today the backend expresses this as a `bool IsUser` flag on `PartyParticipant`. That flag is the current crude form of `Driver` — promote to a `DriverKind` enum (or equivalent) when a third kind appears.

**Scenario**:
Free-text in-fiction setup for a **Room**: where we are, what's going on, what the mood is. Visible to (and influences) every Persona in the Room. Optional — Rooms can have no Scenario.
_Avoid_: prompt, setting, premise, context.

### Persona behaviour dials

**Chattiness** (0..1):
A Persona's baseline urge to speak. Low = brooding, only chimes in when directly addressed or when something jumps out. High = wants in on every conversation.

**Impulsivity** (0..1):
How committed the Persona is to a reply once they've started forming one. Low = deliberative, easily interrupted by something new in the room. High = impulsive, barrels through their thought once committed.

## Relationships

- A **Party** contains zero or more **Rooms**.
- A **Party** has zero or more **Participants**.
- A **Participant** refers to exactly one **Persona**.
- A **Persona** can be a **Participant** in zero or more **Parties** (one Participant per Party).
- A **Room** draws its cast from its **Party**'s **Participants**.
- A **Participant** has exactly one **Driver** (`User` or `LLM`).

## Example dialogue

> **Dev:** When the human sends a message in a **Room**, what's the sender on that message?
> **Tim:** A **Participant** — specifically the one whose **Driver** is `User`. The human acts *through* a Persona; there's no separate "user message" path.
>
> **Dev:** So a **Persona** can be in multiple **Parties** with different Drivers in each?
> **Tim:** Yes — same Persona, different Participants. The Driver lives on the Participant. Each Participant has its own per-Party Memories too, so the same Persona will "remember" different things depending on which Party you meet them in.
>
> **Dev:** If I delete a **Persona** from the library, what happens to a **Room** that had them as cast?
> **Tim:** That's an ambiguity worth flagging — see below.

## Flagged ambiguities

- **"chat-group" / "ChatGroup"** was used throughout the backend to mean **Room** — resolved: **Room** is canonical; `ChatGroup` is a code-level legacy spelling that will be migrated opportunistically.
- **`IsUser` flag** on `PartyParticipant` is the current crude form of **Driver** — resolved in vocabulary (`Driver` is canonical, `IsUser` is the legacy field), code rename deferred until a third `DriverKind` appears or auth lands.
- **Persona deletion semantics** are not yet defined. If a Persona is deleted from the library, what happens to Participants referencing it (and to the messages they sent)? Soft-delete with tombstones? Hard delete with orphaned messages? Block deletion while participating? **Undecided.** Not blocking today — only set when this feature is built.
