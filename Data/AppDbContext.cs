using Microsoft.EntityFrameworkCore;

namespace Simplz.Babytracker.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<BabyEvent> Events => Set<BabyEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BabyEvent>(e =>
        {
            e.HasIndex(x => x.StartUtc);
            e.HasIndex(x => new { x.Kind, x.StartUtc });
        });
    }
}
