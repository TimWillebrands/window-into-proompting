# RFC: dressing for work

> Status: accepted and built as [ADR 0058](../adr/0058-work-clothes.md); where they differ, the ADR holds.
> Builds on ADR 0024 (Wardrobe), 0026 (Occupations and shifts), 0028 (Figures), 0030 (Companies), 0037 (Players on auto).
> Vocabulary in `CONTEXT.md`.

## The case

Eiko Tanaka is the priestess of the Small Shinto shrine. On shift she stands by the pond in a purple band tee, a white
skirt and grey sneakers. That is everything she owns: her Wallet's Wardrobe was seeded from what she wore
(`WalletGrain.cs:80`), and that came from her bio, where the dresser read "a white hakama over plain streetwear" and
could only say *band tee* and *skirt* with the palette. Her Design has no `top` or `bottom` slot left, so nothing
draws a hakama either.

The cause is wider than her: a look is one thing, worn all day. Nothing in a shift touches clothes
(`Workplaces.ApplyAsync`), and nothing in the Wardrobe or the Initiative knows what a job asks you to wear. Leaving it
to a Life choice won't fix it: an Affordance is weighed, and a Persona can skip it every morning.

**Acceptance:** when Eiko's shift starts she is in shrine dress (white kosode, hakama, tabi and sandals), with her
own face, hair and sketchbook. When it ends she is in her band tee again, drawn exactly as before.

## Proposal

### Work clothes

**Work clothes** are what a job has you wear: a garment layer that goes over your own look while you're on shift.
They belong to the **Occupation**, so they come and go with the job and are Party-scoped like it.

```
Occupation.Clothes: WorkClothes?   // null = not designed yet
WorkClothes {
  Code: none | dress     // none: you come in your own clothes
  Picks: {topStyle, top, bottomStyle, bottom, shoeStyle, shoes, legwear, accessory}   // garment picks only
  Design: {top, under, outer, bottom, legwear, shoes, extras}                         // garment slots only
  Summary: "white shrine robes and a vermilion hakama"
}
```

`Code = none` is a real answer and the expected one for many jobs: a writer, most office work, a Cytheurgist at the
desk. The point is the shrine, the café apron, the nurse, not everyone in a costume.

`WorkClothes.Clean` keeps only garment picks and slots. Body, face, hair, stance and moves can't be in it, so changing
for work never changes who you are.

### Changing is a shift step, not a choice

- **Start** (`ShiftStep.Start`, before `GoAsync`): if `Code = dress`, the Body puts the work clothes on.
  Activity: "changed into her work clothes".
- **End** and **LeaveEarlyAsync**: change back.
- **Job changed or removed** while wearing them (`OccupationGrain.SetAsync`): change back.
- **Missed** shifts change nothing.

Deterministic, no LLM call at the moment of changing. That is what makes the Eiko case pass every day.

### The own look waits on the Body

`Body.OwnLook: Appearance?` holds the look to change back into. It is set when work clothes go on and cleared when they
come off. Restoring is a copy, not a re-merge, so the Design comes back byte for byte. That is what ADR 0028 left open
("change out of one and it's gone"), solved for this one case.

Putting on is `Merge(own, clothes.Picks)`, then `Design = Over(Without(own.Design, changed), clothes.Design)`, with
one exception: work `extras` are **added** to your own (capped at `MaxExtras`), not swapped in. Eiko keeps her
sketchbook and can gain a gohei.

### While in work clothes, the Wardrobe works on what you'll change back into

- `wear` isn't offered on shift.
- `buy` still pays and owns, but goes onto `OwnLook`: "bought a green hoodie for after work".
- Restyle, admin `dress` and `RedressAsync` aimed at a Body in work clothes edit `OwnLook`. `RedressAsync` also clears
  `Occupation.Clothes`, so the next shift designs them again.
- `AdoptLooksAsync` (moving World) adopts `OwnLook ?? Appearance`, so nobody moves in stuck in a uniform.

### Designing them

One LLM call per job, from `FigureDesign.WorkPrompt`. It is given:
- name, bio, job title, workplace, the Place's description if the job is onscreen, the Company's Mandate if any
- their current look, picks and Design: "keep it recognisably them"
- the rule: say `none` when this person would come in their own clothes; dress only where the job or place clearly
  asks for it (vestments, uniform, apron, protective gear). A dress code that's only "smart" is at most a blazer over
  their own top.

It answers `{code, summary, picks, design}`. Prints go through `DrawPrintsAsync` as usual.

When: once the job is set or seeded (`OccupationGrain.SeedAsync` / `SetAsync`), detached like `PlacesGrain.DressAsync`.
If a shift starts before they're ready, they come in their own clothes and change as soon as the design lands,
provided they're still on shift.

