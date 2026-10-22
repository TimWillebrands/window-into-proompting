# 09 Things from the catalogue

Status: resolved
Type: task
Blocked by: 06

- AddThingAsync / Decorate: match the description to catalogue tags, then a System One pick among the top 5. ThingModel only on a miss.
- Log misses; recurring misses go into a curation queue (assets follow demand).

## Answer

- `CatalogueMatch.Candidates`: a catalogue model is a candidate only if the description names its head noun ("plant" of
  k-potted-plant); candidates are ranked by word overlap with name + about, top 5.
- `ModelAsync` (decorate, AddThingAsync) tries the catalogue first. With System One on it picks among candidates or "none";
  otherwise it takes the best word match. A hit sets `Furni.Prefab` with no LLM call; a miss falls back to ThingModel as before.
- Misses go to `PlacesState.CatalogueMisses` (last 40), readable at `GET worlds/{id}/growth/catalogue-misses`:
  what to add to tools/catalogue/picks.json next.
- Client: `placeFurni` draws a thing as its catalogue prefab, over its kind and any generated model.
