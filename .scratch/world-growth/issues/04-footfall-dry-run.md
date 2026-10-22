# 04 Footfall ledger, dry run

Status: ready-for-agent
Type: task
Blocked by: 03

- New backend/World/Growth/: FootfallMath (pure, timestamps, nothing ticks) + GrowthGrain (key Party).
- Signals: anchors (read IOccupationGrain once on master; lived sheet lives-in), whereabouts beats, Capture concept tags matching a Locale.
- Gates: per-Participant cap 1.5, footfall ≥ 3, ≥ 2 Participants, ≥ 2 days. Log "would open X".
- Author view: Growing panel (Locales with footfall bars, contributors, the gate each one is missing).
- Bench probe: simulate a week of Days for a cast and show the growth curve.
