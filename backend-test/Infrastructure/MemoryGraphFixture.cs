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
        // Idempotent slice 1 schema: matches docker-entrypoint-initdb.d/06-memory-graph.sql.
        // Replayed here so tests survive a long-lived volume where the original init script
        // ran before this file landed.
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
              FOREACH lbl IN ARRAY ARRAY['Persona','Participant','Party','Room','Message','Concept','Event']
              LOOP
                IF NOT EXISTS (
                  SELECT 1 FROM ag_catalog.ag_label
                   WHERE name = lbl AND graph = (SELECT graphid FROM ag_catalog.ag_graph WHERE name = 'memory')
                ) THEN
                  PERFORM ag_catalog.create_vlabel('memory', lbl);
                END IF;
              END LOOP;
            END
            $$;

            DO $$
            DECLARE
              lbl text;
            BEGIN
              FOREACH lbl IN ARRAY ARRAY['HAS_PARTICIPANT','IN_PARTY','RECOLLECTS','ANCHORED_TO','ABOUT','STANCE']
              LOOP
                IF NOT EXISTS (
                  SELECT 1 FROM ag_catalog.ag_label
                   WHERE name = lbl AND graph = (SELECT graphid FROM ag_catalog.ag_graph WHERE name = 'memory')
                ) THEN
                  PERFORM ag_catalog.create_elabel('memory', lbl);
                END IF;
              END LOOP;
            END
            $$;

            CREATE UNIQUE INDEX IF NOT EXISTS memory_persona_id_uq
              ON memory."Persona" ((properties ->> 'id'));
            CREATE UNIQUE INDEX IF NOT EXISTS memory_party_id_uq
              ON memory."Party" ((properties ->> 'id'));
            CREATE UNIQUE INDEX IF NOT EXISTS memory_room_id_uq
              ON memory."Room" ((properties ->> 'id'));
            CREATE UNIQUE INDEX IF NOT EXISTS memory_message_uq
              ON memory."Message" ((properties ->> 'room_id'), (properties ->> 'id'));
            CREATE UNIQUE INDEX IF NOT EXISTS memory_participant_uq
              ON memory."Participant" ((properties ->> 'persona_id'), (properties ->> 'party_id'));
            CREATE UNIQUE INDEX IF NOT EXISTS memory_concept_name_uq
              ON memory."Concept" ((properties ->> 'name'));
            CREATE INDEX IF NOT EXISTS memory_event_created_at_idx
              ON memory."Event" (((properties ->> 'created_at')));
            CREATE INDEX IF NOT EXISTS memory_recollects_start_idx
              ON memory."RECOLLECTS" (start_id);
            CREATE INDEX IF NOT EXISTS memory_recollects_end_idx
              ON memory."RECOLLECTS" (end_id);
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
