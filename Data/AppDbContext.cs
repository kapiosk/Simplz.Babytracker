using Microsoft.EntityFrameworkCore;

namespace Simplz.Babytracker.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<BabyEvent> Events => Set<BabyEvent>();

    public DbSet<Baby> Babies => Set<Baby>();

    public DbSet<AppSetting> Settings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppSetting>().HasKey(x => x.Key);

        modelBuilder.Entity<BabyEvent>(e =>
        {
            // Every query is for one baby at a time, so the baby leads each index.
            e.HasIndex(x => new { x.BabyId, x.StartUtc });
            e.HasIndex(x => new { x.BabyId, x.Kind, x.StartUtc });

            e.HasOne<Baby>()
                .WithMany()
                .HasForeignKey(x => x.BabyId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
