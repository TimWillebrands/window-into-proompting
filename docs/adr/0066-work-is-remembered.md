# Work is remembered, and Cytheurgy is work on the world

> Status: accepted. Builds on ADR 0026 (Occupations), 0030 (real work) and 0039 (the
> CDU); changes 0039's points 2 and 10. Vocabulary in `CONTEXT.md`.

Asked about her job, Denise said she makes an agenda tool. She had 259 work memories and not one of them said what
the work was for: they were CDU replies kept one by one, full of commit ids and file names, and every look at the
trunk was another. An ordinary job left nothing at all: a shift started, ended and paid, and nobody remembered it.
The one text that said what Cytheurgy is stayed on the desk, and it said it as code ("the Forms are code, the code
this whole world runs on"), which is the simulation thought the lore was there to keep out.

## Decision

- **A shift leaves at most one memory: the Shift memory.** When a shift ends, or someone leaves early, they look back
  on it once, as themselves: what they did and what it was for, in one or two sentences. The World hands them what
  was in it: the day's beats inside the shift, what they did at work, what reached their Company, and for a
  Cytheurgist what was asked of the CDU and what it said. It is captured as their own line (source `work`), so it is
  recalled like anything else they lived.
- **"None" is an answer.** Most shifts are like the one before, and a day with nothing in it leaves nothing.
- **Every job, one way.** `WorkDays` closes the shift from `Workplaces`, for a barista and a Cytheurgist alike. A
  Company adds its frame; the CDU adds the calling and a lens.
- **The plumbing stays on the desk.** A reply from the CDU, a look at the trunk, a parked message and the tide are
  shown on the desk and in the feed, and are no longer memories. A verdict of the One still is: it happens to
  everyone on staff, on shift or not.
- **Cytheurgy is work on the world.** The Forms are the order the world runs by: why sleep restores, what a place
  gives back, what people can and can't do. A Cytheurgist wants to know how the world works, and where it fails the
  people in it wants the power to make it otherwise. To the CDU the Forms are code, so the craft is engineering:
  the world is the work, and engineering the means. The desk frame and the Stances given on hire say it in that
  order.
- **The lens.** A Cytheurgist's Shift memory is asked for as what the day showed about how the world works or fails
  people, and what they set moving. Commits, files and pull request numbers are the CDU's side and stay behind.
- **The CDU speaks of the world first** (its soul): what a Form does to the people under it, then how it's built.
  Something that happened to someone has a Form behind it too, and "not in the code" is an answer only after looking
  for that Form.
- **The day plan gets where the work stands**, not its commit subjects and pull request numbers.

## Considered

- **Keeping each reply as a memory, reworded.** Still dozens a day, and still the CDU's words, not theirs.
- **Putting the frame in the sheet.** It would be in every beat; ADR 0039 kept it out for that reason, and a memory
  surfaces only when it's relevant.
- **Writing the Recollection directly**, past the extractor. It would be the only memory without an Event. The
  extractor shortens it and keeps its sense.
- **A Stance about one's work, seeded.** Hands them the conclusion. The Shift memories are what the overnight pass
  forms a Stance from.

## Consequences

- One more General call per Persona per shift, and the usual two for the Capture when something is kept.
- People already on staff keep the Stances they were hired with; only new hires get the new wording.
- A reply that arrives after the shift is on the desk next morning but in no Shift memory.
- `Work_ShiftMemory` (bench) shows it: a day with something in it is kept, a day like any other is "none", and a
  Cytheurgist's memory names the world and no commit.
