# Places in 3D: scenes from ASCII maps and prefabs

> Status: accepted. Builds on ADR 0020 (one Body, one Place). Code: `frontend/src/apps/places/scene/`.

The 2D canvas drew every Place as the same 10×8 room with emoji furni. The
Places looked alike, and adding one meant writing drawing code.

## Decision

- **three.js via react-three-fiber** (+ drei). It's an orthographic camera at Habbo's 2:1 angle,
  low-poly flat-shaded geometry, a pixel look (half-resolution canvas, `image-rendering: pixelated`,
  toggleable), local-time day and night, shadows, and animated water. It's lazy-loaded, and without
  WebGL2 the old canvas still draws.
- **No art assets.** Everything is coloured primitives (box, cylinder, cone, ball, gable, ring,
  sign). Each prop is baked into a few merged meshes (plain, always lit, lit at night), so a
  city of ~60 houses stays cheap.
- **A prefab** is a named list of parts in `prefabs.ts`. It has a footprint, seats, an optional
  light, `$tint`s and a seeded variant, and it can be a function of furni state (a plant browns,
  cups pile up, a jukebox lights up). Add one and use it by name anywhere.
- **A scene** is an ASCII map plus props by prefab name (`scenes.ts`). The map legend covers floor,
  walls, windows, doors, water, streets, quays, bridges, grass, houses, trees, lamps, bollards and
  bikes, and scenes can add letters (e.g. a house with a "CAFÉ" sign). Props tagged with a spot lend
  it their seats.
- **Spots stay canonical.** A scene only pins the backend's spot ids and furni ids to tiles. Unpinned
  furni (decor) goes on free back walls or floor. A Place nobody drew gets a generated room with a
  prop per spot, guessed from the spot's name.
- **`Place.Scene`** names the scene (`lobby`, `cafe`, `centre`, `home`). Homes vary per owner in
  wall, floor and fabrics.
- **Outdoors is just a scene.** The city centre is a public Place with two canals, bridges,
  quays, canal houses and a little park. Each Place that opens onto it gets a door there
  (`entrances`), and Bodies walk in and out of the matching door.
- Walls and houses in front of something walkable are cut down to a stub so the camera sees in.

## Adding content

1. A thing: add a prefab to `PREFABS`.
2. A Place: add its spots in `PlaceSeeds` (backend, with `Scene`), draw a map in `SCENES`, and pin
   its spots and furni. `scene.test.ts` checks that every seeded spot exists, has room for its
   capacity, and is reachable from the way in.

## Consequences

- The scene code and `PlaceSeeds` must agree on spot ids (the test lists the seeded ones).
- three adds a ~270 kB gz chunk, loaded only when Places opens.
- Earshot, Situation and sleep are unchanged: the renderer is presentation only.
