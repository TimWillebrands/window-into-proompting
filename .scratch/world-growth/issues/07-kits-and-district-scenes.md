# 07 Kits and procedural District scenes

Status: ready-for-agent (kit scenes resolved; District scenes open)
Type: task
Blocked by: 05, 06

- Archetype kits: validated ASCII templates with slots (sign, tints, seed, prop swaps, spot names). Pass PlaceDesign.Check by construction (test).
- District scene: street grid + signed house fronts per opened venue, wired to entrances. 0 LLM calls.
- Kit-drawn when a Sketched Place has had 2+ Bodies there at once on 2+ occasions.

## Answer (kit scenes)

- Kits moved to `backend/World/Places/kits.json` (embedded resource), shared with the client like prefabs.json.
- `frontend/src/apps/places/scene/kits.ts`: one scene per archetype (11), furnished mostly with catalogue models.
  `sceneFor` draws an opened Place from `KIT_SCENES[place.archetype]`.
- Test (scene.test.ts) reads kits.json. Every kit has a scene, every kit spot is pinned with room for its capacity and is
  reachable from the door, and every prop exists.
- Story `Places/Kits` shows each.
- Kit-drawn is no longer a separate rung (ADR 0027 updated): kit scenes cost nothing, so Openings use them directly.
- Open: procedural District scenes (street grid + catalogue buildings + venue doors).
