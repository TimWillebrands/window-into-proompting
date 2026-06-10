using Microsoft.EntityFrameworkCore;
using Npgsql;
using PartyTown.Data;

namespace BackendTest.Infrastructure;

/// <summary>
/// Connects integration tests to the developer Postgres+AGE container that the Aspire
/// AppHost spins up on <c>localhost:5455</c>. Tests are skipped (via the static
/// <see cref="IsAvailable"/> flag) when the container is unreachable so CI without the
/// dev stack does not appear red.
/// </summary>
/// <remarks>
/// Connection string can be overridden via <c>PARTYTOWN_TEST_CONN</c>. Defaults match the
/// Aspire-managed container and the password baked into the local dev <c>.env</c>; if you
/// run a different password locally, export the env var.
/// </remarks>
public sealed class MemoryGraphFixture : IAsyncLifetime
{
    private const string DefaultConn =
        "Host=localhost;Port=5455;Database=partytown;Username=partytown;Password=postgresPW";

    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("PARTYTOWN_TEST_CONN") ?? DefaultConn;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }

    public IDbContextFactory<AppDbContext> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        Factory = new TestDbContextFactory(options);

        try
        {
            await using var probe = new NpgsqlConnection(ConnectionString);
            await probe.OpenAsync();
            await using (var cmd = probe.CreateCommand())
            {
                cmd.CommandText = "LOAD 'age';";
                await cmd.ExecuteNonQueryAsync();
            }

            await EnsureMemoryGraphSchemaAsync(probe);
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            UnavailableReason = ex.Message;
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task EnsureMemoryGraphSchemaAsync(NpgsqlConnection conn)
    {
        // Idempotent schema: matches docker-entrypoint-initdb.d/06-memory-graph.sql for
        // graph + labels (reshaped per ADR 0014 — no Party/Room/Message vertices, no
        // IN_PARTY/ANCHORED_TO edges). Indexes in the docker init script are perf-only and
        // skipped here; tests pass without them.
        // create_vlabel/create_elabel are cstring-typed, so cast explicitly.
        const string ddl = """
            LOAD 'age';
            SET search_path = ag_catalog, "$user", public;

            DO $$
            BEGIN
              IF NOT EXISTS (SELECT 1 FROM ag_catalog.ag_graph WHERE name = 'memory') THEN
                PERFORM ag_catalog.create_graph('memory');
              END IF;
            END
            $$;

            DO $$
            DECLARE
              lbl text;
            BEGIN
              FOREACH lbl IN ARRAY ARRAY['Persona','Participant','Concept','Event']
              LOOP
                IF NOT EXISTS (
                  SELECT 1 FROM ag_catalog.ag_label
                   WHERE name = lbl AND graph = (SELECT graphid FROM ag_catalog.ag_graph WHERE name = 'memory')
                ) THEN
                  PERFORM ag_catalog.create_vlabel('memory'::cstring, lbl::cstring);
                END IF;
              END LOOP;
            END
            $$;

            DO $$
            DECLARE
              lbl text;
            BEGIN
              FOREACH lbl IN ARRAY ARRAY['HAS_PARTICIPANT','RECOLLECTS','ABOUT','STANCE']
              LOOP
                IF NOT EXISTS (
                  SELECT 1 FROM ag_catalog.ag_label
                   WHERE name = lbl AND graph = (SELECT graphid FROM ag_catalog.ag_graph WHERE name = 'memory')
                ) THEN
                  PERFORM ag_catalog.create_elabel('memory'::cstring, lbl::cstring);
                END IF;
              END LOOP;
            END
            $$;
            """;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = ddl;
        await cmd.ExecuteNonQueryAsync();
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
