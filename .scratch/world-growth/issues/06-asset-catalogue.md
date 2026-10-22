# 06 Asset catalogue from Poly Pizza (prototype first)

Status: ready-for-agent
Type: prototype

- Prototype: normalise 5 Kenney CC0 GLBs (desk, office chair, screen, office block, streetlight) into tile units,
  palette vertex colours, night glow; render them in the centre scene next to the primitive prefabs. Answer: does it fit?
- Then tools/catalogue: harvest (API search, License=1, creator whitelist) → gltf-transform normalise → annotate once
  (footprint, seats, tags, about) → review gallery → publish to prefabs.json + PREFABS (GLB source), self-hosted.
- API key: env only (POLY_PIZZA_KEY), never committed, never used at runtime. Downloads need a browser User-Agent.
