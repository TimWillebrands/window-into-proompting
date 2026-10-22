# 06 Asset catalogue from Poly Pizza (prototype first)

Status: resolved
Type: prototype

- Prototype: normalise 5 Kenney CC0 GLBs (desk, office chair, screen, office block, streetlight) into tile units,
  palette vertex colours, night glow; render them in the centre scene next to the primitive prefabs. Answer: does it fit?
- Then tools/catalogue: harvest (API search, License=1, creator whitelist) → gltf-transform normalise → annotate once
  (footprint, seats, tags, about) → review gallery → publish to prefabs.json + PREFABS (GLB source), self-hosted.
- API key: env only (POLY_PIZZA_KEY), never committed, never used at runtime. Downloads need a browser User-Agent.

## Answer (prototype)

Yes, it fits. Story `Places/Catalogue prototype` (PlaceScene.stories.tsx) shows 5 Kenney CC0 models next to the
primitive prefabs, by day and at night.

- Furniture (desk, desk chair, screen) sits naturally beside the primitives, and `$fabric`/wood remaps work.
- Buildings look cold as-is (grey, pale-blue windows). With the default window rule (warm, glows at night)
  and tinted facades (`_defaultMat` → `$brick`, `border` → `$trim`) they match the canal houses at night.
- Pipeline needed less than planned. Normalising happens client-side in `meshes.ts`: transforms baked, split
  by material, floored at the origin, uniform fit to the prefab footprint. That means no gltf-transform step.
  Up-front work per asset = curation + a prefab entry: `fit`, `seats`, material rules, tint slots and an
  `about` line in prefabs.json.
- Kenney material names are semantic (wood, metal, carpet, window, roof, door, border), so one shared rule
  table covers most models.
- Gotchas:
  - static.poly.pizza returns 403 without a browser User-Agent.
  - Servo has no WebGL; screenshot stories with Playwright Chromium (swiftshader).
  - Stories need `places.css` and a flex-column wrapper.

Next: tools/catalogue harvest script (search → shortlist → download → draft prefab entries for review).

## Answer (pipeline)

`tools/catalogue/harvest.mjs` (Node, no dependencies; key in `POLY_PIZZA_KEY`, never committed):
- `search`: runs terms.json per archetype through the API (CC0, creator whitelist, ≤ 3000 tris, not animated) and writes
  shortlist.json and shortlist.html (contact sheet; already-picked models outlined green). Both are git-ignored.
- `picks.json` is the reviewed source of truth: id, name and optional size/fit/seats/tint/mats/solid/about.
- `fetch`: downloads each pick to `frontend/public/models/<creator>/`.
  - It rejects textured models (the client draws flat colours only); Kay Lousberg's are all textured.
  - It measures the world-space box (node transforms applied). Size and fit are drafted from it: Kenney furniture ×2 to
    real scale, buildings fit a 3×3 footprint.
  - It names by creator prefix (k-, kl-, q-) and writes per-creator LICENSE.txt.
  - It generates `scene/catalogue.gen.ts` (spread into PREFABS) and rewrites the catalogue tail of backend prefabs.json,
    so the sync test holds.
- Review: story `Places/Catalogue gallery` lays out every catalogue model, by day and at night.

First catalogue: 28 Kenney models (office, café, bar, restaurant, shop, park, clinic, street), 744 kB, lazy-loaded per scene.
