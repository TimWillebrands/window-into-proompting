using Microsoft.EntityFrameworkCore;

namespace PartyTown.Services.Graph;

/// <summary>
/// EF Core context backing AGE access. Cypher traffic does not flow through
/// EF model mapping (agtype isn't relational); callers reach the underlying
/// connection via <see cref="GraphService"/>. Add <see cref="DbSet{TEntity}"/>
/// properties here for any plain relational tables the app wants alongside
/// the graph.
/// </summary>
public class GraphDbContext(DbContextOptions<GraphDbContext> options) : DbContext(options)
{
}
