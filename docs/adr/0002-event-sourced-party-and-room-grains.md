# Event-source Party and Room, leave everything else plain

`PartyGrain` and `ChatGroupGrain` (will be renamed `RoomGrain`) inherit `JournaledGrain<TState, TEvent>` and persist as a stream of events (`PartyEvent`, `ChatGroupEvent` subclasses). `PersonaGrain`, `LlmRouterGrain`, endpoint grains, and the root registry grains use plain state (or `IPersistentState<T>`). The event-sourcing choice is **scoped, not blanket**.

## Why event-source Party + Room

These two grains own the **conversational history** of the product — what was said, when, by whom, what generations were attempted, what the stop-signal race decided. That history is the *audit log of the product*, not just incidental state. Event-sourcing means:

- The thought-log / papertrail / debug views are free — they're just projections of the journal.
- Reprompts and message-deletions are events on top, not destructive overwrites — easy to add new history-rewrite operations without losing the prior shape.
- Stop-signal race outcomes, skipped turns, and generation failures are first-class persisted history. Anyone (Persona, UI, future analytics) can replay them.

## Why not event-source the rest

- **Persona** is config — name, system prompt, dials. Edits are rare and don't form a history anyone reads back. Plain state wins on ceremony.
- **LLM router / endpoint grains** are stateless or near-stateless (model lists, provider config). No history value.
- **Root registry grains** are indices — they exist to enumerate, not to remember.

## Consequences

- Changing the event shape of `PartyEvent` / `ChatGroupEvent` is a migration, not a refactor. Adding events is safe; removing or renaming existing events requires upcast logic on the journal.
- Snapshots may eventually be needed if the event journal of a long-running Party gets large. Not built yet — defer until it bites.
- New "things that happened in a conversation" should default to a new event subclass, not a side table.
