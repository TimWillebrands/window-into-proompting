using Microsoft.EntityFrameworkCore;
using Npgsql;
using PartyTown.Data;
using Testcontainers.PostgreSql;

namespace PartyTown.Bench;

/// <summary>
/// Owns the bench's ephemeral Apache AGE container (ADR 0011 amendment). Memory-needing probes
/// get a real <c>MemoryRepository</c> over a one-shot container the bench starts itself — random
/// host port (no 5455 collision with a running AppHost), <see cref="PostgreSqlContainer.GetConnectionString"/>
/// as the single source of truth, Ryuk reaping it on exit. The Orleans DDL in
/// <c>docker-entrypoint-initdb.d/01–04</c> is dead weight here (bench uses in-memory grain storage),
/// so instead of bind-mounting the init dir we bring AGE up in C# and call the shared
/// <see cref="MemoryGraphSchema"/>.
/// </summary>
public static class BenchMemory
{
    // Same image the Aspire AppHost runs, so bench AGE behaviour matches dev.
    private const string Image = "apache/age:release_PG16_1.6.0";

    public static PostgreSqlContainer NewContainer()
        => new PostgreSqlBuilder().WithImage(Image).Build();

    /// <summary>
    /// Brings a freshly-started bare AGE container to the state the Aspire init scripts leave the
    /// dev DB in: <c>CREATE EXTENSION age</c> (registers <c>ag_catalog</c> + the cypher()/agtype
    /// machinery the bare image lacks), then the shared <see cref="MemoryGraphSchema"/>. The
    /// <c>search_path</c> half of <c>05-age-setup.sql</c> is handled per-connection instead — see
    /// <see cref="WithMemorySearchPath"/> — because a DB-level <c>ALTER DATABASE … SET</c> only
    /// reaches sessions opened after it, which Npgsql connection pooling makes unreliable here.
    /// </summary>
    public static async Task PrepareSchemaAsync(string connectionString, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(WithMemorySearchPath(connectionString));
        await conn.OpenAsync(ct);

        // CREATE EXTENSION registers ag_catalog + the cypher()/agtype machinery; LOAD (per session,
        // also done by AgeOperatorInterceptor on the repo's connections) arms the operators.
        await ExecAsync(conn, "CREATE EXTENSION IF NOT EXISTS age;", ct);
        await ExecAsync(conn, "LOAD 'age';", ct);

        await MemoryGraphSchema.EnsureAsync(conn, ct);
    }

    /// <summary>
    /// A DbContext factory over the container connection, wired like production
    /// (<c>backend/Program.cs</c>): <see cref="AgeOperatorInterceptor"/> fires <c>LOAD 'age'</c> on
    /// every pooled-connection checkout. The connection also pins <c>search_path</c> so the repo's
    /// unqualified <c>cypher()</c> resolves — the dev DB gets that from <c>05-age-setup.sql</c> at
    /// the database level, which the bare bench container has no equivalent for.
    /// </summary>
    public static IDbContextFactory<AppDbContext> CreateDbContextFactory(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(WithMemorySearchPath(connectionString))
            .AddInterceptors(new AgeOperatorInterceptor())
            .Options;
        return new BenchDbContextFactory(options);
    }

    // Npgsql applies the search_path in the startup packet on every physical connection, so each
    // pooled session sees ag_catalog regardless of open order — unlike a DB-level ALTER.
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
