using Microsoft.EntityFrameworkCore;
using Simplz.Babytracker.Data;

namespace Simplz.Babytracker.Services;

public class EventService(IDbContextFactory<AppDbContext> factory)
{
    /// <summary>Raised whenever the event log changes, so open circuits can refresh.</summary>
    public event Action? Changed;

    private void NotifyChanged() => Changed?.Invoke();

    public async Task<BabyEvent?> GetRunningBreastFeedAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Events
            .Where(e => e.Kind == EventKind.BreastFeed && e.EndUtc == null)
            .OrderByDescending(e => e.StartUtc)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<BabyEvent> StartBreastFeedAsync(CancellationToken ct = default)
    {
        var running = await GetRunningBreastFeedAsync(ct);
        if (running is not null)
        {
            return running;
        }

        var ev = new BabyEvent { Kind = EventKind.BreastFeed, StartUtc = DateTime.UtcNow };
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Events.Add(ev);
        await db.SaveChangesAsync(ct);
        NotifyChanged();
        return ev;
    }

    public async Task StopBreastFeedAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (ev is null || ev.EndUtc is not null)
        {
            return;
        }

        ev.EndUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        NotifyChanged();
    }

    /// <summary>Logs a point-in-time event (poop, urine, vomit).</summary>
    public async Task<BabyEvent> LogAsync(EventKind kind, string? notes = null, CancellationToken ct = default)
    {
        var ev = new BabyEvent { Kind = kind, StartUtc = DateTime.UtcNow, Notes = notes };
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Events.Add(ev);
        await db.SaveChangesAsync(ct);
        NotifyChanged();
        return ev;
    }

    public async Task<BabyEvent> LogBottleAsync(MilkKind milk, int? amountMl, string? notes = null, CancellationToken ct = default)
    {
        var ev = new BabyEvent
        {
            Kind = EventKind.BottleFeed,
            StartUtc = DateTime.UtcNow,
            Milk = milk,
            AmountMl = amountMl,
            Notes = notes
        };
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Events.Add(ev);
        await db.SaveChangesAsync(ct);
        NotifyChanged();
        return ev;
    }

    /// <summary>Adds an entry that happened earlier and is being logged after the fact.</summary>
    public async Task<BabyEvent> AddAsync(BabyEvent ev, CancellationToken ct = default)
    {
        ev.Id = 0;
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Events.Add(ev);
        await db.SaveChangesAsync(ct);
        NotifyChanged();
        return ev;
    }

    public async Task<List<BabyEvent>> GetRecentAsync(int count = 20, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Events
            .OrderByDescending(e => e.StartUtc)
            .Take(count)
            .ToListAsync(ct);
    }

    public async Task<List<BabyEvent>> GetRangeAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Events
            .Where(e => e.StartUtc >= fromUtc && e.StartUtc < toUtc)
            .OrderByDescending(e => e.StartUtc)
            .ToListAsync(ct);
    }

    public async Task<BabyEvent?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Events.FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task UpdateAsync(BabyEvent updated, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == updated.Id, ct);
        if (ev is null)
        {
            return;
        }

        ev.Kind = updated.Kind;
        ev.StartUtc = updated.StartUtc;
        ev.EndUtc = updated.EndUtc;
        ev.Milk = updated.Milk;
        ev.AmountMl = updated.AmountMl;
        ev.Notes = updated.Notes;
        await db.SaveChangesAsync(ct);
        NotifyChanged();
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Events.Where(e => e.Id == id).ExecuteDeleteAsync(ct);
        NotifyChanged();
    }
}
