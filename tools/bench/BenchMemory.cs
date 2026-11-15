using Microsoft.EntityFrameworkCore;
using Npgsql;
using PartyTown.Data;
using Testcontainers.PostgreSql;

namespace PartyTown.Bench;

public static class BenchMemory
{
    private const string Image = "apache/age:release_PG16_1.6.0";

    public static PostgreSqlContainer NewContainer()
        => new PostgreSqlBuilder().WithImage(Image).Build();

    public static async Task PrepareSchemaAsync(string connectionString, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(WithMemorySearchPath(connectionString));
        await conn.OpenAsync(ct);

        await ExecAsync(conn, "CREATE EXTENSION IF NOT EXISTS age;", ct);
        await ExecAsync(conn, "LOAD 'age';", ct);

        await MemoryGraphSchema.EnsureAsync(conn, ct);
    }

    public static IDbContextFactory<AppDbContext> CreateDbContextFactory(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(WithMemorySearchPath(connectionString))
            .AddInterceptors(new AgeOperatorInterceptor())
            .Options;
        return new BenchDbContextFactory(options);
    }

    private static string WithMemorySearchPath(string connectionString)
        => new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = "ag_catalog, public" }
            .ConnectionString;

    private static async Task ExecAsync(NpgsqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private sealed class BenchDbContextFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
