# Property-anchored Events; drop Party/Room/Message vertices

> Status: accepted. Reshapes the AGE graph from [ADR 0006](0006-pure-age-for-memory.md) /
> ADR 0007 (folded into 0016). Supersedes the *shape* shown in the
> examples of ADR 0009 (folded into 0015); the recall query is unchanged in
> spirit — it still walks `(:Participant)-[:RECOLLECTS]->(:Event)` — only the surrounding
> vertices are fewer. The **language** in `CONTEXT.md` is untouched: this is shape, not
> vocabulary.

## The change

The memory graph carried five "stub" vertex labels that mirrored grain-side identity —
`Party`, `Room`, `Message` — plus `Persona` and `Participant`. Scoping already ran on
`party_id` *properties*, and the debug viz explicitly excluded the `Party` node. The stubs
were pure `MERGE` overhead.

New shape — **4 vertex labels** (`Persona`, `Participant`, `Concept`, `Event`) and
**4 edge labels** (`HAS_PARTICIPANT`, `RECOLLECTS`, `ABOUT`, `STANCE`; `STANCE` is still
reserved for a later slice):

- **Dropped `Party` vertex + `IN_PARTY` edges.** Every consumer already filtered on the
  `party_id` property of Participant/Event. The Party vertex anchored nothing.
- **Dropped `Message` vertex + `ANCHORED_TO` edges.** An `Event` already knew its
  `anchor_message_id`; it now also carries `room_id` and `party_id` as properties. Message
  *content* never lived in AGE — it is in the Orleans journal; the Event is an index/anchor,
  not a copy. The viz reconstructs the "which message" affordance from the Event's
  properties (shown in the side panel) instead of a Message node.
- **Dropped `Room` vertex.** It existed only so the debug viz could anchor *empty* Rooms.
  With `party_id` on Event, the viz scopes via `MATCH (e:Event {party_id: …})` directly and
  lists Rooms from the regular REST API (the frontend already enriched Room/Persona display
  names client-side). Consequence: empty Rooms no longer appear as graph nodes — acceptable
  for a memory viz, which is about what's been *remembered*. This also let us delete
  `IMemoryRepository.EnsureRoomAsync` and its eager call site in `PartyGrain.CreateChatGroup`.

`MemoryRepository` write blocks shrank accordingly: `CreateEventAsync` stamps
`room_id`/`party_id` onto the Event and no longer `MERGE`s Room/Message; the `ABOUT` and
`RECOLLECTS` writers no longer `MERGE` a Party or `IN_PARTY` edge. `GetPartyMemoryGraphAsync`
anchors on `MATCH (e:Event {party_id})` and emits no Room/Message/Party nodes.

## Two capture-path cleanups bundled in

These rode along because they touch the same files and matter before import bakes in the
current behaviour:

1. **Batched Recollection extraction (1+N → 2 LLM calls).** Capture previously fanned out
   one `ExtractRecollectionAsync` call per present non-User Participant. It now issues a
   single `ExtractRecollectionsAsync(targets)` call returning a name-keyed JSON map, mapped
   back to `PersonaId`. Per-Participant spin is preserved (each target tagged SPEAKER /
   OBSERVER inline so the speaker gets "you said…" framing and observers get "you saw…").
   A target the model declines (NONE / missing / unparseable) simply gets no Recollection —
   same degradation as the old per-call failure path. Capture is now exactly **2 LLM calls
   regardless of cast size**.

2. **Concept match-or-mint at capture.** Before the event-extraction call, the repository
   fetches existing Concept display labels and the prompt instructs the model to *reuse* an
   existing tag when one fits, only minting when none do. This heads off fragmentation
   ("lisp" / "common lisp" / "lisp programming") that would later wreck graph-walk recall.
   `NormaliseConceptName` stays as the write-side dedup backstop. The fetch pastes at most
   ~60 names into the prompt; once Concept volume outgrows that, switch the fetch to a
   prefix/fuzzy match (the seam is `FetchExistingConceptDisplaysAsync`).

## AGE index gotcha

The property-functional indexes were written `((properties ->> 'name'))`. AGE's `->>` takes
an **agtype** key on the right, so a bare SQL string raises `invalid input syntax for type
agtype` and **aborts initdb** — which is why those indexes never actually existed in any dev
DB (the fixture even worked around their absence). They now use the agtype-quoted form
`((properties ->> '"name"'))`, verified to create cleanly on a fresh AGE volume. So fresh
`docker volume rm partytown-pgdata` + re-init now produces a fully-indexed graph, not a
half-initialised one.

## Consequences

- **Fewer writes per capture.** No Party/Room/Message `MERGE`s; the Event is a single
  `CREATE` with its anchor inline.
- **Empty Rooms are invisible in the viz.** By design — the viz reflects memory, and Rooms
  come from REST. If this proves confusing, the fallback is to re-introduce a Room vertex
  (Party/Message stay gone); not expected to be needed.
- **`anchor_message_id` / `room_id` are properties, not edges.** A future "all Events that
  touched message N" query is a property scan, not a graph walk. Fine at current scale; if
  it becomes hot, add an Event `anchor_message_id` index (cheap) or reintroduce a Message
  vertex then.
- **Recall is unchanged.** `RecallRecentSnippetsAsync` still walks `(:Participant)-[:RECOLLECTS]->(:Event)`; fewer Event neighbours don't touch it.
