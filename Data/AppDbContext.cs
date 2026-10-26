using Microsoft.EntityFrameworkCore;

namespace Simplz.Babytracker.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<BabyEvent> Events => Set<BabyEvent>();

    public DbSet<Baby> Babies => Set<Baby>();

    public DbSet<AppSetting> Settings => Set<AppSetting>();

    public DbSet<EventMedia> Media => Set<EventMedia>();

    public DbSet<PumpEntry> PumpEntries => Set<PumpEntry>();

    public DbSet<Milestone> Milestones => Set<Milestone>();

    public DbSet<TemperatureReading> Temperatures => Set<TemperatureReading>();

    public DbSet<Medicine> Medicines => Set<Medicine>();

    public DbSet<MedicineDose> MedicineDoses => Set<MedicineDose>();

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

        modelBuilder.Entity<PumpEntry>(e =>
        {
            // The stock is worked out by reading this table forwards from the last correction,
            // so every query is one baby in date order.
            e.HasIndex(x => new { x.BabyId, x.StartUtc });
            e.HasIndex(x => new { x.BabyId, x.Kind, x.StartUtc });

            e.HasOne<Baby>()
                .WithMany()
                .HasForeignKey(x => x.BabyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Milestone>(e =>
        {
            e.HasIndex(x => new { x.BabyId, x.ReachedOn });

            // One row per milestone from the guide per baby: recording it again moves the date
            // rather than adding a second. Entries somebody wrote themselves have no key and
            // are not constrained — two of those may well say similar things.
            e.HasIndex(x => new { x.BabyId, x.Key }).IsUnique().HasFilter("[Key] IS NOT NULL");

            e.HasOne<Baby>()
                .WithMany()
                .HasForeignKey(x => x.BabyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TemperatureReading>(e =>
        {
            e.HasIndex(x => new { x.BabyId, x.TakenUtc });

            e.HasOne<Baby>()
                .WithMany()
                .HasForeignKey(x => x.BabyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Medicine>(e =>
        {
            e.HasIndex(x => x.BabyId);

            e.HasOne<Baby>()
                .WithMany()
                .HasForeignKey(x => x.BabyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MedicineDose>(e =>
        {
            e.HasIndex(x => new { x.MedicineId, x.GivenUtc });

            // A dose belongs to its medicine, and so to the medicine's baby: deleting either
            // takes the doses with it.
            e.HasOne<Medicine>()
                .WithMany()
                .HasForeignKey(x => x.MedicineId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EventMedia>(e =>
        {
            e.HasIndex(x => x.BabyEventId);

            // Deleting an entry takes its attachments with it. The rows go by this cascade; the
            // files on disk are MediaService's job, which is why EventService asks it first.
            e.HasOne<BabyEvent>()
                .WithMany()
                .HasForeignKey(x => x.BabyEventId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
