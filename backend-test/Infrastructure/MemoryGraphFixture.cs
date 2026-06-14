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

            // Dev Postgres already ran 05-age-setup.sql + 06-memory-graph.sql; this re-ensures the
            // graph idempotently so a freshly-reset volume (or a partial init) still passes.
            await MemoryGraphSchema.EnsureAsync(probe);
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            UnavailableReason = ex.Message;
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
