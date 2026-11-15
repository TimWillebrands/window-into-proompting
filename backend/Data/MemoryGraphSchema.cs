using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace PartyTown.Data;

public static class MemoryGraphSchema
{
    private const long MigrationLock = 0x4D454D4F5259;

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
