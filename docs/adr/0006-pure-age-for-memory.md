# Memory graph: pure AGE; EF holds the connection and the migrations

> Status: accepted. Builds on ADR 0004. Absorbs the former ADR 0008 (EF migrations for the schema); it replaced 0006's "schema lives in init scripts".

The memory subsystem (**Concept**, **Event**, **Stance**, **Recollection**) keeps its canonical state as **Apache AGE** vertices and edges. **No EF entities** for memory, no `DbSet<>`s, EF's model snapshot stays empty. `AppDbContext` is the connection holder (`Database.SqlQueryRaw<T>`), DTO mapper and migration runner; Cypher is the query language. Services reach the database **directly**, not through Orleans grains.

## Why pure AGE (not EF rows + derived graph)

- Two stores must sync; the sync layer becomes the most-mutated, least-typed surface.
- Writes on one store, recall walks on the other: "atomic" writes no longer are.
- AGE complexity *and* EF model complexity at once.
- Escape hatch (ADR 0004) stays local: translate Cypher to recursive CTEs in the same Postgres.

## Why not grain-mediated

Recall walks every Participant and Event of a Party. Per-grain slices need cross-grain orchestration; one grain for the whole graph is a singleton in front of the DB. Memory writes are low-throughput and uncontended. Controller → service → DB.

## Schema: EF migrations holding raw SQL

The graph schema (`create_graph`, labels, backfills) is versioned by **EF migrations with hand-written `migrationBuilder.Sql(...)`**. Init scripts caused drift and needed `docker volume rm partytown-pgdata` to evolve; migrations re-run on existing volumes, one pipeline for Aspire dev and Kamal prod.

Boundary is **privilege**: superuser-only goes in `docker-entrypoint-initdb.d/05-age-setup.sql` (`CREATE EXTENSION age`); everything the app user can run is a migration. `LOAD 'age'` and `search_path` are Npgsql connection-init.

- **Boot:** `Program.cs` calls `MemoryGraphSchema.EnsureAsync` before `app.Run`; `backend-test` and the bench call the same method. A failed migration stops the boot.
- **`InitMemoryGraph` is idempotent:** dev volumes already have the graph from `06-memory-graph.sql`; prod doesn't mount it.
- **History table pinned to `public`** in `AppDbContext`: the DB `search_path` leads with `ag_catalog`, unqualified it lands in AGE's schema.
- **Advisory lock around `MigrateAsync`:** the provider's `LOCK TABLE` is released between migrations, concurrent callers double-apply and die on the history PK.
- **Hand-written, no snapshot:** migrations carry their own `[DbContext]`/`[Migration]` attributes; no `Microsoft.EntityFrameworkCore.Design` reference, so `dotnet ef migrations add` needs it first. Files live flat in `backend/Data/Migrations/`.
- **Forward-only:** `Down()` calls `MigrationHelpers.ForwardOnlyDown`, a `RAISE WARNING` no-op. Dev resets by volume wipe, prod rolls back by image; nothing calls `Down()`.
- **Fresh clone without Aspire** must `CREATE EXTENSION age` by hand first.

## Consequences

- Ad-hoc inspection is Cypher, no LINQ: `IMemoryRepository` carries the common projections.
- Every `RETURN` casts `agtype` to `text`/`int`/`uuid` for `SqlQueryRaw<T>`.
- Memory DTOs cross the service boundary as plain records, never as grain returns (Orleans codec footguns, `CLAUDE.md`).
- Cypher lives in C# string literals; the compiler won't catch syntax errors, run each migration locally before merge.
- `__EFMigrationsHistory` is the single audit log; init scripts stay invisible to EF.
- Reversal is bounded: the migrations are raw SQL, portable to numbered init scripts.
