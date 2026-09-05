using Microsoft.EntityFrameworkCore;
using Simplz.Babytracker.Data;

namespace Simplz.Babytracker.Services;

public class EventService(IDbContextFactory<AppDbContext> factory, MediaService media, ILogger<EventService> log)
{
    /// <summary>
    /// Raised whenever the event log changes, so open circuits can refresh. Carries the baby the
    /// change was for, so a page showing the other one does not reload for nothing.
    /// </summary>
    public event Action<int>? Changed;

    /// <summary>
    /// The subscribers are pages belonging to other people's circuits. Invoked as one delegate,
    /// the first of them to throw would stop the rest being told and surface as an unhandled
    /// error in the circuit of whoever happened to write the entry — their phone showing the
    /// error bar for a problem that was never theirs. So each page is notified on its own, and
    /// a page that cannot cope keeps that to itself.
    /// </summary>
    private void NotifyChanged(int babyId)
    {
        if (Changed is not { } subscribers)
        {
            return;
        }

        foreach (var subscriber in subscribers.GetInvocationList())
        {
            try
            {
                ((Action<int>)subscriber)(babyId);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "A page failed to handle a change to the event log.");
            }
        }
    }

    /// <summary>
    /// The session of this kind that has been started and not stopped, if there is one. A feed
    /// and a sleep run independently, so each is asked for separately.
    /// </summary>
    public async Task<BabyEvent?> GetRunningAsync(int babyId, EventKind kind, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Events
            .Where(e => e.BabyId == babyId && e.Kind == kind && e.EndUtc == null)
            .OrderByDescending(e => e.StartUtc)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Starts a session, or returns the one already running rather than opening a second.</summary>
    public async Task<BabyEvent> StartAsync(int babyId, EventKind kind, CancellationToken ct = default)
    {
        var running = await GetRunningAsync(babyId, kind, ct);
        if (running is not null)
        {
            return running;
        }

        var ev = new BabyEvent { BabyId = babyId, Kind = kind, StartUtc = DateTime.UtcNow };
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Events.Add(ev);
        await db.SaveChangesAsync(ct);
        NotifyChanged(babyId);
        return ev;
    }

    public async Task StopAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (ev is null || ev.EndUtc is not null)
        {
            return;
        }

        ev.EndUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        NotifyChanged(ev.BabyId);
    }

    /// <summary>Logs a point-in-time event (poop, urine, vomit).</summary>
    public async Task<BabyEvent> LogAsync(int babyId, EventKind kind, string? notes = null, CancellationToken ct = default)
    {
        var ev = new BabyEvent { BabyId = babyId, Kind = kind, StartUtc = DateTime.UtcNow, Notes = notes };
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Events.Add(ev);
        await db.SaveChangesAsync(ct);
        NotifyChanged(babyId);
        return ev;
    }

    public async Task<BabyEvent> LogBottleAsync(int babyId, MilkKind milk, int? amountMl, string? notes = null, CancellationToken ct = default)
    {
        var ev = new BabyEvent
        {
            BabyId = babyId,
            Kind = EventKind.BottleFeed,
            StartUtc = DateTime.UtcNow,
            Milk = milk,
            AmountMl = amountMl,
            Notes = notes
        };
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Events.Add(ev);
        await db.SaveChangesAsync(ct);
        NotifyChanged(babyId);
        return ev;
    }

    /// <summary>Adds an entry that happened earlier and is being logged after the fact.</summary>
    public async Task<BabyEvent> AddAsync(BabyEvent ev, CancellationToken ct = default)
    {
        ev.Id = 0;
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Events.Add(ev);
        await db.SaveChangesAsync(ct);
        NotifyChanged(ev.BabyId);
        return ev;
    }

    public async Task<List<BabyEvent>> GetRecentAsync(int babyId, int count = 20, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Events
            .Where(e => e.BabyId == babyId)
            .OrderByDescending(e => e.StartUtc)
            .Take(count)
            .ToListAsync(ct);
    }

    public async Task<List<BabyEvent>> GetRangeAsync(int babyId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Events
            .Where(e => e.BabyId == babyId && e.StartUtc >= fromUtc && e.StartUtc < toUtc)
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

        // The baby an entry belongs to is not something the editor changes.
        ev.Kind = updated.Kind;
        ev.StartUtc = updated.StartUtc;
        ev.EndUtc = updated.EndUtc;
        ev.Milk = updated.Milk;
        ev.AmountMl = updated.AmountMl;
        ev.Notes = updated.Notes;
        await db.SaveChangesAsync(ct);
        NotifyChanged(ev.BabyId);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        // Read the baby first: after the delete there is nothing left to ask.
        var babyId = await db.Events.Where(e => e.Id == id).Select(e => e.BabyId).FirstOrDefaultAsync(ct);
        if (babyId == 0)
        {
            return;
        }

        // The rows go with the entry through the foreign key, but the files would be left on
        // the disk with nothing pointing at them, so they go first.
        await media.DeleteForEventAsync(id, ct);

        await db.Events.Where(e => e.Id == id).ExecuteDeleteAsync(ct);
        NotifyChanged(babyId);
    }
}
