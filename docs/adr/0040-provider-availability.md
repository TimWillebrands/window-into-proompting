# Provider availability: local first, cloud when it's down

> Status: accepted. Builds on ADR 0033 (Tailnet sidecars) and ADR 0034 (System One through providers). Vocabulary in
> `CONTEXT.md`.

A model on a home box behind a Tailnet should be used when the box is up and skipped when it isn't. Priority already
put it first, but the router only learned the box was gone from a failed call. That stalled the call about 20 seconds,
lost a streamed line, and benched the box for 10 minutes, so it came back late too. When the only decision model was
benched, System One threw instead of answering.

## Decision

1. **A provider is Ready, Benched or Offline.** Benched means it answered but refused (bad key, no credit, missing
   model, a judge that timed out): skip it for 10 minutes, but still use it when nothing else is left. Offline means the
   connection never happened: leave it out entirely, so the caller drops to its next tier instead of waiting on a dead
   box.
2. **The router pings every enabled provider every 30 seconds.** A local one counts as reachable when it lists its
   models, through its Tailnet if it has one. A ping that fails marks it Offline before any call hits it, and the first
   ping that succeeds takes it back, clearing any bench from while it was down. A bench on a provider that never went
   away runs its 10 minutes.
3. **Only local providers really ping.** Cloud providers always answer reachable. Their `/models` stays up while the
   key is out of credit, so a ping says nothing, and a failed call benches them as before.
4. **On a local provider, a connection failure in a call also marks Offline.** That's no response at all, as opposed to
   an error status, so the next ping can take it back instead of waiting out a bench.
5. **A stream that fails before its first chunk moves on.** The call skips that provider, then that tier if it has no one
   else, and the caller never sees it. After the first chunk the failure surfaces, since the caller has already used
   the text.
6. **System One falls back to emulation.** When every Decisions provider has failed, or none can be routed, one
   General-tier LLM call answers the questions (`LlmJudge`). A failed beat costs more than a less calibrated answer.
7. **Control Panel shows each provider's state** with the reason, and the fallback order per tier.

## Considered

- **Pinging with a real one-token generation:** proves the model loads, but costs GPU time every 30 seconds and would
  keep a model in VRAM that should be free to unload.
- **Checking that the model is listed:** llama.cpp lists its one model under whatever name it likes, so a mismatch
  would mark a working box Offline.
- **A shorter bench instead of pings:** still loses a call each time the bench runs out while the box sleeps.

## Cost

Emulated decisions are LLM calls on the beat path. With every decision model down they run at the beat rate, on the
General tier's model.
