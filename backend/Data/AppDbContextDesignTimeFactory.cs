using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PartyTown.Data;

/// <summary>
/// Provides an <see cref="AppDbContext"/> to the <c>dotnet ef</c> CLI at design time.
/// At runtime, the host (Program.cs) wires the real connection string via Aspire.
/// </summary>
/// <remarks>
/// Override the connection by setting <c>PARTYTOWN_DESIGN_CONN</c>, e.g.
/// <c>PARTYTOWN_DESIGN_CONN="Host=localhost;Port=5455;Database=partytown;Username=partytown;Password=…"</c>
/// before running <c>dotnet ef migrations add</c> or <c>database update</c>.
/// </remarks>
internal sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string DefaultConn =
        "Host=localhost;Port=5455;Database=partytown;Username=partytown;Password=postgresPW";

    public AppDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("PARTYTOWN_DESIGN_CONN") ?? DefaultConn;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(conn)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options);
    }
}
