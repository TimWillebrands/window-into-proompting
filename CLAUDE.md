No file path provided — compressing the text directly:

---

# CLAUDE.md

Guidance for Claude Code (claude.ai/code) working in this repo.

## Project Overview

Proompting (aka Partytown) = Windows XP-themed AI chat. Multiple AI personas converse in party groups. C#/.NET backend + Microsoft Orleans (actor model). React frontend via xp.css.

## Development Environment

Dev runs on the host via .NET Aspire. The AppHost (`aspire/Proompting.AppHost`) orchestrates: backend (.NET project), frontend (Vite via pnpm), and Postgres+AGE (container). Aspire embeds its own dashboard for OTel logs/traces/metrics.

**Host prerequisites:**
- .NET SDK with .NET 10 runtime + .NET 11 preview SDK (the AppHost targets `net10.0`, the backend targets `net11.0` preview)
- Node 22+ with pnpm (install globally: `npm install -g pnpm`)
- Docker (Aspire spins up the Postgres container; backend + frontend run as host processes)

```bash
# From repo root
dotnet run --project aspire/Proompting.AppHost     # start everything
# Or: aspire run                                    # if Aspire CLI is installed
```

The dashboard URL is printed at startup (typically `http://localhost:15000`) and auto-opens in browser.

`.env` at repo root: `POSTGRES_PASSWORD`, `OPENROUTER_API_KEY`. AppHost reads `postgres-user`/`postgres-password`/`openrouter-api-key` parameters via `dotnet user-secrets` (set inside `aspire/Proompting.AppHost`) or env vars (`PARAMETERS__postgres-password=...`).

### Service URLs (host)

- **Aspire Dashboard**: printed at startup (e.g., `http://localhost:15000`) — links to all resources, logs, traces
- **Frontend**: http://localhost:5173 (Vite dev server)
- **Backend**: http://localhost:5072 (.NET, OpenAPI at `/api/openapi/v1.json`, Swagger at `/swagger`)
- **Database**: localhost:5455 (PostgreSQL + Apache AGE)

Backend hot-reloads via `dotnet watch` (Aspire spawns it). Frontend hot-reloads via Vite. Frontend talks to backend through Vite's `/api` proxy (configured from `VITE_API_URL` env var injected by AppHost).

## Commands

### Frontend (run from `frontend/`)

```bash
pnpm dev               # Vite dev server (Aspire normally runs this for you)
pnpm build             # Production build
pnpm test              # Vitest
pnpm lint              # Biome lint
pnpm check             # Biome check (lint + format)
pnpm api-generate      # Regenerate API client from OpenAPI spec (orval)
```

`pnpm api-generate` builds the backend with `OPENAPI_GENERATE=1` to emit `backend/openapi.json` (via `Microsoft.Extensions.ApiDescription.Server`), then runs orval against that file. The backend does not need to be running. The sentinel env var skips Orleans + DB wiring during the build so the GetDocument tool doesn't try to reach Postgres. `backend/openapi.json` is committed. Regenerate via `pnpm api-generate` after any change to the HTTP surface (controller routes, method signatures, request/response DTOs, status codes, OpenAPI metadata). Never hand-edit `backend/openapi.json`, `frontend/src/api/party-zone.ts`, or `frontend/src/api/model/**` — those files are regenerated from the backend controllers.

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

### Backend (run from `backend/`)

```bash
dotnet run --project backend.csproj            # run standalone (Aspire usually does this)
dotnet build
```

When the AppHost is running, the backend is launched with `dotnet watch` automatically.

### Database

The Postgres container is managed by AppHost. To open a `psql` shell:

```bash
psql -h localhost -p 5455 -U partytown -d partytown
# or via the Aspire dashboard → resources → age-db → console
```

DB init scripts in `docker-entrypoint-initdb.d/` are bind-mounted into the container and run on first volume init. To re-run them, delete the Aspire-managed volume `partytown-pgdata` (`docker volume rm partytown-pgdata`) and restart AppHost.

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

**API client:** Auto-gen React Query hooks in `src/api/party-zone.ts` via Orval. Regen with `pnpm api-generate` on backend changes. Default: `useSuspenseQuery`.

**State management:**
- `desktop-context.tsx` — Zustand store, window mgmt (open/close/focus/drag, z-ordering)
- `realtime-store.ts` — Zustand, per-party WebSocket conns: reconnect, sequence tracking, msg merging

**Desktop UI:** XP theme via `xp.css`. `react-grid-layout` for draggable/resizable windows. Apps: `ChatManagerApp` (chat), `PersonasApp` (persona mgmt), `ConfigPanelApp` (settings).

**Code style:** Biome for linting/formatting — 4-space indent, single quotes, trailing commas. No Prettier/ESLint.

### Infrastructure

- **.NET Aspire** (dev only): orchestrates backend, frontend, and Postgres. AppHost in `aspire/Proompting.AppHost`, shared OTel/health/service-discovery defaults in `aspire/Proompting.ServiceDefaults`.
- **PostgreSQL + Apache AGE** for Orleans clustering, grain persistence, and graph data
- **Orleans ports:** 11111 (silo-to-silo), 30000 (gateway)
- **Frontend → backend routing:** Vite proxies `/api/*` to `VITE_API_URL` (set by Aspire to `http://localhost:5072`).

### Deployment

Deployed via [Kamal](https://kamal-deploy.org/). Config: `config/deploy.yml`, `config/deploy.frontend.yml`. Target: `game.timwillebrands.nl`. Accessories: DB + standalone Aspire dashboard. **Production does not use the AppHost** — Kamal builds `backend/Dockerfile` and `frontend/Dockerfile` directly. The backend image still emits OTLP via `OTEL_EXPORTER_OTLP_ENDPOINT` (set by Kamal to the dashboard accessory), which `AddServiceDefaults()` honors.

## Agent skills

### Issue tracker

Local markdown under `.scratch/<feature-slug>/` — one dir per feature, one file per ticket. See `docs/agents/issue-tracker.md`.

### Triage labels

Canonical names — `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context — `CONTEXT.md` and `docs/adr/` at repo root. See `docs/agents/domain.md`.
