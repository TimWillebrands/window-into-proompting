using Microsoft.EntityFrameworkCore;

namespace PartyTown.Data;

/// <summary>
/// Connection holder for the memory subsystem. Carries no <see cref="DbSet{TEntity}"/>s —
/// memory state lives in Apache AGE as Cypher-managed vertices and edges (see ADR 0006).
/// Callers use <see cref="DatabaseFacade.SqlQueryRaw{TResult}(string, object[])"/> to
/// execute Cypher wrapped in <c>SELECT * FROM cypher(...)</c>.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}
