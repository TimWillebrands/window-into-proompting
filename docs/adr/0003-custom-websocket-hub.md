# Custom WebSocket hub now, SignalR later

Realtime fan-out from the backend to the browser uses **raw `System.Net.WebSockets.WebSocket`** wrapped by `PartyRealtimeHub` (one session per Party, subscribed to Orleans streams), with a custom envelope `{ type, sequence, timestamp, data }`. SignalR — the .NET default — is **not** in use. Migration to SignalR is **planned**, not rejected.

## Why raw WebSockets today

- The hub's main job is to **bridge Orleans streams** (`IAsyncStream<PartyStreamEvent>`) into client WebSocket frames. Doing that with raw WS + a small envelope was a few hundred lines; doing it through SignalR's hub/client abstraction would have meant wiring Orleans-stream subscription into SignalR's connection lifecycle — more moving parts at a moment when the protocol shape was still being figured out.
- Sequence numbers + a single message-merge path on the client (`realtime-store.ts`) were easier to design when both ends were ours, no client SDK conventions to fit.
- Auth, fallback transports, and group/topic abstractions weren't needed yet — SignalR's wins didn't apply.

## Why we'll move to SignalR

- Reconnect, transport fallback (long-poll), and backpressure handling are nontrivial to do well; SignalR ships them.
- Once auth lands, SignalR's per-connection auth context is much cleaner than rolling our own.
- The custom envelope is bespoke knowledge every new contributor has to absorb. SignalR + typed hubs are familiar to most .NET devs.

## When

When one of the following bites: (a) reconnect/backpressure pain in production, (b) auth lands and starts duplicating SignalR's wheel, (c) a third client (mobile, CLI) needs to consume the stream and we don't want to re-implement the envelope.

Until then this is **acknowledged tech debt**, not a long-term position. New features that go through the realtime path should be designed so they'd port cleanly to a SignalR hub method (no envelope-specific tricks).

## Consequences

- The whole realtime path is custom: envelope, session lifecycle, reconnect, message merging. Bugs there have no Stack Overflow answer.
- The frontend `realtime-store.ts` is tightly coupled to the envelope shape. Any SignalR migration touches both sides.
- Will be superseded by a future ADR once migration ships.
