# Lived self: lifelikeness metrics

`tools/lived-self/snapshot.py` appends a snapshot of a running World to `metrics.jsonl`. The snapshot is
computed over the last 80 persona lines across Rooms, DMs and the Board (see ADR 0021).

| When | Avg words | Stage directions | Name openers | Self-repeats | Worn words |
| --- | --- | --- | --- | --- | --- |
| Before (08:00) | 81–93 | 42–92% | (not measured, ~55%) | — | lantern, mōshiwake, verses, load-bearing |
| 14:10 | 20 | 0% | 17% | 6% | tin, roof, lid, shelf (callbacks fading after stale recall) |

What to look for when tuning: worn words should be everyday nouns that rotate, not one storyline. Name openers
should stay under ~20%. Repeats should stay under ~10%.
