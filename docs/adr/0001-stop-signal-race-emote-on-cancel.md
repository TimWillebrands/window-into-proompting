# Race-cancelled replies become in-character emotes, not errors

When a new message arrives mid-generation, the in-flight Persona runs a **stop-signal race** (decide-phase → always cancel; generation past PNR → can't cancel; generation pre-PNR → score salience via LFM2 → cancel if `cancelScore > 0.5`). On cancel-pre-PNR, the half-formed reply is **discarded** and replaced with a freshly-generated, in-character abandonment line (`ChatMessage.Kind = "emote"`, rendered as an italic action), rather than dropping the message, surfacing a `[cancelled]` placeholder, or letting the stale reply complete.

## Why this and not the obvious alternatives

- **Why not silently drop?** The Room would lose a beat — the Persona was visibly typing/streaming, then nothing. Breaks the conversational rhythm and makes interruptions feel like bugs.
- **Why not `[cancelled]` / error?** Breaks the fiction with system-y chrome. The whole product premise is "characters talking" — an OOC banner is a tonal break.
- **Why not let it ride?** That's what pre-Race behavior was. Personas would steamroll over interjections, making fast-paced exchanges feel rude/dumb.

## Consequences

- Race-cancellation produces an **extra LLM call** (the emote generation) — paid per cancellation. Worth the cost because cancellations are the rare path and the emote is short.
- The emote is generated against the *stale draft* + the *interrupting message*, so it can reference what the Persona was going to say. This is the point of keeping the draft around even after cancelling.
- `Kind = "emote"` is now a permanent message taxonomy axis — anything that wants to filter "real speech vs in-character actions" must respect it.
- Future work that introduces *other* Persona reactions (laughter, gestures, leaving the room) probably extends `Kind` rather than introducing parallel concepts.
