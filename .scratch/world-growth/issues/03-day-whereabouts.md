# 03 Day whereabouts and a closed vocabulary

Status: ready-for-agent
Type: task
Blocked by: 01

- Add output field `whereabouts` to the Day call: "HH:MM-HH:MM | place name" per beat away from home.
- Add input line "Places you go: …" (own anchors + District top 5) with a `new:` escape.
- Coordinate: proompting-75 adds a "Fixed today: …" input line to the same call (shifts). Don't touch it.
- Bodies go away for beats of 2 h or more when they're not mid-conversation; they come back at the end of the beat.
