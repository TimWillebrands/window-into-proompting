# Real work: Companies on real Markets, Practices that sell Sessions

> Status: accepted. Builds on ADR 0018 (Apps, Initiative), 0024 (economy), 0026 (Occupation, Responsibilities)
> and 0027 (Locales, Openings). Vocabulary in `CONTEXT.md` (§ Real work).

An Occupation only fills time: the `work` verb sets an Activity. The world is also a closed loop: LLMs talking
to LLMs converge (ADR 0021), and the worn words are still till, tin, lid and tray. Work should produce something
nobody authored, and someone who cares about it. Both capabilities are configured, not hardcoded: "start a Company
that trades in X", "this Persona is a career coach for logistics".

## Decision

1. **The system of record reports outcomes, never the Persona.** Agents fabricate results when they narrate their
   own work. Prices, fills, Settlements and Fees come from code; a Persona can brag in chat and the Book still decides.
2. **No agent loop.** Both capabilities are `IWorldApp`s: one choice, one structured call. Trading needs no tools, and
   a tool loop belongs with the repo idea, which is parked.
3. **No human/persona distinction.** Staff, Practitioners and Clients are Participants. Every act is one grain command,
   called by Life or by HTTP alike, and the Effective Driver decides who types in a Session. This is a research
   project, so Sessions carry no human safeguards.
4. **Ghost fills on real quotes.** The World only reads Markets. Buys fill at the ask and sells at the bid, plus a
   fee, so round trips cost money. Without those, a Book earns free money. Markets without bid/ask get a set spread.
5. **Books never touch Wallets.** Daily pay is 30–80 credits, so one lucky Trade would break the shops. Staff get
   shift pay. A Book moves moods and memory instead.
6. **Only the Market settles.** Polymarket's `endDate` lies both ways: markets stay open past it and close years
   before it. Settlement needs `closed` and `resolved`. A failed fetch leaves a Position stale, not settled.
7. **Memory only at thresholds.** Prices are not Events. Settlements, closed Positions and big daily moves are,
   for staff only, so one Company can't flood the world's Salience.
8. **Market text is untrusted.** Only a clipped, control-stripped title enters prompts; descriptions and comments
   never do. Everything else is numbers.
9. **A paused World doesn't trade.** Marks and Settlements catch up on resume, as one gap. The clock drives
   refreshes, so pausing needs no extra rule.
10. **A Session is a booking, not a kind of conversation.** It holds who, when, why and the Fee, and nothing else about
    it is special. At the slot the Client joins the Practitioner's Spot; a Locale opens as it does for a Gathering. They
    talk in Place talk, so anyone at that Spot hears it, the thermostat reads it and lull capture remembers it. Why they
    met reaches speech only as a fact in `# What you owe`. It isn't a Gathering because nobody else is invited.
11. **No special rules for Sessions.** We built a private thread, sealed memory, a thermostat exemption and a longer
    round limit, then removed them all. Real practitioners get overheard and talk about clients, and a leak is drama,
    not a bug. Don't add confidentiality to the talk or memory layers.
12. **Practices pay by Fee, not by shift.** No Clients means no income. Fees are charged when a Session took place,
    so a no-show costs nothing. This is the first money that moves between Participants.
13. **Fit is judged, not hardcoded.** One System One noul per Client and Practice asks whether this Practice speaks to
    what's going on with them, so a career coach and a therapist attract different people with no rules per trade.

## Consequences

- Stocks use Yahoo's unofficial chart endpoint: keyless but with no bid/ask or guarantee. A keyed vendor is one more
  `IMarketFeed`.
- Publishing live exchange prices on a public site may breach vendor terms. Check before relying on it in production.
- A Practitioner's Wallet can sit flat for days. That is what self-employment is.
