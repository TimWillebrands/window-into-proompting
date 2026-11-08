# Player MCP

> Status: accepted. Builds on ADR 0029 (Clerk auth).

People want an agent of their own (Claude Code, Claude Desktop, …) to dress their Player and do up their Home. The
site's REST API is shaped for the frontend and its session tokens live a minute in a browser, so an outside agent has no
good way in.

## Decision

- **An MCP server inside the backend**, Streamable HTTP at `/api/mcp`, stateless (`ModelContextProtocol.AspNetCore`).
  Tools mirror the controller actions (`Mcp/PlayerTools.cs`): read (whoami, participants, places, situation, Home, look,
  wallet), look (palette picks, dress), Home (design, add / arrange / remove things, colours, move).
- **The agent designs, we check.** `get_design_guide` hands over the schema and rules our own designer gets (place,
  look, thing); `design_place`, `dress` and `add_thing` run the same validators and return what's wrong. The MCP never
  prompts the in-game LLM: the agent on the other end is the better designer.
- **Agents sign in as their human, through Clerk OAuth.** The endpoint is an OAuth protected resource (RFC 9728): a
  401 points at `/api/.well-known/oauth-protected-resource/mcp`, which names Clerk's Frontend API as the authorization
  server. Agents register themselves (DCR) and get a Clerk OAuth access token.
- **The Clerk scheme takes OAuth access tokens only on `/api/mcp`.** Opaque `oat_` and `at+jwt` tokens are verified by
  asking Clerk (which also catches revocation) and must name the endpoint in `aud`. Everywhere else they're refused: a
  grant to an agent doesn't open DMs or the rest of the API.
- **Same rights as the site.** Reading is public, as it is there. Changing takes `Actors.ActAsync`: your own Player, or
  anyone for an admin (`participantId`). A Player on auto refuses their human's hand, agent or not.
- **Designing runs off the Places grain** (`PlaceDesignerGrain`), so a slow redesign never holds the World up.

## Consequences

- Clerk must publish DCR and know the MCP URL (Dashboard → OAuth applications), or agents can't register.
- Every agent call costs a round trip to Clerk's verify API; fine at agent pace, cache it if that changes.
- `X-Forwarded-Proto` is trusted so sign-in links come out https behind kamal-proxy; the backend is only reachable
  through it.
- Pictures of a room or character need a headless browser (three.js renders client-side); a follow-up.
