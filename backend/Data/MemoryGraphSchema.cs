using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace PartyTown.Data;

/// <summary>The one schema pipeline (ADR 0008): backend boot, <c>backend-test</c> and the bench all migrate through here.</summary>
/// <remarks>Assumes the AGE extension exists; a bare container runs that bring-up first (<c>BenchMemory.PrepareSchemaAsync</c>).</remarks>
public static class MemoryGraphSchema
{
    // Arbitrary key, scoped to the database by Postgres.
    private const long MigrationLock = 0x4D454D4F5259;

    /// <summary>Concurrent MigrateAsync callers double-apply and die on the history PK, so they queue on an advisory lock.</summary>
    public static async Task EnsureAsync(NpgsqlConnection conn, CancellationToken ct = default)
    {
        await ExecAsync(conn, $"SELECT pg_catalog.pg_advisory_lock({MigrationLock})", ct);
        try
        {
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(conn).Options);
            await db.Database.MigrateAsync(ct);
        }
        finally
        {
            // A dead session already dropped the lock; unlocking there would mask the real failure.
            if (conn.State == ConnectionState.Open)
                await ExecAsync(conn, $"SELECT pg_catalog.pg_advisory_unlock({MigrationLock})", CancellationToken.None);
        }
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
