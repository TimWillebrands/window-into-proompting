using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PartyTown.Data.Migrations;

/// <summary>Only what RecallBeat's plans use; non-unique, so existing data can never fail it.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261105110100_AddMemoryReadIndexes")]
public sealed class AddMemoryReadIndexes : Migration
{
    protected override void Up(MigrationBuilder b) => b.Sql("""
        -- Every edge hop lands on a vertex by graphid; label tables get no index from AGE.
        CREATE INDEX IF NOT EXISTS memory_participant_id_idx ON memory."Participant" (id);
        CREATE INDEX IF NOT EXISTS memory_event_id_idx ON memory."Event" (id);
        CREATE INDEX IF NOT EXISTS memory_concept_id_idx ON memory."Concept" (id);
        CREATE INDEX IF NOT EXISTS memory_persona_id_idx ON memory."Persona" (id);

        -- Edge walks; the planner enters a walk from either end, so both sides of RECOLLECTS and ABOUT.
        CREATE INDEX IF NOT EXISTS memory_recollects_start_idx ON memory."RECOLLECTS" (start_id);
        CREATE INDEX IF NOT EXISTS memory_recollects_end_idx ON memory."RECOLLECTS" (end_id);
        CREATE INDEX IF NOT EXISTS memory_about_start_idx ON memory."ABOUT" (start_id);
        CREATE INDEX IF NOT EXISTS memory_about_end_idx ON memory."ABOUT" (end_id);
        CREATE INDEX IF NOT EXISTS memory_stance_start_idx ON memory."STANCE" (start_id);

        -- MATCH {...} property maps compile to `properties @> ...`, which only GIN can serve.
        CREATE INDEX IF NOT EXISTS memory_participant_props_idx ON memory."Participant" USING gin (properties);
        CREATE INDEX IF NOT EXISTS memory_persona_props_idx ON memory."Persona" USING gin (properties);
        """);

    protected override void Down(MigrationBuilder b) => b.ForwardOnlyDown(GetType().Name);
}
