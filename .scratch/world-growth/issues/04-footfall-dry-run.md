# 04 Footfall ledger, dry run

Status: resolved (bench probe open)
Type: task
Blocked by: 03

- New backend/World/Growth/: FootfallMath (pure, timestamps, nothing ticks) + GrowthGrain (key Party).
- Signals: anchors (read IOccupationGrain once on master; lived sheet lives-in), whereabouts beats, Capture concept tags matching a Locale.
- Gates: per-Participant cap 1.5, footfall ≥ 3, ≥ 2 Participants, ≥ 2 days. Log "would open X".
- Author view: Growing panel (Locales with footfall bars, contributors, the gate each one is missing).
- Bench probe: simulate a week of Days for a cast and show the growth curve.

## Answer

- `World/Growth/FootfallMath` (pure): weights anchor 0.5, day 0.5, talk 0.2; 3-day half-life; per-person cap 1.5;
  gates footfall ≥ 3, ≥ 2 people, ≥ 2 days, reported as the first missing gate ("needs 1 more person"). One signal per
  (Locale, person, source, day).
- `GrowthGrain` (key Party) keeps 15 days of signals, logs "World growth (dry run): would open …" once a day per Locale,
  and opens nothing.
- Signals wired:
  - day: Self, after resolving whereabouts.
  - talk: Places, whole-word Locale names in any message; generic "X's work" doesn't count.
  - anchor: `IGrowthGrain.NoteAsync(locale, pid, "anchor")` is open to Occupation (proompting-75) to call once per shift day.
- `GET worlds/{id}/growth` returns rows for Locales only. The Navigator shows a "Growing" pane in All-ears (author) mode.
- Open: the Bench probe (a simulated week of Days) needs a Bench Session with a live model.
