# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Proompting (aka Partytown) is a Windows XP-themed AI chat application where multiple AI personas converse in "party" chat groups. It uses a C#/.NET backend with Microsoft Orleans (actor model) and a React frontend styled with xp.css.

## Development Environment

Everything runs via Docker Compose — no local .NET, Node, or PostgreSQL needed.

```bash
docker compose up          # Start all services (frontend, backend, db, caddy)
docker compose down        # Stop (preserves DB)
docker compose down -v     # Stop and wipe DB
```

A `.env` file is required at the project root with `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`, `HOST_DB_PORT`, `UID`, `GID`.

### Service URLs (host)

- **Frontend**: http://localhost:3000 (Vite dev server)
- **Backend**: http://localhost:5072 (.NET)
- **Caddy proxy**: http://localhost:8080 (routes `/api/*` → backend, `/*` → frontend)
- **Database**: localhost:${HOST_DB_PORT:-5455} (PostgreSQL + Apache AGE)

Both frontend and backend hot-reload on file changes.

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

### Backend (run inside `backend/` or via `docker compose exec dev-backend`)

```bash
dotnet run --project backend.csproj --urls http://0.0.0.0:5072
dotnet clean && dotnet build
```

### Database

```bash
docker compose exec age-db sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
```

DB init scripts live in `docker-entrypoint-initdb.d/`. To re-run them, wipe the volume (`docker compose down -v`).

## Architecture

### Backend (.NET 11 + Orleans 10)

**Orleans Grains (distributed actors):**
- `PartyGrain` — event-sourced (`JournaledGrain<PartyState, PartyEvent>`) grain keyed by GUID. Manages chat groups, participants, messages, and LLM generation. Persistence store: `"parties"`.
- `PersonaGrain` — stores AI persona config (model, provider, system prompt). Persistence store: `"personas"`.
- `PartyRootGrain` / `PersonaRootGrain` — singleton registry grains (keyed `Guid.Empty`) that track all parties/personas.

**Real-time:** `PartyRealtimeHub` is a singleton service managing raw WebSocket connections per party. It broadcasts message envelopes `{ type, sequence, timestamp, data }` to all connected clients. Not SignalR — it's custom WebSocket handling.

**LLM providers:** `ILlmProvider` interface with `OllamaLlmProvider` and `OpenRouterLlmProvider` implementations, registered in `LlmProviderRegistry`. Configured via `appsettings.json` under `Llm` section. Responses are streamed to clients via the realtime hub.

**Controllers:** `PartyController` (CRUD for parties/chat groups, prompt/reprompt/proceed endpoints) and `PersonaController` (CRUD for personas, model listing).

**OpenAPI:** Backend exposes spec at `/api/openapi/v1.json`.

### Frontend (React 19 + TanStack Start/Router/Query)

**Routing:** File-based via TanStack Router in `src/routes/`. Currently a single route (`index.tsx`) that renders the desktop experience. Route search params store desktop window layout state.

**API client:** Auto-generated React Query hooks in `src/api/party-zone.ts` via Orval. Regenerate with `npm run api-generate` when backend endpoints change. Hooks use `useSuspenseQuery` by default.

**State management:**
- `desktop-context.tsx` — Zustand store for window management (open/close/focus/drag windows, z-ordering)
- `realtime-store.ts` — Zustand store managing per-party WebSocket connections with reconnection, sequence tracking, and message merging

**Desktop UI:** Windows XP theme via `xp.css`. Uses `react-grid-layout` for draggable/resizable windows. Apps: `ChatManagerApp` (chat interface), `PersonasApp` (persona management).

**Code style:** Biome for linting/formatting — 4-space indent, single quotes, trailing commas. No Prettier/ESLint.

### Infrastructure

- **Caddy** reverse proxy routes API traffic to backend
- **PostgreSQL + Apache AGE** for Orleans clustering, grain persistence, and graph data
- **Orleans ports:** 11111 (silo-to-silo), 30000 (gateway)
