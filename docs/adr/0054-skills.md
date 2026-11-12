# Skills: knowing how is learned, and shows only where it's used

> Status: accepted. Fills the Procedural slot of the memory model (ADR 0015, 0016). Builds on 0021 and 0038 (lived
> sheets), 0026 (Occupations, Desires, Promotion), 0030 (Practices), 0049 (Features and Takes). ADR 0053 is held by the
> Stream RFC. Vocabulary in `CONTEXT.md`.

Three of the four kinds of knowing have an entity: Events and Concepts (that), Recollections (what it was like),
Stances (by being in relation). Procedural knowing, knowing *how*, had none. It lived in the dials and in prose: the
lived sheet's job line, its `## With other people` bullets ("you fix bikes"), its `## How you talk`. Prose that is
always on gets performed (0021): a barista who knows coffee talks about coffee. And nothing a Persona did in the World
taught them anything. A hundred shifts at the Café left them exactly as good at it as the first.

Rule of thumb: what shapes every line stays in the sheet (how you talk). What comes into play only when the moment
calls for it is a Skill.

## Decision

1. **Skill** (`Skill`): something a Persona knows how to do. A short name ("pulling espresso", "calming someone who's
   panicking"), a **how** (1–3 sentences of the judgement and steps a practised hand has, second person, no example
   lines), a few **cues** (the words a moment has to touch for it to come to mind), a practice count and when it was
   last practised. Shaped like an agent skill on purpose: the name is what's known about it, the how is loaded only
   when it's in use.
2. **Intrinsic Skills** (`IntrinsicSkillsGrain`, key persona id): who they are. Distilled once from the lived sheet
   (2–4: their trade's craft, something from home or history, a way with people), re-distilled when the sheet
   changes unless an author set them. They count as second nature.
3. **Acquired Skills** (`SkillsGrain`, key `party:participant`): learned in play, local to one Party, Promotable like
   Stances and Desires. Practising is the only way in ("practice" stays the Real work term, 0030). Each time names
   what was done and a key:
   - a shift worked (`work:<title>`), and a Practitioner's Session (same key: it's the same craft);
   - a Feature done (`feature:<what>`, ADR 0049);
   - a being-Desire to get better at something, fulfilled (`desire:<id>`, counts as three at once).

   A key practised three times is looked at once by the General tier: is there know-how here that grows with doing
   it? If it's one they already have, the key joins it; if not, a new Skill is written; if there's nothing to learn
   (writing a wish on a wall), the key is closed and never asked again.
4. **Levels come from practice, not prose.** New to it (under 3), getting the hang of it (3–9), practised (10–29),
   second nature (30+). Crossing a level rewrites the how from what they did, so it grows with them. An Acquired Skill
   untouched for 30 days reads as rusty; procedural memory fades slowly and is never deleted.
5. **Shown only where it's used** (progressive disclosure). No skill list in any prompt. Matching is deterministic:
   content words against cues and name, no LLM.
   - **Initiative:** an option that exercises a Skill says so ("look through the telescope (you know how: reading the
     night sky)"). No extra pull; System One weighs who they are. The voicing call for the picked option gets that
     Skill's how, so the intent is a competent one.
   - **Doing a Feature:** the Muse is told the doer knows how, so the Take is what someone who knows would notice.
   - **Rooms, DMs, Place talk:** at most one Skill, the best match on the live exchange, as `# What you know how to do`
     in both Decision and Speaking (same reasoning as Stances, 0016). Framed as know-how, not a topic: it shows when
     it's useful and is never brought up for its own sake.
6. **The sheet keeps the voice.** `## How you talk` shapes every line and stays. `VoiceWear`'s per-speaker tics are
   procedural *feedback* on that voice (habit correction from counts), not learned know-how, and stay a Lived self
   signal.
7. **Lived sheets are warmed before they're read.** The distilled sheet sits in an in-process cache. It used to be
   filled by a detached call once per activation, so the first prompt after a restart, and every prompt after a failed
   distillation, read the raw authored sheet, and Desires and Skills distilled from it. Now the stored sheet is loaded
   and awaited (no LLM), distillation stays detached and retries, and intrinsic distillers wait for a lived sheet
   instead of distilling the raw one.

## Considered

- **A skill list in the sheet or in Right now.** Always-on prose is what 0021 removed; it would be performed.
- **Skills as graph edges** (like Stances, in AGE). Nothing walks them: they're matched on cues and read per Persona.
  Grain state, like Desires.
- **Decision picks a skill** (the Recollection handoff). Another handoff field (0010 warns), and an LLM choice where a
  word match does.
- **Practice from every Activity, judged at rest.** Catches more (cooking at home, chess with a friend) but turns
  every deed into a judge call and every summary into a candidate. Explicit practice sites first.
- **Skill pull in Initiative** (+1 like wanting). Doing what you're good at is already what "being who they are"
  weighs; a bonus would make everyone exploit.

## Consequences

- One General-tier call per Persona per sheet for Intrinsic Skills; one per key at its third practice; one per level
  crossed (at most three per Skill).
- Speaking and Decision gain at most one short block, only when the talk touches what they know.
- Social know-how ("calming someone down") reaches talk only through its cues. If those never match, it is still
  in the Life voicing; widen matching before adding a judge.
- Players practise too. Their Skills matter once they're on auto (0037).
