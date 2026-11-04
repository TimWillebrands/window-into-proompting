# Figures: people as documents an LLM can write

> Status: accepted. Replaces the look half of ADR 0025 (Appearance v2) and the "no art assets" rule of ADR 0023 for people.
> Code: `frontend/src/apps/places/figure/`, `backend/World/Places/FigureDesign.cs`.

People were boxes: 13 palette picks on a hard-coded blocky mesh. An LLM could choose from the menu but nothing new could exist,
and it looked like Minecraft. The aim is Sims 2 / IMVU fidelity with Habbo's readability, 2000s alt scenes included, with every
aspect of a person writable by an LLM.

## Decision

LLMs can't sculpt meshes or animate bones. They can write numbers, names, hex colours, SVG and lists of blobs. So a Figure is a JSON
document in exactly those terms, and the client compiles it to a skinned mesh.

- **Signed distance fields are the modelling language.** Bodies, clothes, hair and accessories are blended fields polygonised by
  surface nets (`nets.ts`). Smooth blending forgives coarse numbers, there is no topology to get wrong, and an offset of the body
  field is a garment that fits any body. That last part is why we don't use a third-party base mesh: no body field, no fitted clothes.
- **Each aspect has the medium an LLM is good at.**

  | Aspect | Medium |
  | --- | --- |
  | Body | sliders on a radius table (`anatomy.ts`) |
  | Face | named shapes and sliders, painted to a canvas atlas with blink and talk frames (`face.ts`) |
  | Hair | zones of locks, grown with gravity and skull collision, sculpted into one solid (`hair.ts`) |
  | Clothes | cut numbers (sleeve, hem, fit, flare) over the body field (`garments.ts`) |
  | Surface | patterns, plus SVG prints on Roblox-style UV panels (`paint.ts`, `unwrap.ts`) |
  | Accessories | blobs hung on named sockets (`extras.ts`) |
  | Posture, gestures | pose words in degrees, keyed into moves (`pose.ts`, `moves.ts`) |

- **Third-party motion, own mesh.** The rig and walk, sit and dance clips are Quaternius' Universal Animation Library (CC0), baked by
  `tools/figure/bake-rig.mjs`. The Figure is built around that skeleton, so clips apply unchanged at any proportions. The mesh is bound
  in an A-pose for better shoulders; clips keep their T rest.
- **Look: big-headed fashion doll.** About 1:4.2 head to body, because faces must read at 80 px. Toon ramp, baked occlusion from the
  fields, gloss, and a screen-space ink line that gives the pixel look its Habbo edge. Everything is drawn at one detail level, because
  at half resolution finer meshes and fingers don't show.
- **Bodies differ on purpose.** Seven body types (straight, athletic, lanky, stocky, heavyset, pear, hourglass) are palette picks that
  set the sliders, and the LLM adjusts from there. An hourglass has to be asked for: `curve` draws nothing under about 0.2, a bust
  only from about 0.3, and thigh width follows the hip line. Left at fixed radii, every body drifted to an hourglass. LLMs read
  `curve` 0.5 as "average", so the prompt spells out what it means.
- **`Appearance` keeps its palette picks and gains `Design`.** Picks still drive shops, prompts and the 2D fallback, and every palette
  value has a recipe in `dress.ts`. `Design` lays over them slot by slot (body, face, hair, top, under, outer, bottom, legwear, shoes,
  extras, stance, moves). A pick replaces the slots it touches, so buying a hoodie takes the designed top off. A dye keeps the cut.
- **`FigureDesign.Clean` makes any output safe**, as `ThingModel.Clean` does for things: clamped numbers, known names, capped counts.
  SVG goes through an element and attribute allow-list, and is only ever rasterised through an `<img>`, which can't run or fetch anything.
- **Two steps of generation.** One call writes the doc, with a one-line `brief` per print. One call per print draws the SVG. Keeping SVG
  out of the JSON keeps the JSON parseable, and a failed print is simply left off.
- **Compiling happens in workers** (`runtime.ts`), cached by doc. The last Figure stays up until the next is ready.

## Migration

A Persona with no `Design` is dressed once more, told what people know them as, so they stay recognisable. Until then, and for every
Player, the palette recipes draw them.

## Consequences

- A Figure costs 25-40k triangles in a scene, with a second pass for the ink line, and 0.2-0.6 s of worker time to compile, once per
  doc. Nothing is drawn but a shadow and a name until then. A crowd level of detail is the next lever.
- Every Persona is dressed again on the first run after this lands: one design call and up to three print calls each.
- `doc.ts`, `FigureDesign.cs` and the prompt must agree. `figure.test.ts` checks that every backend palette value has a recipe.
- Designed garments aren't Wardrobe items yet: change out of one and it's gone. Making them ownable is the natural next step.
- Adding a garment idea rarely needs code. The LLM can write it; a new shape of thing needs a field.
