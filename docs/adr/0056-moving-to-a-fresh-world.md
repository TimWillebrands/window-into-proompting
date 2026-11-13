# Moving to a fresh World: Nature travels, Nurture stays behind

> Status: accepted. Builds on ADR 0018 (World), 0022 (Nature and Nurture), 0035 (Relations), 0038 (lived sheets), 0039
> (the CDU) and 0046 (Player MCP). ADR 0053 is held by the Stream RFC. Vocabulary in `CONTEXT.md`.

Some Personas drift. With months of memory, Stances, Desires and Threads written by older versions of the code, there
is no telling whether the code or the data is to blame. The model already splits a Persona in two: Nature lives on the
Persona and travels, Nurture lives on the Participant in one Party. Everything to start clean was there except a way
to start a second World, stop the first and choose between them.

## Decision

1. **Move**: an admin moves everyone picked out of a World into a new one, from the login screen. What comes along:
   - each Persona's Nature (it was never in the Party);
   - the Participants, Drivers kept, and each human's claim on their Player, so nobody has to make a new character;
   - looks, so nobody's Figure is drawn anew;
   - the Relations the admin ticks, kind, story and dates kept, nobody living together yet;
   - the CDU's Company and its staff, on the same hours, taught the secret again;
   - a Player on auto stays on auto, with their mission;
   - the Places the admin ticks, as they were made: scene, colours, things. Ticked by default are what humans
     authored: Places an admin made, Places a Player had made, Players' Homes. Offered unticked: Places a Persona had
     made and the ones the town opened. A Place's District comes along by name. Who was in it, keys, guests, staff and
     things left by people who stay behind don't.
   Nothing else: no memories, Stances, Ties, Desires, Threads, Persona Homes, jobs or money. The seeded town and a
   fresh Home for every Persona are there anyway.
2. **Stopped**: the old World stops. A stopped World keeps everything and can be looked around in, but takes no
   writes (409 `This World is stopped.`), gets no Life turns, no CDU news, no mishaps, and nobody in it moves. An
   admin can start it again.
3. **Current**: one World is current: where MCP agents act and what the persona generator briefs from. A Move makes
   the new World current. It starts with the old World's clock settings, running if that one was.
4. **The login screen picks the World**, like XP picks a user. The browser remembers its pick; Switch World in the
   Start menu goes back. Admins stop, start again, make current and move from there; a Move is a wizard (name,
   people, Relations, Places, summary) that sends one request at the end.
5. **Distilled sheets are redone** (`LivedSheets.Version` 6), and with them every Intrinsic Desire and Skill nobody
   authored, so Nature is fresh too.

## Why not reset the old World in place

Resetting deletes what we'd compare against, and a reset touches every store (grain state, the memory graph, streams)
where a new Party touches none. A stopped World costs nothing to keep and can be started again.

## Consequences

- A Persona is in both Worlds. Removing a Player deletes their library Persona, which the stopped World still names.
- Other Companies, Practices, Gatherings and the Board don't come along; the World grows them again.
- Arrival (ADR 0022) is still unbuilt. A Relation's story is the only shared past a Move carries.
