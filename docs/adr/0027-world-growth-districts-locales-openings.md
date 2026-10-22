# World growth: Districts, Locales and Openings

> Status: proposed. Locales are built (slice 1). Builds on ADR 0020 (one Body, one Place), 0021 (Day), 0023
> (scenes, prefabs), 0025 (made Places) and 0026 (Occupations). Spec and issues: `.scratch/world-growth/`.

The drawn world is three public Places plus Homes, and a Persona's Day is prose about somewhere else: "09:00
tram to the office in Zuid". The Body stayed at the Café. Making a new Place cost up to three General calls
(`CreatePlaceAsync`) and only the author could ask for one. Nothing let the world grow from what its people do.
Growing on every mention would be just as wrong: one Persona's idea shouldn't redraw the city.

## Decision

**Fidelity follows use.** Draw where people meet, not where people are mentioned. A location climbs a ladder,
and each rung is earned:

| Rung | What exists | Cost |
| --- | --- | --- |
| Locale | a named offscreen Place; Bodies can be away there | 0 calls |
| Opened | spots, purpose and a kit scene of its archetype (office, café, bar, park…) furnished from the catalogue | 0 calls (System One only if the name doesn't say) |
| Bespoke | `CreatePlaceAsync` design, seeded with the kit map | author only |

Sketched and Kit-drawn started as two rungs. Kit scenes turned out to cost nothing at runtime, so an Opening goes
straight to its kit scene. The generated room is only for archetypes without one.

1. **Locales** (built). A Place with `Kind = "locale"`: one spot, no talk Room, never drawn, not offered as
   somewhere to go, excluded from Earshot and co-presence. `GoAwayAsync` walks a Body out (`AwayAs`, e.g. "at
   work"), `ReturnAsync` brings it back, bedtime brings it home. `LocaleAsync` resolves names; "work" for
   someone is their own. Opening a Locale keeps its id, so Events captured there keep their `place_id`. This
   amends ADR 0020: a Body is in exactly one Place, and that Place may be a Locale.
2. **Districts.** `Place.DistrictId`. The old centre is the first District. Within a District walking stays
   60 s and shouts carry next door. Between Districts it's a tram ride of a few minutes. Life's place list
   shows your District, your anchors and where your Ties are; other Districts collapse to one line. This
   comes before any growth, so prompts don't grow with the city.
3. **Anchors.** A Participant's standing tie to a Locale: lives, works, studies, regular. "Works" is read from
   the Occupation (ADR 0026), never stored twice. "Lives in" is read once per persona and moves their Home into that District. "Regular" comes from
   visit days.
4. **Footfall.** Decaying pressure on a Locale, with a 3-day half-life:

   | Signal | Source | Weight |
   | --- | --- | --- |
   | anchor | Occupation, lived sheet | 0.5 per day while held |
   | a Day beat spent there | `whereabouts` field on the Day call | 0.5 |
   | named in captured talk | Capture concept tag matches a Locale | 0.2, once per speaker per day |

   - **Each Participant contributes at most 1.5.** That is what stops one Persona from changing the world.
   - **Opening needs three gates:** footfall ≥ 3, from ≥ 2 Participants, across ≥ 2 days.
   - **Demand opens without the gates.** A concrete plan needs a place:
     - the Player visiting someone there (always);
     - a Persona walking over to someone there (FollowThrough included), but only if someone is a regular there
       (2+ visit days).
     A Gathering at a Locale is not built yet.
   - **District scene:** two or more opened venues in it, or anchors from three or more Participants.
   - **Budget:** at most one Opening per world-day, kept by Places for every reason except the author's. That covers
     footfall and visits; the Player's own visit counts too.
   - **The author** can open any Locale from the Growing panel, or close (veto) any Opening.
   - **Dormant:** an opened Place with no visits and no mention in talk for 7 days. The Place stays, out of Life's
     lists, and a visit or a mention wakes it. Nothing is deleted.
   - **One presence a day:** an anchor and a Day stretch for the same person, Locale and day count once.
5. **Cheap generation.** Generation is choosing, not authoring.
   - **Signals ride existing calls.** `whereabouts` is added to the Day call and anchors to the lived-sheet
     distillation. No new calls.
   - **A closed vocabulary.** The Day prompt gets "places you go" (your anchors plus the District's top 5),
     with a `new:` escape. Names converge on shared places.
   - **Classification, not design.** System One picks the archetype (office, bar, shop, park, station, gym,
     market, flat block).
   - **Kits.** Archetype scene templates, validated ahead of time, so there is no retry loop. Districts are
     procedural: a street grid with signed house fronts for their venues.
   - **The asset catalogue.** Prefabs can be CC0 GLB models as well as primitives. They are curated offline
     from Poly Pizza (Kenney first), normalised to tiles and the palette (vertex colours, night glow, `$tint`
     slots), annotated once (footprint, seats, tags) and self-hosted. A thing's description then matches the
     catalogue by tags, with System One picking among the top 5. `ThingModel` is the fallback for misses, and
     misses are logged so curation follows demand.
6. **The city is loosely Amsterdam.** The centre already has canals and a herring cart, and the model knows the
   real districts (Zuid: office towers, the station, Beatrixpark), which is flavour for free.

## Why

- Breadth, persistence and a per-Participant cap stop single-Persona thoughts while still letting shared
  life through. They are the same kind of guard as stale words and the warmth caps in ADR 0021.
- A Locale is the cheapest true thing: it makes the Day and the Body agree before anything is drawn.
- Search results on Poly Pizza are noisy ("whiteboard" returns a cutting board, "tram" a cargo train), so
  picking assets at runtime would be wrong. Curating up front makes runtime cheap and safe.

## Consequences

- Clients get Locales in the snapshot and must not draw them (the Navigator lists who's away).
- The Life prompt is scoped by District; the "Elsewhere —" line no longer lists every Place.
- Thresholds are first measured in a dry run (log "would open X", plus the author's Growing panel) and tuned
  on the Bench before anything opens.
- `ThingModel` and `CreatePlaceAsync` remain as fallbacks; their call volume should drop.
