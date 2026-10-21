# Places: making, restyling and hanging out

> Status: accepted. Builds on ADR 0020 (Bodies and Places) and 0023 (scenes and prefabs). Economy and
> the Life choices that use this are ADR 0024 (proompting-84). Code: `backend/World/Places/`,
> `frontend/src/apps/places/`.

ADR 0023 made Places look good, but content was still hand-written. Bodies could only stand or sit,
and a look was six palette picks chosen once. This ADR adds the groundwork for the World to grow by
itself and for Bodies to hang out the way people do.

## Decision

- **Generated things.** A thing (`Furni.Model`) can have a 3D model the LLM builds from coloured
  primitives (the prefab part format). `ThingModel.Clean` makes any output safe: at most 28 parts,
  sizes clamped to one tile, hex colours, known shapes only. Decorating and `AddThingAsync` queue a
  model. The kind's prefab stands in until it lands, and stays if it never does.
- **Generated Places.** `CreatePlaceAsync` asks for spots plus a scene (ASCII map + props from the
  shared catalogue). `PlaceDesign.Check` validates it: known chars and prefabs, props in bounds,
  a way in, every spot reachable with room for its capacity. Failures go back to the model, up to
  three tries. The scene is stored on the Place (`CustomScene`), and the client falls back to a
  generated room if it can't draw it. There's a cap of 12 per world. The author can remove them,
  and anyone inside walks home.
- **Shared prefab catalogue.** `backend/World/Places/prefabs.json` (name, footprint, solidity,
  seats, a one-line description) prompts the designer and validates designs. A frontend test keeps
  it identical to `PREFABS`.
- **Appearance v2.** The palette now covers top and bottom styles, shoes, facial hair, build,
  height and eyes, plus more hair and accessories. `RestyleAsync` maps words ("dye my hair pink")
  onto the palette, and only palette values are accepted. The options API is a field list, so
  editors and the Wardrobe follow new fields automatically.
- **Hang-out poses (IMVU-style).** A spot lists the poses it affords (lean, lounge, perch, dance,
  lie, cross-legged). Think and wave work anywhere. Pair poses (hug, slow dance, high five, hold
  hands) need someone at your spot. Slow dance only works where dancing does, and hugs and high
  fives wear off after 20 s. A pair pose takes both Bodies, and walking off ends it for both.
  Situation says it in plain words ("Denise (right next to you, hugging you on the bridge)").
  - **Split:** solo poses come from Places (the go-to-spot intent, or "get comfortable").
    Pair poses are relationship acts, offered by proompting-84's Life apps and gated on Ties.
- **Arranging.** Owners move and turn things (`TileX`/`TileZ`/`Rot`, null hands the thing back to
  the scene), take decor down, and pick wall and floor colours. The author (`Guid.Empty`) can do
  this anywhere.

## Consequences

- Moved furniture doesn't block walking yet: the client's walk grid comes from the scene alone.
- Generated content costs one General-tier call per thing and up to three per Place. Economy
  (ADR 0024) is what makes it scarce for Personas.
- Spot ids from a design are slugged and capped at six. Spots stay canonical, as in ADR 0020.
