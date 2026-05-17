# EF migrations hold raw SQL for the AGE memory schema

> Status: accepted. Supersedes the schema-management portion of [ADR 0006](0006-pure-age-for-memory.md) (specifically the "Schema lives in Cypher DDL, not EF migrations / manage in `docker-entrypoint-initdb.d/`" consequence). The rest of 0006 — pure AGE, no entity model, `AppDbContext` as connection holder — remains in force.

The memory schema (`create_graph`, vertex and edge labels) is versioned through **EF Core migrations holding raw `migrationBuilder.Sql(...)` blocks**. There are still **no `DbSet<>`s** for memory entities and EF's model snapshot stays empty for them; the migration file is the schema's only source of truth. `dotnet ef migrations add <Name> --output-dir Data/Migrations` produces an empty scaffold, the developer hand-writes the Cypher DDL inside `Up()`.

## Why (and not init scripts)

ADR 0006's original "Consequences" pointed schema versioning at `docker-entrypoint-initdb.d/`. That conflated two separable things:

- **(a) no EF entity model for memory** — the actual bet of 0006, unchanged here.
- **(b) no EF migrations tooling at all** — overreach; orthogonal to (a). Migrations are a generic DDL-versioning vehicle; whether the DDL is C# entity classes or hand-written `Sql(...)` is a detail.

Going with EF migrations buys:

- **Re-runnable on existing volumes.** `Database.Migrate()` consults `__EFMigrationsHistory` and only applies what's pending. No more `docker volume rm partytown-pgdata` to evolve dev schema.
- **One pipeline at startup.** Backend boots → `await ctx.Database.MigrateAsync()` runs every pending migration, regardless of whether its body is generated from a model or hand-written SQL. Prod (Kamal) and dev (Aspire) follow the same path.
- **Familiar tooling.** Every C# dev knows `dotnet ef migrations add`. Init-script numbering and ordering conventions are project-local lore.
- **Versioned, reviewable, branchable.** Migration files live in source with timestamps; init scripts in the docker bind-mount drift more easily.

## Boundary: what stays in init scripts

`CREATE EXTENSION age` requires superuser privileges (AGE is not in `pg_trusted` on vanilla Postgres). The app user that EF migrations connect as cannot run it. **Stays in `docker-entrypoint-initdb.d/05-age-setup.sql`**, where it runs once during initdb as the Postgres superuser.

Boundary line: **privilege requirement**. Superuser-only operations → init scripts. App-user-runnable schema → EF migrations.

| Operation | Owner | Reason |
| --- | --- | --- |
| `CREATE EXTENSION age` | initdb script | superuser-only |
| `LOAD 'age'`, `SET search_path = ag_catalog, "$user", public` | Npgsql connection-init | per-session, not schema |
| `create_graph('memory')` | EF migration | app-user OK |
| `create_vlabel(...)`, `create_elabel(...)` | EF migration | app-user OK |
| any data backfill | EF migration | app-user OK |

The first memory migration depends on AGE being already loaded into the cluster. Aspire orchestrates volume init → backend boot → migrations, in that order, so the dependency is satisfied. A fresh-clone dev workflow that bypasses Aspire would need to `CREATE EXTENSION age` manually first.

## Down migrations are forward-only

`Down()` bodies call a shared helper that emits a Postgres `RAISE WARNING` and does nothing else:

```csharp
internal static class MigrationHelpers
{
    public static void ForwardOnlyDown(this MigrationBuilder b, string migrationName) =>
        b.Sql($"DO $$ BEGIN RAISE WARNING 'Migration {migrationName} is forward-only; Down() is a no-op.'; END $$;");
}

// In each migration:
protected override void Down(MigrationBuilder b) => b.ForwardOnlyDown(GetType().Name);
```

Rationale: dev resets via volume wipe, prod rolls back by reverting the deployed image — neither path calls `Down()`. Writing real reverses (`drop_label`, `drop_graph`, undo data migrations) is engineering effort with no caller. The `RAISE WARNING` survives the helper getting copy-pasted incorrectly: an accidental `database update <previous-target>` will be loud in the logs instead of silently noop'ing.

## Layout

Migration files live flat under `backend/Data/Migrations/`. Descriptive names without a `Memory_` prefix — file count stays low, names like `InitMemoryGraph` / `AddStanceLabel` already self-describe. If file count explodes, subdir is a one-time refactor.

## Consequences

- **One context, two kinds of migrations.** `AppDbContext` already manages whatever relational tables exist (today: the V1 `app.persona_memory` — slated for removal in the V2 PR). New migrations may be either relational (EF-modeled, `dotnet ef migrations add` populates the body from the model diff) or raw Cypher (empty scaffold, hand-filled). Both flow through the same `Database.Migrate()` call.
- **`__EFMigrationsHistory` is the single audit log.** Init scripts (Orleans + AGE extension) remain invisible to EF; if they ever need versioning, that's a separate decision.
- **Cypher in C# string literals.** Verbatim string literals (`@""` or `"""..."""`) keep escaping reasonable. Worth a per-migration sanity check (run it locally) before merge — the C# compiler won't catch Cypher syntax errors.
- **Reversal cost stays bounded.** If EF migrations ever bite (toolchain rot, preview package churn), the migration files are still raw SQL — port them to numbered init scripts in an afternoon.
