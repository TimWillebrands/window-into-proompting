using Microsoft.EntityFrameworkCore.Migrations;

namespace PartyTown.Data.Migrations;

internal static class MigrationHelpers
{
    /// <summary>Migrations are forward-only (ADR 0008): an accidental Down is loud, never a silent no-op.</summary>
    public static void ForwardOnlyDown(this MigrationBuilder b, string migrationName) =>
        b.Sql($"DO $$ BEGIN RAISE WARNING 'Migration {migrationName} is forward-only; Down() is a no-op.'; END $$;");
}
