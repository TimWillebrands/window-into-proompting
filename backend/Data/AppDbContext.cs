using Microsoft.EntityFrameworkCore;
using PartyTown.Data.Entities;

namespace PartyTown.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<PersonaMemory> PersonaMemories => Set<PersonaMemory>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("app");

        var m = b.Entity<PersonaMemory>();
        m.ToTable("persona_memory");
        m.HasKey(x => x.Id);
        m.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        m.Property(x => x.EncodedAt).HasDefaultValueSql("now()");
        m.Property(x => x.Text).IsRequired();
        m.HasIndex(x => new { x.PersonaId, x.PartyId });
        m.HasIndex(x => new { x.PartyId, x.ChatGroupId, x.SourceMessageId });
    }
}
