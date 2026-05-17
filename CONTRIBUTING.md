# Contributing to Partytown

This project uses [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) to orchestrate the dev environment. The AppHost runs the backend (.NET), frontend (Vite), and Postgres+AGE (container) as a single distributed app, with an embedded dashboard for logs, traces, and metrics.

## Quick Start

### Prerequisites

- .NET SDK with .NET 10 runtime + .NET 11 preview SDK (AppHost targets `net10.0`; backend targets `net11.0` preview)
- Node 22+ with npm
- Docker (Aspire spins up the Postgres container; backend + frontend run as host processes)
- Git
- Optional: the [Aspire CLI](https://learn.microsoft.com/dotnet/aspire/fundamentals/aspire-cli) for the shorter `aspire run` command

### 1. Clone the Repository

```bash
git clone <repository-url>
cd partytown
```

### 2. Configure secrets

Create a `.env` at the repo root:

```env
POSTGRES_PASSWORD=your_password
OPENROUTER_API_KEY=your_key
```

The AppHost reads `postgres-user` / `postgres-password` / `openrouter-api-key` parameters via `dotnet user-secrets` (set inside `aspire/Proompting.AppHost`) or env vars (`PARAMETERS__postgres-password=...`).

### 3. Start All Services

```bash
aspire run
# or, without the Aspire CLI:
dotnet run --project aspire/Proompting.AppHost
```

The dashboard URL is printed at startup (typically `http://localhost:15000`) and auto-opens in your browser. From there you can reach every resource, follow logs, and inspect traces.

Service endpoints:

- **Aspire Dashboard**: http://localhost:15000 (or whatever is printed)
- **Frontend** (React + Vite): http://localhost:5173
- **Backend** (.NET 11 + Orleans): http://localhost:5072 (OpenAPI at `/api/openapi/v1.json`, Swagger at `/swagger`)
- **Database** (PostgreSQL + Apache AGE): localhost:5455

## Development Workflow

### Hot Reloading

Both frontend and backend hot-reload automatically:

- **Frontend**: edit files in `frontend/src/` — Vite refreshes the browser instantly.
- **Backend**: edit files in `backend/` — `dotnet watch` (spawned by AppHost) rebuilds and restarts.

### Viewing Logs

Use the Aspire dashboard — each resource has a Logs tab with structured OTel output. For the raw stdout of a single resource, the dashboard's "Console" tab works too.

### Stopping Services

Hit `Ctrl+C` in the terminal running `aspire run`. The Postgres volume (`partytown-pgdata`) is preserved across restarts.

To wipe the database, stop AppHost and remove the volume:

```bash
docker volume rm partytown-pgdata
```

## Database Access

### Connection Details

The database is accessible at `localhost:5455` with the credentials from your `.env` / user-secrets:

```
Host: localhost
Port: 5455
Database: partytown
Username: partytown
Password: (from POSTGRES_PASSWORD)
```

### Connecting with psql

```bash
psql -h localhost -p 5455 -U partytown -d partytown
```

Or via the Aspire dashboard → resources → `age-db` → console.

### Apache AGE Graph Database

The project uses Apache AGE (A Graph Extension) for PostgreSQL. Example Cypher queries:

```sql
-- Enable AGE
LOAD 'age';
SET search_path = ag_catalog, "$user", public;

-- Create a graph
SELECT create_graph('my_graph');

-- Create nodes
SELECT * FROM cypher('my_graph', $$
  CREATE (n:Person {name: 'Alice', age: 30})
  RETURN n
$$) AS (n agtype);

-- Query nodes
SELECT * FROM cypher('my_graph', $$
  MATCH (n:Person)
  RETURN n
$$) AS (n agtype);
```

### Database Initialization

SQL files in `docker-entrypoint-initdb.d/` are bind-mounted into the Postgres container and executed on first volume init. To re-run them, remove the `partytown-pgdata` volume and restart AppHost.

## Troubleshooting

### "Port already in use" Errors

If ports 5173, 5072, 5455, 11111, 30000, or the dashboard port are in use, stop the conflicting process or adjust the AppHost configuration in `aspire/Proompting.AppHost/Program.cs`.

### Frontend dependencies not updating

```bash
cd frontend
npm install
```

Then restart `aspire run`.

### Backend build errors

```bash
dotnet clean backend/backend.csproj
```

Then restart `aspire run`. The dashboard shows the build output under the `backend` resource.

### Database won't start

Inspect the `age-db` resource logs in the Aspire dashboard. To reset (WARNING: deletes all data), stop AppHost and run `docker volume rm partytown-pgdata`.

### Orleans silo connection issues

Ensure these ports are available and not blocked:
- 11111 (silo-to-silo)
- 30000 (Orleans gateway)

## Project Structure

```
partytown/
├── aspire/                          # .NET Aspire orchestration
│   ├── Proompting.AppHost/          # AppHost (entry point for `aspire run`)
│   └── Proompting.ServiceDefaults/  # Shared OTel/health/service-discovery
├── .env                             # Secrets (POSTGRES_PASSWORD, OPENROUTER_API_KEY)
├── docker-entrypoint-initdb.d/      # Database initialization scripts
├── frontend/                        # React 19 + TanStack Start frontend
│   ├── src/
│   ├── package.json
│   └── vite.config.ts
└── backend/                         # .NET 11 + Orleans backend
    ├── Program.cs
    ├── backend.csproj
    └── Properties/
```

## Making Changes

### Frontend Development

1. Edit files in `frontend/src/`
2. Vite hot-reloads in the browser
3. Access at http://localhost:5173

Stack: React 19, TanStack Router/Query/Start, xp.css, Vite. Biome for lint/format.

### Backend Development

1. Edit files in `backend/`
2. `dotnet watch` rebuilds on save
3. Access API at http://localhost:5072

Stack: ASP.NET Core (.NET 11 preview), Microsoft Orleans 10, Npgsql.

### Adding Database Migrations

Since we use Apache AGE, traditional EF Core migrations don't apply. Instead:

1. Add SQL to `docker-entrypoint-initdb.d/`
2. Reset the volume: `docker volume rm partytown-pgdata` and restart `aspire run`
3. Or apply changes manually via `psql`

## Questions?

- Open the Aspire dashboard and check the relevant resource's logs.
- Review this guide's troubleshooting section.
- Open an issue with the error message and steps to reproduce.
