# Conversations live in Messenger

> Status: accepted. Builds on ADR 0018 (the World and its Apps), 0049 (Places worth going to), 0059 (playing as a
> Persona), 0067 (the UI kit). Vocabulary in `CONTEXT.md`.

D'Accord was a Discord-like app of public, named Rooms with a director's console: Scenario text, "Speak as" driver
overrides, Proceed, a Thought Log and per-message redo/cut/delete/remember. Its lobby ran on stub data. Meanwhile
Places became where things happen and who is there, the Partyboard became the public community, and Messenger (with
its MSN skin) became the place people actually talk. D'Accord's only remaining purpose was group chat, and its
director tools were testing instruments that broke the fiction for players.

## Decision

1. **Conversation is the domain term.** A Conversation is a Messenger conversation, one-to-one (a DM) or a group,
   or the talk at a Place. "Room" is retired from the language and the UI; the code keeps `ChatGroup`, and the stored
   kind `room` now means a group conversation (no data migration).
2. **D'Accord is retired.** Its program, desktop icon, app code and lobby go. Group conversations live in Messenger.
3. **Group conversations are private and start fresh.** Never public or joinable. A human starts one by inviting
   someone from an open conversation or by picking several contacts. Inviting never turns a DM into a group: a DM's
   words stay private to its two members (ADR 0059), so the invite opens a new group conversation with everyone in
   it and no history. Inviting someone into an existing group adds them to it; a group is shared among its members,
   so the newcomer can read what was said before. Anyone can leave a group.
4. **Personas take part in groups.** In Life, a Persona can start a group conversation with two or more people it
   knows (a Tie or Relation), at low priority and at most one new group per Persona per day; invite someone it knows
   who knows another member; talk in groups it's in; and leave a group once the conversation thermostat says it has
   run its course. They can no longer drop into a public room: being somewhere together is a Place.
5. **Director and message tools are removed**, front and back: Scenario, driver overrides, Proceed, the Thought Log,
   and redo/cut/delete/remember on messages. Captures still happen when a conversation goes quiet. An integrated
   testing mode (regenerating or editing any recent expression) may be designed later as its own feature; "play as
   Persona X" is a separate future feature beyond ADR 0059's take-over.
6. **Gatherings happen at Places only.** Planning can happen anywhere; a Gathering has a Place, and when it's due
   attendees get a Life turn to go there. Upcoming Gatherings that were in a Room are cancelled as having no venue.
7. **Event-sourced history keeps replaying.** Journaled events for removed features (Scenario set, driver overrides)
   stay deserialisable and are applied as no-ops; grain state fields keep their serialisation ids.

## Consequences

- The Rooms World App folds into Messenger's; Personas' affordances mention groups by their members, not `#name`.
- Existing Rooms become group conversations of their current members, no longer joinable, and lose their Scenario.
- The Player MCP, the API and the generated client lose the removed actions.
- Public, ongoing community is the Board's job; public, live company is a Place's.
