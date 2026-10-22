# 05 Opening to Sketched, and demand triggers

Status: resolved (tram-stop exit open)
Type: task
Blocked by: 04

- IPlacesGrain.OpenAsync(localeId, archetype): Kind locale → public, spots + purpose from the archetype, same id.
- Archetype: System One choice. Spots: kit spot list (no LLM), names slotted.
- Demand: a Gathering at a Locale, a FollowThrough to one, the Player visiting → open at once to Sketched.
- Budget: one Opening per world-day. Author veto. A Happening announces it ("A new bar opened in Zuid").
- A tram-stop exit appears in the centre scene for any District with an open Place.

## Answer

- `Kits` (Places/Kits.cs): office, cafe, bar, restaurant, shop, gym, park, station, school, clinic, plus a plain "place".
  Each has spots (with Staff spots), a purpose and a description. The archetype comes from whole-word name keywords, then
  System One, then the plain kit.
- `IPlacesGrain.OpenAsync(localeId, archetype?, reason)`: the Locale turns public with the same id. Scene "sketch" is drawn
  by the client's generated room. "X's work" opening as an office becomes "X's office". Bodies away there are now there,
  and at-work ones sit at the Staff spot, "working". A Happening announces it, e.g. "The climbing gym in Zuid is on the map now".
  Capped at 24 Openings.
- `CloseAsync(placeId)`: author veto. The Place goes back to a Locale and whoever's there is away there again.
- Demand: `JoinAsync` to someone at a Locale opens it, but only if someone is a regular there (2+ visit days).
  FollowThrough ("I'll come by") goes through JoinAsync.
- Auto: `GrowthGrain` opens the strongest Locale past the gates, one per world-day, only when the author has switched
  Auto on (default off until thresholds are tuned live).
- Author UI: the Growing pane (All ears) gets Open per Locale, Close for Openings from the last 7 days, and the Auto toggle.
- Not done:
  - the tram-stop exit drawn in the centre scene;
  - a Gathering at a Locale (Calendar only plans at public Places today).
