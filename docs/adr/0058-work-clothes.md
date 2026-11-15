# Work clothes: dressed for the job, then yourself again

> Status: accepted. Builds on ADR 0024 (Wardrobe), 0026 (Occupations), 0028 (Figures), 0037 (Players on auto).
> Vocabulary in `CONTEXT.md`.

Eiko, the shrine's priestess, stood at the pond on shift in a purple band tee and sneakers. A look was one thing worn
all day. No shift step touched clothes, and leaving it to a Life choice would let her skip it every morning.

## Decision

- **Work clothes belong to the Occupation** (`Occupation.Clothes`, `WorkClothes`): garment picks, garment Design slots
  (top, under, outer, bottom, legwear, shoes, extras), a summary, and the title they were made for. `OwnClothes` means
  the job asks nothing, and it's the expected answer for many jobs. `WorkClothesRules.Clean` drops everything else, so
  changing for work never touches face, hair, body or stance.
- **Changing is a shift step.** `Workplaces` puts them on at Start and takes them off at End and on leaving early.
  Losing the job or getting new clothes mid-shift changes at once (`OccupationGrain.DressForShiftAsync`). Missed
  shifts change nothing. No LLM call at the moment of changing.
- **The own look waits on the Body** (`Body.OwnLook`, `Body.WorkClothes`). Changing back is a copy, so the Design
  comes back exactly. Every look change in Places goes through `SetOwn`: buying, wearing, restyling, `dress` and the
  bio dresser change the own look and show after work. Moving World adopts the own look.
- **Work extras join yours.** Eiko keeps her sketchbook and gains a gohei; extras are capped at `MaxExtras`.
- **Designed once per title, detached.** `OccupationGrain` asks `FigureDesign.WorkPrompt` when a job has none, and
  retries every 10 minutes on failure. Someone whose shift starts first comes in their own clothes and changes when
  the design lands, if they're still on shift. Same title, same clothes (`WorkRules.CarryClothes`).
- **Players are never dressed unasked.** The World designs only for Personas. A Player designs theirs from their own
  words (Wardrobe → Work clothes, `POST worlds/{id}/jobs/{participantId}/clothes`) and stops with
  "Come in my own clothes" (`DELETE`). Designing them is the opt-in, so there's no separate toggle.
- **MCP:** `dress_for_work` takes a designed look (garments only, or `{"code": "none"}`), a wish, or `clear`.
  `get_look` is unchanged and shows what's worn now.

## Consequences

- One design call and up to three print calls per job title, once. Every Persona with a job gets one after deploy.
- `FigureDesign.DrawPrintsAsync` is shared by Places and Occupations.
- Not built: a Company uniform shared by its staff, changing at home before leaving, and owning designed garments in
  general (ADR 0028's open step; `OwnLook` covers only the round trip).
- The figure doc has no crossed kimono collar. A kosode is a V neck with long sleeves, and a hakama is full-length
  flared pants with pleats. If that doesn't read, a `wrap` neck is the next field.
