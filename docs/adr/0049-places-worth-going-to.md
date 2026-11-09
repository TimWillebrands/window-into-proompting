# Places worth going to: models as data, and things to do

> Status: accepted. Builds on ADR 0023 (scenes and prefabs), 0025 (making) and 0046 (Player MCP).
> Code: `backend/World/Places/{Library,ThingModel,PlaceDesign,Doing}.cs`, `frontend/src/apps/places/scene/`, `tools/places/library.py`.

A made Place was scenery. The designer got ~95 prefabs and seven shapes, one tile per thing, and nothing in the design
could be *used*: only the hand-seeded coffee machine and jukebox did anything. A weak model failed the checker on a
misspelt prefab; a strong one had no room to do better than the weak one. And a Persona had no reason to go to an empty
Place, or to come back to one.

## Decision

- **Models are data.** One part format, in JSON, for the library, for a scene's own models and for a made thing:
  the old shapes plus dome, capsule, wedge, pyramid, lathe, extrude and tube; `group` and `use` (another prefab as a
  part); `array`, `spin` and `mirror` instead of retyping; `tag` for motion (spin, bob, sway, pulse, flame, twinkle);
  named palette colours; and per model a footprint up to 3×3, seats, a light and tints. `ThingModel.Clean` keeps any
  output safe: 64 parts as written, 320 drawn.
- **The library is one file**, `backend/World/Places/library.json`, written by `tools/places/library.py` and copied to
  the frontend (a test keeps the copy equal). It holds the palette, the models, the catalogue's groups, and what things
  afford. Code prefabs (`prefabs.ts`) stay for what needs state or randomness, and win a name clash.
- **Sets** are models that `use` others: `dining-set`, `lounge-set`, `bar-set`. One prop is a whole Spot.
- **A scene says more with less**: twelve more ground letters (carpet, tiles, stage, sand, path, cobbles, deck, pond,
  flowers, hedge, fence, snow), `floorStyle`, `wallStyle`, `accent`, a `light` mood that holds whatever the hour, an
  `ambience` in the air, and `models` of its own. All optional; a map, spots and props is still a Place.
- **The checker mends before it refuses.** A near-miss prefab name, a thing on a wall tile, a Spot on top of furniture
  and an unknown style are fixed and noted. What's left comes back with a did-you-mean and the map with props lettered
  and Spots numbered. `check_place` runs it without changing anything.
- **Features: things to do.** A prop with `do` and `gives` becomes a Furni of kind `feature`, pinned by the scene.
  Catalogue things carry a default (a piano, a fountain, a telescope), so placing one is enough; five per Place.
  A made thing in a Home can carry one too.
- **A Take is what someone comes away with.** Doing a Feature asks the Muse (a stateless grain, never the Places grain)
  for one concrete line from the Feature's `gives`, the hour, the doer's reason and the last few Takes there. The world
  writes a find; when the Feature `leaves`, the Persona writes it in their own voice, and it stays for the next visitor
  to read. A Player may type their own.
- **What a Persona keeps**: the Take is an activity others see, an Opener to bring up, a small lift in mood, and a
  Moment with the Place's id, so being back there brings it to mind. Not offered again to them for 20 hours.
- **What pulls them there**: "To do here" in the Situation, one Feature at their Spot and one across the room as
  Affordances (what someone left is the hook), and an empty Place elsewhere is offered with what there is to do there.

## Consequences

- One General-tier call per Take. The 20-hour rest and one-or-two offers per turn keep it rare.
- Takes are invented facts about the Place (a record in the crates). They're small and nobody is held to them; the
  last five steer the next away from repeating.
- Existing Places get Features on activation: the Lobby's noticeboard, the Café's chalkboard, the centre's herring
  cart and ducks by seed, designed Places from what their scene already holds.
- The Places look changed for everyone: planked floors, skirting, daylight through windows, a gradient behind the room.
- Keepsakes (owning what you found) are not done: the Wallet only knows the clothes catalogue.
