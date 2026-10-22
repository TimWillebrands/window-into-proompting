# 03 Day whereabouts and a closed vocabulary

Status: resolved
Type: task
Blocked by: 01

- Add output field `whereabouts` to the Day call: "HH:MM-HH:MM | place name" per beat away from home.
- Add input line "Places you go: …" (own anchors + District top 5) with a `new:` escape.
- Coordinate: proompting-75 adds a "Fixed today: …" input line to the same call (shifts). Don't touch it.
- Bodies go away for beats of 2 h or more when they're not mid-conversation; they come back at the end of the beat.

## Answer

- Day call: output `whereabouts` ("HH:MM-HH:MM | place | district"), input "Places you go: …" from
  `IPlacesGrain.GetKnownPlacesAsync` (Places in your District, Locales you've been to, the busiest Locales).
  Parsed by `SelfMath.ParseWhereabouts` (home, wraps and junk dropped; `new:` prefix stripped).
- After the Day is written, a detached `ApplyWhereaboutsAsync` resolves each stretch via `LocaleInAsync(name, pid, district)`.
  Stretches overlapping `FixedWindowsAsync` (Occupation shifts; empty until proompting-75's branch fills it) are dropped.
- Places `WhereaboutsCycleAsync` (every 20 s, beside sleep). It reads the plan detached, every 30 min, from Self's
  [AlwaysInterleave] `GetWhereaboutsAsync`. For a stretch ≥ 2 h: go once (`AwayStretch`), never from company or at night, AwayAs "at work"/"out".
  It comes back only if still at the Locale the Day sent it to (`AwayByDay`); a shift's GoAwayAsync clears that.
- Pure decision `PlacesGrain.ErrandStep` is unit-tested (WhereaboutsTest). Not covered end-to-end: Self uses wall-clock time.
