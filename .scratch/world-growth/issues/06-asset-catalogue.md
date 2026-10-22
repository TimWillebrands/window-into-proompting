# 06 Asset catalogue from Poly Pizza (prototype first)

Status: ready-for-agent (prototype resolved)
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
