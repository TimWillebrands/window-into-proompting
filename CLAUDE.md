No file path provided — compressing the text directly:

---

# CLAUDE.md

Guidance for Claude Code (claude.ai/code) working in this repo.

## Project Overview

Proompting (aka Partytown) = Windows XP-themed AI chat. Multiple AI personas converse in party groups. C#/.NET backend + Microsoft Orleans (actor model). React frontend via xp.css.

## Development Environment

All via Docker Compose. No local .NET/Node/PostgreSQL.

```bash
docker compose up          # Start all services (frontend, backend, db, caddy)
docker compose down        # Stop (preserves DB)
docker compose down -v     # Stop and wipe DB
```

`.env` at project root: `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`, `HOST_DB_PORT`, `UID`, `GID`. `UID`/`GID` → backend container shares volume mounts (`.nuget`, `bin`, `obj`) w/ host for LSP.

### Service URLs (host)

- **Frontend**: http://localhost:3000 (Vite dev server)
- **Backend**: http://localhost:5072 (.NET)
- **Caddy proxy**: http://localhost:8080 (routes `/api/*` → backend, `/*` → frontend)
- **Database**: localhost:${HOST_DB_PORT:-5455} (PostgreSQL + Apache AGE)
- **Aspire Dashboard**: http://localhost:18888 (OpenTelemetry traces/metrics/logs)

Frontend + backend hot-reload on changes.

## Commands

### Frontend (run inside `frontend/` or via `docker compose exec dev-frontend`)

```bash
npm run dev            # Vite dev server
npm run build          # Production build
npm run test           # Vitest
npm run lint           # Biome lint
npm run check          # Biome check (lint + format)
npm run api-generate   # Regenerate API client from OpenAPI spec (orval)
```

`npm run api-generate` fetches OpenAPI spec from backend (via Caddy at localhost:8080). Requires `docker compose up` first.

#### Storybook MCP

When working on UI components, always use the `proompting-party-sb-mcp` MCP tools to access Storybook's component and documentation knowledge before answering or taking any action.

- **CRITICAL: Never hallucinate component properties!** Before using ANY property on a component from a design system (including common-sounding ones like `shadow`, etc.), you MUST use the MCP tools to check if the property is actually documented for that component.
- Query `list-all-documentation` to get a list of all components
- Query `get-documentation` for that component to see all available properties and examples
- Only use properties that are explicitly documented or shown in example stories
- If a property isn't documented, do not assume properties based on naming conventions or common patterns from other libraries. Check back with the user in these cases.
- Use the `get-storybook-story-instructions` tool to fetch the latest instructions for creating or updating stories. This will ensure you follow current conventions and recommendations.
- Check your work by running `run-story-tests`.

Remember: A story name might not reflect the property name correctly, so always verify properties through documentation or example stories before using them.

### Backend (run inside `backend/` or via `docker compose exec dev-backend`)

```bash
dotnet run --project backend.csproj --urls http://0.0.0.0:5072
dotnet clean && dotnet build
```

### Database

```bash
docker compose exec age-db sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
```

DB init scripts in `docker-entrypoint-initdb.d/`. Re-run: wipe volume (`docker compose down -v`).

## Architecture

### Backend (.NET 11 + Orleans 10)

**Orleans Grains (distributed actors):**
- `PartyGrain` — event-sourced (`JournaledGrain<PartyState, PartyEvent>`), GUID-keyed. Manages groups/participants/msgs/LLM gen. Store: `"parties"`.
- `PersonaGrain` — AI persona config (model, provider, system prompt). Store: `"personas"`.
- `PartyRootGrain` / `PersonaRootGrain` — singleton registry grains (keyed `Guid.Empty`), track all parties/personas.

**Real-time:** `PartyRealtimeHub` = singleton, manages raw WebSocket conns per party. Broadcasts `{ type, sequence, timestamp, data }` to clients. Custom WebSocket (not SignalR).

**LLM generation:** Orleans grains. `LlmRouterGrain` aggregates models from endpoint grains → routes gen requests. `OllamaEndpointGrain` + `OpenRouterEndpointGrain` impl `ILlmEndpointGrain`, handle streaming gen. Provider config: `LlmOptions` (from `Llm` in env/appsettings). Responses stream via realtime hub + Orleans streams.

**Controllers:** `PartyController` (CRUD parties/groups, prompt/reprompt/proceed). `PersonaController` (CRUD personas, model listing).

**OpenAPI:** Backend exposes spec at `/api/openapi/v1.json`.

**Orleans serialization constraint:** No C# collection exprs (`[]`, `[.. x]`) as grain interface return values. Compiler-gen `<>z__ReadOnlyList` unknown to Orleans → `CodecNotFoundException` at runtime. Use `Array.Empty<T>()` (empty) / `.ToList()` (spread/projection) instead.

### Frontend (React 19 + TanStack Start/Router/Query)

**Routing:** File-based via TanStack Router in `src/routes/`. Single route (`index.tsx`) renders desktop. Search params store window layout state.

**API client:** Auto-gen React Query hooks in `src/api/party-zone.ts` via Orval. Regen with `npm run api-generate` on backend changes. Default: `useSuspenseQuery`.

**State management:**
- `desktop-context.tsx` — Zustand store, window mgmt (open/close/focus/drag, z-ordering)
- `realtime-store.ts` — Zustand, per-party WebSocket conns: reconnect, sequence tracking, msg merging

**Desktop UI:** XP theme via `xp.css`. `react-grid-layout` for draggable/resizable windows. Apps: `ChatManagerApp` (chat), `PersonasApp` (persona mgmt), `ConfigPanelApp` (settings).

**Code style:** Biome for linting/formatting — 4-space indent, single quotes, trailing commas. No Prettier/ESLint.

### Infrastructure

- **Caddy** reverse proxy routes API traffic to backend
- **PostgreSQL + Apache AGE** for Orleans clustering, grain persistence, and graph data
- **Orleans ports:** 11111 (silo-to-silo), 30000 (gateway)

### Deployment

Deployed via [Kamal](https://kamal-deploy.org/). Config: `config/deploy.yml`, `config/deploy.frontend.yml`. Target: `game.timwillebrands.nl`. Accessories: DB + Aspire dashboard.
