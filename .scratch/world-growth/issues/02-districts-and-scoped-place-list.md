# 02 Districts and a scoped place list

Status: resolved
Type: task

- Place.DistrictId; seed District "the centre" (Lobby, Café, city centre, Homes).
- Walking between Districts takes minutes ("on the tram to Zuid"); within one, unchanged.
- Shouts carry next door only within a District (Overhearing.NextDoor).
- PlacesApp: list your District, your anchors and Places where your Ties are; other Districts collapse to one line each.
- Tests: list stays bounded with 20 Places across 4 Districts.

## Answer

- Districts are slugs (`centre`, `amsterdam-zuid`) in `PlacesState.Districts`; `Place.DistrictId` (null only for unplaced Locales).
  `DistrictAsync(name)` resolves or creates one, and `SetDistrictAsync(placeId, id)` places something in it. Seeds, Homes and Made Places start in the centre.
- Crossing Districts is a 4-minute tram ride: "You're on the tram to the Café in Amsterdam Zuid".
- Shouts carry next door only within a District.
- PlacesApp: your District in full. Each other District gets one line ("Further away, a tram ride — Zuid: the Bar: Ana, Bo (7 places)")
  and one "Take the tram" affordance toward where people are.
- Not done: Navigator grouping by District (wait until a second District exists).
