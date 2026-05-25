# Default Driver on Participant, override on Room

A **Participant** carries a **Default Driver** (Party-scope); a **Room** may carry zero or more **Driver overrides** (per-Persona). The **Effective Driver** consulted by the Response pipeline is the override if present, else the default. This gives the same Persona-in-a-Party two natural shapes — "User-driven in the den, LLM-driven in the kitchen" — without inflating the Participant link into a per-Room object.

## Considered Options

- **Party-scoped Driver only.** Driver lives on Participant, identical across every Room of the Party. Simplest model. Rejected: makes the "drop into one Room and watch your Persona drive itself" use case impossible without faking it (deleting and re-adding the Participant, or duplicating Personas).
- **Room-scoped Driver only.** Driver lives on the Room's Participant list, no Party-level concept. Rejected: every new Room would have to re-declare its Drivers from scratch, and the Party-wide "this is *my* Persona" semantic disappears.
- **Default + Override (chosen).** Two layers: Party-level default, Room-level sparse override. Slightly more shapes to maintain, but matches user intuition ("normally Vlad is mine, but in this Room he's LLM") and keeps the Room override map empty for the common case.

## Consequences

- `PartyParticipant.IsUser` survives as the storage form of Default Driver (legacy spelling, kept until a third Driver kind appears or auth lands).
- `ChatGroupGrain` (the Room) stores `HashSet<Guid> ParticipantIds` + `Dictionary<Guid, DriverKind> DriverOverrides` — no longer a copy of `PartyParticipant`.
- Effective-Driver resolution lives at the call site (PersonaGrain), not in PartyGrain (which knows nothing of Rooms) or ChatGroupGrain (which knows nothing of Personas' library entries).
- Two new endpoints on the Room: `PUT /chatgroups/{id}/participants` (the membership set, Ids only) and `PUT /chatgroups/{id}/driver-overrides` (the override map).
- Consumers (Decision/Speaking/Memory phases) only ever see the Effective Driver via `ParticipantView.Driver` / `SelfView.Driver` — the default/override distinction is invisible to them.
