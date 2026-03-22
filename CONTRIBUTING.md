# Contributing to Partytown

This project uses Docker Compose for the entire development environment. You don't need to install .NET, Node.js, Bun, PostgreSQL, or any other dependencies locally.

## Quick Start

### Prerequisites

- Docker and Docker Compose
- Git

### 1. Clone the Repository

```bash
git clone <repository-url>
cd partytown
```

### 2. Start All Services

```bash
docker compose up
```

This will start three services:

- **Frontend** (SolidJS + Vite): http://localhost:3000
- **Backend** (.NET 11 + Orleans): http://localhost:5072
- **Database** (PostgreSQL + Apache AGE): localhost:5455

The first startup will download Docker images and install dependencies, which may take a few minutes.

## Development Workflow

### Hot Reloading

Both frontend and backend support hot reloading:

- **Frontend**: Edit files in `frontend/src/` - changes appear instantly in the browser
- **Backend**: Edit files in `backend/` - `dotnet watch` automatically rebuilds and restarts

### Running Services in Background

```bash
# Start detached (run in background)
docker compose up -d

# View logs
docker compose logs -f

# View logs for specific service
docker compose logs -f backend
```

### Stopping Services

```bash
# Stop gracefully (preserves database)
docker compose down

# Stop and remove database volume (start fresh)
docker compose down -v

# Stop specific service
docker compose stop backend
```

### Viewing Service Logs

```bash
# All services
docker compose logs

# Specific service
docker compose logs age-db
docker compose logs dev-backend
docker compose logs dev-frontend

# Follow logs (live tail)
docker compose logs -f backend
```

## Database Access

### Connection Details

The database is accessible at `localhost:5455` with the credentials defined in your `.env` file:

```
Host: localhost
Port: 5455
Database: (from POSTGRES_DB env var)
Username: (from POSTGRES_USER env var)
Password: (from POSTGRES_PASSWORD env var)
```

### Connecting with psql

```bash
docker compose exec age-db psql -U $POSTGRES_USER -d $POSTGRES_DB
```

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

SQL files in `docker-entrypoint-initdb.d/` are executed when the database is first created. Modify these to set up initial schema, extensions, or seed data.

## Troubleshooting

### "Port already in use" Errors

If ports 3000, 5072, 5455, 11111, or 30000 are in use, either:
1. Stop the conflicting service, or
2. Edit `compose.yml` to use different ports

### Frontend dependencies not updating

```bash
# Rebuild the frontend container
docker compose down
docker compose up --build frontend
```

Or manually enter the container:
```bash
docker compose exec dev-frontend bun install
docker compose restart dev-frontend
```

### Backend build errors

```bash
# Clean and rebuild
docker compose exec dev-backend dotnet clean
docker compose restart dev-backend
```

### Database won't start

```bash
# Reset database (WARNING: deletes all data)
docker compose down -v age-db
docker compose up
```

### Orleans silo connection issues

Ensure these ports are available and not blocked:
- 11111 (Silo-to-silo communication)
- 30000 (Orleans gateway)

## Project Structure

```
partytown/
├── compose.yml              # Docker Compose configuration
├── .env                     # Environment variables (database credentials)
├── docker-entrypoint-initdb.d/  # Database initialization scripts
├── frontend/                # SolidJS frontend
│   ├── src/
│   ├── package.json
│   └── vite.config.ts
└── backend/                 # .NET 11 + Orleans backend
    ├── Program.cs
    ├── backend.csproj
    └── Properties/
```

## Making Changes

### Frontend Development

1. Edit files in `frontend/src/`
2. Changes auto-reload in the browser
3. Access at http://localhost:3000

The frontend is configured with:
- SolidJS for reactive UI
- TanStack Router for routing
- TanStack Query for data fetching
- Tailwind CSS for styling
- Vite for fast dev server

### Backend Development

1. Edit files in `backend/`
2. `dotnet watch` automatically rebuilds on save
3. Access API at http://localhost:5072

The backend includes:
- ASP.NET Core 11
- Microsoft Orleans for distributed computing
- Connection to PostgreSQL via Npgsql

### Adding Database Migrations

Since we use Apache AGE (graph database), traditional EF Core migrations don't apply. Instead:

1. Add initialization SQL to `docker-entrypoint-initdb.d/`
2. Reset database: `docker compose down -v age-db && docker compose up`
3. Or use psql to run migrations manually

## Environment Variables

Create a `.env` file in the project root:

```env
POSTGRES_USER=your_username
POSTGRES_PASSWORD=your_password
POSTGRES_DB=your_database
HOST_DB_PORT=5455
```

These variables are used by both the database container and backend connection string.

## Optional: pgAdmin

Uncomment the pgadmin service in `compose.yml` to add a web-based database admin interface:

```yaml
pgadmin:
  image: dpage/pgadmin4
  container_name: dev-pgadmin
  environment:
    PGADMIN_DEFAULT_EMAIL: admin@local.dev
    PGADMIN_DEFAULT_PASSWORD: admin
  ports:
    - "5050:80"
  depends_on:
    - age-db
```

Access at http://localhost:5050 after restarting with `docker compose up`

## Questions?

- Check the logs: `docker compose logs -f [service]`
- Review this guide's troubleshooting section
- Open an issue with the error message and steps to reproduce
