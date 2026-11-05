using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PartyTown.Data.Migrations;

/// <summary>Graph + labels; idempotent because dev volumes already got them from 06-memory-graph.sql.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261105110000_InitMemoryGraph")]
public sealed class InitMemoryGraph : Migration
{
    protected override void Up(MigrationBuilder b) => b.Sql("""
        LOAD 'age';

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
        """);

    protected override void Down(MigrationBuilder b) => b.ForwardOnlyDown(GetType().Name);
}
