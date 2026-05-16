# Pure AGE for memory data, EF only as a connection holder

The memory subsystem (**Concept**, **Event**, **Stance**, **Recollection**) stores its canonical state as **Apache AGE** vertices and edges. There are **no EF Core entities** for memory and **no EF migrations** on the memory schema. The existing `AppDbContext` is kept solely to host `Database.SqlQueryRaw<T>` — Cypher is the query language, EF is just the connection holder and DTO mapper. The memory subsystem accesses the database **directly** from services, not via Orleans grains.

This builds on the bet in [ADR 0004](0004-postgres-age-for-persona-memories.md).

## Why pure AGE (not a hybrid EF + AGE store)

ADR 0004 bets on AGE because the data is a mesh (Persona → Concept → Event → Recollection → …) and graph traversals are the natural recall query. A hybrid store — EF rows as the source of truth with AGE as a derived graph view — is a half-bet:

- Two stores must stay in sync. The sync layer becomes the most-mutated, least-typed surface in the subsystem.
- Recall walks would run on the derived store; writes would run on the canonical one. Writes that "feel atomic" no longer are.
- We'd pay AGE complexity *and* EF migration complexity at once.

Pure AGE keeps a single source of truth. If the AGE bet fails, ADR 0004's escape hatch (relational tables in the same Postgres) still applies — the migration is local, not cross-database.

## Why not grain-mediated

Recall is a graph walk that spans every Participant and Event in a Party, not a single grain's state. Wrapping that in an Orleans grain means either:

- Each grain holds a slice of the graph (then walks need cross-grain orchestration, which is fighting the actor model), or
- One grain holds the whole graph (then it's a singleton in front of the database — pure ceremony).

Memory writes are also low-throughput and uncontended. Orleans' concurrency model doesn't earn its place here. The PoC's instinct (controller → service → DB) was right; keep it.

## Why keep `AppDbContext` at all

Reasons it stays:

- Connection pooling and `ServiceProvider` integration come for free.
- `Database.SqlQueryRaw<T>` maps result rows into plain C# records — cheaper than reaching for Npgsql + Dapper for the same job.
- Other features may eventually want EF for non-memory data; the context already exists.

There are **no `DbSet<>`s for memory entities**. The context is an execution surface, not a model.

## Consequences

- **Schema lives in Cypher DDL**, not EF migrations. Manage it in `docker-entrypoint-initdb.d/` (or an equivalent versioned init script). EF's `dotnet ef migrations` will not see memory entities.
- **Ad-hoc inspection is harder.** No LINQ; admin queries are Cypher. Worth a small helper layer in the backend (`IMemoryRepository` with a few common projections) so callers don't write raw Cypher everywhere.
- **`agtype` casts in every query.** AGE returns its own graph type by default; every `RETURN` needs explicit casts to `text` / `int` / `uuid` for `SqlQueryRaw<T>` to map cleanly. Boilerplate is real but contained.
- **Orleans serialization stays out of the memory path.** Memory DTOs cross the service boundary as plain records, never as grain method returns. Avoids the Orleans codec footguns flagged in `CLAUDE.md`.
- **Reversal cost is bounded.** If AGE has to go, the rewrite is "translate Cypher to recursive CTEs" — schema and access pattern stay close to today's shape.