Cost: one design call and up to three print calls per job, once. A changed title or workplace designs them again.

### Players

A Player's look is theirs (`Appearance.Kept`), so nothing changes them unasked, on auto or not.
- The Wardrobe app gets a **Work clothes** panel when they have a job: a preview, "Design my work clothes" (free
  text, the same call), and a toggle "Change for work" (off by default). With it on, the shift steps above apply to
  them.
- MCP: `get_look` returns `workClothes`, and `dress` takes `forWork: true` to write them instead of the look.

### What a Persona perceives

- Wardrobe: "Wearing: your work clothes (white shrine robes and a vermilion hakama)." and "After work you'll change
  back into: …".
- Others see the Summary through Places as with any look. Nothing new in "Right now".

## Not now

- **Company uniforms.** Staff of one Company (ADR 0030) sharing a uniform, the café's aprons, is a natural second
  step: `Company.Clothes` as the base, an Occupation's as the override. Left out so v1 is one owner and one call.
- **Changing at home.** Changing happens at Start, wherever they are. Walking over in your own clothes and changing in a
  back room is nicer, but nobody would see it.
- **Owning designed garments in general.** `OwnLook` handles the round trip. A Wardrobe of designed pieces is still
  ADR 0028's open step.
- **Eiko's bio.** It says she wears the hakama over streetwear every day. Once work clothes exist, that sentence
  should become "in shrine dress on duty", and she can be redressed for off duty.

## Tests

Backend (`backend-test/WorkClothesTest.cs`), with the LLM stubbed:

1. **Eiko goes to work dressed for it.** Occupation priestess at the shrine, `Code = dress`, shift Start: her Body
   wears the hakama picks and Design slots, keeps her face, hair and body slots, and her extras hold both the
   sketchbook and the work extra.
2. **She comes back as she was.** End: `Appearance` equals the look from before Start, Design string identical.
3. Leave early changes back. Job removed mid-shift changes back. A missed shift changes nothing.
4. `Code = none`: Start changes nothing, and no `OwnLook` is set.
5. Buying on shift: the item is owned and on `OwnLook`, and the work clothes stay on. After End they wear it.
6. Not designed yet at Start: own clothes, then work clothes once the design lands; not if the shift has ended.
7. A Player on auto without "Change for work" never changes; with it on, they do.
8. `WorkClothes.Clean` drops body, face, hair, stance, moves and non-garment picks.
9. `AdoptLooksAsync` from a Body in work clothes adopts the own look.

Prompt check against real models (`tools/` script, not CI): Eiko gives shrine dress; a barista gives an apron over
their own clothes; an accountant gives `none` or a blazer; a Cytheurgist gives `none`; a nurse gives scrubs. Run once
across the World's current jobs, and look at how many come back `none`. If it's under about half, the prompt is
dressing the theme, not the person.

## Code touched

| Where | What |
| --- | --- |
| `World/Work/Shifts.cs` | `Occupation.Clothes` (`[Id(9)]`), `WorkClothes` record |
| `World/Work/OccupationGrain.cs` | design on Set/Seed; change back on Set when the job goes |
| `World/Work/Workplaces.cs` | put on at Start, take off at End and leave early |
| `World/Places/PlacesModels.cs` | `Body.OwnLook` |
| `World/Places/PlacesGrain.cs` / `.Making.cs` | `ChangeForWorkAsync`, `ChangeBackAsync`; Restyle, Dress, Redress, AdoptLooks respect `OwnLook` |
| `World/Places/FigureDesign*.cs` | `WorkPrompt`, garment-only clean, extras added rather than replaced |
| `World/Economy/WardrobeApp.cs` | perception lines, no `wear` on shift, `buy` onto `OwnLook` |
| `Controllers/`, `Mcp/PlayerTools.cs`, `frontend/src/apps/wardrobe/` | Player panel, toggle, `forWork`; then `pnpm api-generate` |
| `CONTEXT.md` | **Work clothes**, and under Appearance: "the own look waits while you're in them" |

Serialization: `WorkClothes` uses `Dictionary` and `string?`, no collection expressions.

## Open questions

1. Is changing at Start right for onscreen jobs, or should they change on arriving at the Place? Arriving is more
   honest, but if a Body never gets there, nothing changes.
2. Can the Figure doc draw a kosode? It has `neck: v` with trim and `outer` with `open`, but no crossed wrap. Hakama is
   `bottom {kind: pants, length 1, fit 0.9, flare 1, rise 1, pleats}`. Check with `dress` on Eiko before building. A
   `wrap` neck may be needed.
3. Default for Players on auto: off (as proposed), or on once they've designed work clothes?
