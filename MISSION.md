# Mission: Vervaeke's cognitive science, as embodied in Proompting

## Why
Tim is a software architect who intends to own and extend Proompting. The codebase's core design decisions (memory model, response pipeline, recall, consolidation) are derived from John Vervaeke's cognitive-science framework — relevance realization, the four kinds of knowing, agent–arena, opponent processing. Without the framework, the architecture reads as arbitrary; with it, every ADR becomes predictable. The goal is to make design decisions *from* the framework, not just decode past ones.

## Success looks like
- Given any construct in `CONTEXT.md` (Salience, Stance, Arena, Consolidation…), can name the Vervaeke concept behind it and say why the code shape follows from it.
- Given a new feature request, can identify which of the four RR instances (Interruption / Urge / Recall / Capture) it touches and which opposing pulls must stay in tension.
- Can critique or write an ADR in the same idiom as ADR 0015 ("recall as relevance realization") — spotting when a proposal collapses an opponent process into a single tuned formula.
- Can explain the 4P memory layering (propositional/perspectival/participatory/procedural) well enough to place a new memory feature in the right layer.

## Constraints
- Learner is an experienced architect: skip software fundamentals, go deep on the cognitive-science side and its mapping to code.
- Prefers concise, low-filler communication (caveman mode active in chat; lessons written normally).
- Learning happens alongside real work in this repo — lessons should anchor to actual files/ADRs so review happens naturally during development.

## Out of scope
- Vervaeke's religious/meaning-crisis programme beyond what grounds the cognitive science (wisdom traditions, ecology of practices) — only pull it in when a code concept needs it.
- General prompt-engineering (covered by the `proompter` skill).
- Orleans/AGE mechanics for their own sake — only as carriers of the concepts.
