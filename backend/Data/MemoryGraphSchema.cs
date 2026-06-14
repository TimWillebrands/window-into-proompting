using Npgsql;

namespace PartyTown.Data;

/// <summary>
/// Idempotent builder for the <c>memory</c> graph schema, shared by every consumer that talks to
/// an AGE database the Aspire AppHost did <em>not</em> initialise: <c>backend-test</c>'s
/// <c>MemoryGraphFixture</c> (dev Postgres, schema already present → no-op) and the bench's
/// ephemeral Testcontainers AGE (bare container → this is the only place the graph gets built).
/// </summary>
/// <remarks>
/// Mirrors <c>docker-entrypoint-initdb.d/06-memory-graph.sql</c> for the graph + labels (reshaped
/// per ADR 0014 — no Party/Room/Message vertices, no IN_PARTY/ANCHORED_TO edges). The perf-only
/// indexes in the docker init script are skipped; correctness does not depend on them.
/// <para>
/// This ensures the graph object only — it assumes the AGE extension is present and the
/// <c>search_path</c> resolves <c>ag_catalog</c>. Against the Aspire DB that is true
/// (<c>05-age-setup.sql</c>); a bare container must run that bring-up first (see the bench's
/// <c>BenchMemory.PrepareAsync</c>).
/// </para>
/// </remarks>
public static class MemoryGraphSchema
{
    public static async Task EnsureAsync(NpgsqlConnection conn, CancellationToken ct = default)
    {
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
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
