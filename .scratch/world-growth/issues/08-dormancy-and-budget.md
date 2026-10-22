# 08 Dormancy

Status: resolved
Type: task
Blocked by: 05

- No visits for 7 days and footfall < 1 → Dormant: out of Life lists and the Navigator's main list, never deleted.
- Any new signal wakes it.

## Answer

- Dormancy is derived on each snapshot (`Place.Dormant`). A Place opened from a Locale goes quiet when:
  - it opened ≥ 7 days ago;
  - nobody has a visit day there in the last 7;
  - nobody named it in talk in the last 7 (`MentionedAt`).
- Dormant Places drop out of Life's place lists and move to the Navigator's "Quiet lately" group. A visit or a mention
  wakes them; nothing is deleted.
- Seeded and Made Places never go dormant.
- The growth budget (one automatic Opening per world-day) shipped with 05.
