# CLAUDE.md

Guidance for Claude Code working in this repo.

## Project

Proompting (aka Partytown): Windows XP-themed AI chat where multiple AI personas converse in party groups. C#/.NET backend with Microsoft Orleans (actor model), React 19 frontend styled with xp.css, PostgreSQL + Apache AGE for Orleans persistence and the persona memory graph.

Domain terminology and architecture overview: `CONTEXT.md`. Design decisions: `docs/adr/`.

## Running the app

.NET Aspire orchestrates everything in dev: backend (`dotnet watch`), frontend (Vite), Postgres+AGE (container). Both hot-reload; the frontend proxies `/api` to the backend.

```bash
dotnet run --project aspire/Proompting.AppHost   # start everything; dashboard URL printed at startup
```

Prereqs: .NET 10 runtime + .NET 11 preview SDK (AppHost targets `net10.0`, backend `net11.0`), Node 22+ with pnpm, Docker.

Secrets: `.env` at repo root (`POSTGRES_PASSWORD`, `OPENROUTER_API_KEY`). The AppHost reads parameters from `dotnet user-secrets` (set inside `aspire/Proompting.AppHost`) or `PARAMETERS__*` env vars.

URLs: frontend http://localhost:5173 · backend http://localhost:5072 (Swagger at `/swagger`) · Postgres via `psql -h localhost -p 5455 -U partytown -d partytown`.

DB init scripts in `docker-entrypoint-initdb.d/` run on first volume init only. To re-run them: `docker volume rm partytown-pgdata`, then restart the AppHost.

## Commands

- Frontend (from `frontend/`): `pnpm test` (Vitest), `pnpm check` (Biome lint + format), `pnpm build`
- Backend (from `backend/`): `dotnet build`

### Generated API client

After any change to the backend HTTP surface (routes, DTOs, status codes, OpenAPI metadata), run `pnpm api-generate` from `frontend/`. It builds the backend with `OPENAPI_GENERATE=1` to emit `backend/openapi.json`, then runs orval; the backend does not need to be running. Never hand-edit `backend/openapi.json`, `frontend/src/api/party-zone.ts`, or `frontend/src/api/model/**` — they are generated from the backend controllers.

## Gotchas

- **Orleans serialization:** never use C# collection expressions (`[]`, `[.. x]`) for grain interface args/returns or serialized state — it compiles fine but throws `CodecNotFoundException` at runtime. Use `Array.Empty<T>()` or `.ToList()`.
- Realtime is a custom raw-WebSocket hub (`PartyRealtimeHub`), not SignalR.
- `PartyGrain` is event-sourced (`JournaledGrain`); the root registry grains are keyed `Guid.Empty`.

## Code style

Frontend uses Biome (4-space indent, single quotes, trailing commas) — no Prettier/ESLint. Generated React Query hooks default to `useSuspenseQuery`.

Don't write prose in comments, keep them concise and to the point. Use triple
slash comments with xml tags for in-code docs.
Comments and docblocks are 1 line max — if the why needs more, it belongs in
an ADR, the issue, or a commit message. Sacrifice syntactical correctness
for readability in writing.

## UI components (Storybook MCP)

When working on UI components, verify component properties through the `proompting-party-sb-mcp` MCP tools (`list-all-documentation`, `get-documentation`) before using them — never assume props from naming conventions or other libraries; if a prop isn't documented, check with the user. Fetch `get-storybook-story-instructions` before creating or updating stories, and verify with `run-story-tests`.

## Deployment

Kamal (`config/deploy.yml`, `config/deploy.frontend.yml`) deploys to game.timwillebrands.nl. Production builds `backend/Dockerfile` and `frontend/Dockerfile` directly — the Aspire AppHost is dev-only.

## Agent docs

- Issue tracker: local markdown under `.scratch/<feature-slug>/` — see `docs/agents/issue-tracker.md`
- Triage labels: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix` — see `docs/agents/triage-labels.md`
- Domain docs: `CONTEXT.md` + `docs/adr/` — see `docs/agents/domain.md`
