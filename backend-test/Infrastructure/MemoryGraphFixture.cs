using Microsoft.EntityFrameworkCore;
using Npgsql;
using PartyTown.Data;

namespace BackendTest.Infrastructure;

/// <summary>Integration tests' Postgres+AGE: the shared Testcontainers one (<see cref="AgeDb"/>), or <c>PARTYTOWN_TEST_CONN</c> when set.</summary>
public sealed class MemoryGraphFixture : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }

    public IDbContextFactory<AppDbContext> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            ConnectionString = Environment.GetEnvironmentVariable("PARTYTOWN_TEST_CONN") ?? await AgeDb.ConnectionStringAsync();
        }
        catch (Exception ex)
        {
            UnavailableReason = $"no Docker for Testcontainers ({ex.Message})";
            return;
        }
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

            // Same migrations as backend boot, so tests run against the indexed schema; no-op once applied.
            await MemoryGraphSchema.EnsureAsync(probe);
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            UnavailableReason = ex.Message;
        }
    }

    /// <summary>Same database with Postgres session settings applied, e.g. to force a plan.</summary>
    public IDbContextFactory<AppDbContext> FactoryWith(string sessionOptions) => new TestDbContextFactory(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(ConnectionString) { Options = sessionOptions }.ConnectionString)
            .Options);

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
