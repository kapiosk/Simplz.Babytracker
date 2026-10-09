using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Simplz.Babytracker.Data;
using Simplz.Babytracker.Services;

namespace Simplz.Babytracker.Checks;

/// <summary>
/// The services wired up against a fresh, migrated SQLite file, plus the means of saying
/// whether something came out right.
///
/// Every run gets its own database in its own temp folder. Running the real migrations rather
/// than creating the schema directly is deliberate: it means the checks also prove the
/// migrations apply, in order, from nothing.
/// </summary>
public sealed class Harness
{
    private readonly string folder;
    private readonly List<string> failures = [];
    private int passed;
    private string section = "";

    private Harness(
        string folder, IDbContextFactory<AppDbContext> factory, EventService events, PumpService pump,
        Milestones milestones, BabyService babies, int babyId)
    {
        this.folder = folder;
        Factory = factory;
        Events = events;
        Pump = pump;
        Milestones = milestones;
        Babies = babies;
        BabyId = babyId;
    }

    public IDbContextFactory<AppDbContext> Factory { get; }
    public EventService Events { get; }
    public PumpService Pump { get; }
    public Milestones Milestones { get; }
    public BabyService Babies { get; }

    /// <summary>The baby the migration creates, which every check records against.</summary>
    public int BabyId { get; }

    public static async Task<Harness> CreateAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"babytracker-checks-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContextFactory<AppDbContext>(o =>
            o.UseSqlite($"Data Source={Path.Combine(folder, "checks.db")}"));
        services.AddSingleton(sp => new MediaService(
            sp.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            Path.Combine(folder, "media"),
            sp.GetRequiredService<ILogger<MediaService>>()));
        services.AddSingleton<EventService>();
        services.AddSingleton<PumpService>();
        services.AddSingleton<Milestones>();
        services.AddSingleton<BabyService>();

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();

        int babyId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();
            babyId = await db.Babies.Select(b => b.Id).FirstAsync();
        }

        return new Harness(
            folder, factory,
            sp.GetRequiredService<EventService>(), sp.GetRequiredService<PumpService>(),
            sp.GetRequiredService<Milestones>(), sp.GetRequiredService<BabyService>(),
            babyId);
    }

    public void Section(string name)
    {
        section = name;
        Console.WriteLine();
        Console.WriteLine($"— {name}");
    }

    /// <summary>
    /// Compares by value, falling back to the printed form — so an int and a long that say the
    /// same thing agree, and so does a pair of DateOnly values.
    /// </summary>
    public void Check(string what, object? actual, object? expected)
    {
        var ok = Equals(actual, expected) || Equals(actual?.ToString(), expected?.ToString());

        if (ok)
        {
            passed++;
            Console.WriteLine($"  ok    {what}");
        }
        else
        {
            var line = $"{section}: {what} — got {Show(actual)}, expected {Show(expected)}";
            failures.Add(line);
            Console.WriteLine($"  FAIL  {what} — got {Show(actual)}, expected {Show(expected)}");
        }
    }

    private static string Show(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        _ => value.ToString() ?? "null"
    };

    /// <summary>
    /// Empties everything that belongs to the baby, so each group of checks starts from nothing
    /// and none of them depends on what an earlier one left behind.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var db = await Factory.CreateDbContextAsync();
        await db.Media.ExecuteDeleteAsync();
        await db.Events.ExecuteDeleteAsync();
        await db.PumpEntries.ExecuteDeleteAsync();
        await db.Milestones.ExecuteDeleteAsync();
        await db.Babies.Where(b => b.Id != BabyId).ExecuteDeleteAsync();
        await db.Babies.Where(b => b.Id == BabyId).ExecuteUpdateAsync(s => s.SetProperty(b => b.BornOn, (DateOnly?)null));
    }

    /// <summary>
    /// The services stamp "now", so a session that needs to have lasted a while has its start
    /// moved back after it is created.
    /// </summary>
    public async Task BackdateAsync(int eventId, TimeSpan by)
    {
        await using var db = await Factory.CreateDbContextAsync();
        var ev = await db.Events.FirstAsync(e => e.Id == eventId);
        ev.StartUtc -= by;
        await db.SaveChangesAsync();
    }

    public async Task SetPausedAtAsync(int eventId, DateTime? pausedAtUtc)
    {
        await using var db = await Factory.CreateDbContextAsync();
        var ev = await db.Events.FirstAsync(e => e.Id == eventId);
        ev.PausedAtUtc = pausedAtUtc;
        await db.SaveChangesAsync();
    }

    public async Task<BabyEvent> EventAsync(int eventId)
    {
        await using var db = await Factory.CreateDbContextAsync();
        return await db.Events.FirstAsync(e => e.Id == eventId);
    }

    public async Task<PumpEntry> PumpEntryAsync(int id)
    {
        await using var db = await Factory.CreateDbContextAsync();
        return await db.PumpEntries.FirstAsync(e => e.Id == id);
    }

    public async Task SetPumpTimesAsync(int id, DateTime startUtc, DateTime? pausedAtUtc = null)
    {
        await using var db = await Factory.CreateDbContextAsync();
        var e = await db.PumpEntries.FirstAsync(x => x.Id == id);
        e.StartUtc = startUtc;
        e.PausedAtUtc = pausedAtUtc;
        await db.SaveChangesAsync();
    }

    public async Task<int> CountAsync(EventKind kind)
    {
        await using var db = await Factory.CreateDbContextAsync();
        return await db.Events.CountAsync(e => e.BabyId == BabyId && e.Kind == kind);
    }

    public async Task<int> AddBabyAsync(string name)
    {
        await using var db = await Factory.CreateDbContextAsync();
        var baby = new Baby { Name = name };
        db.Babies.Add(baby);
        await db.SaveChangesAsync();
        return baby.Id;
    }

    /// <summary>Prints the tally and the failures together, and gives the exit code.</summary>
    public int Report()
    {
        Console.WriteLine();
        Console.WriteLine(new string('-', 60));
        Console.WriteLine($"{passed} passed, {failures.Count} failed");

        foreach (var line in failures)
        {
            Console.WriteLine($"  FAIL  {line}");
        }

        // The file is still held open by the pool until it is told to let go.
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // Left for the OS to tidy. Not worth failing a clean run over.
        }

        return failures.Count == 0 ? 0 : 1;
    }
}
