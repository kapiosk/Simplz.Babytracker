using Microsoft.EntityFrameworkCore;
using Simplz.Babytracker.Data;

namespace Simplz.Babytracker.Services;

/// <summary>What became of a session that was asked to stop.</summary>
public enum StopOutcome
{
    /// <summary>Stopped and kept, as normal.</summary>
    Stopped = 0,

    /// <summary>Too short to have been meant, so it was thrown away instead of recorded.</summary>
    Discarded = 1,

    /// <summary>There was nothing running — stopped on the other phone a moment earlier.</summary>
    NotRunning = 2
}

/// <param name="Discarded">
/// The thrown-away entry, ready to be handed back to <see cref="EventService.AddAsync"/> if
/// whoever stopped it says the app got that wrong. Null unless it was discarded.
/// </param>
public sealed record StopResult(StopOutcome Outcome, TimeSpan Elapsed, BabyEvent? Discarded);

/// <param name="Started">The new session, or the one already running if there was one.</param>
/// <param name="Ended">The other session this one displaced, if any.</param>
/// <param name="EndedWasDiscarded">
/// Whether that displaced session was thrown away rather than recorded, for having been too
/// short to have been meant.
/// </param>
public sealed record StartResult(BabyEvent Started, BabyEvent? Ended, bool EndedWasDiscarded);

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
    /// The session of this kind that has been started and not stopped, if there is one. Asked
    /// per kind because the caller wants to know which it is, not merely that something runs.
    /// </summary>
    public async Task<BabyEvent?> GetRunningAsync(int babyId, EventKind kind, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Events
            .Where(e => e.BabyId == babyId && e.Kind == kind && e.EndUtc == null)
            .OrderByDescending(e => e.StartUtc)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Starts a session, or returns the one already running rather than opening a second.
    ///
    /// A baby cannot be feeding and asleep at the same time, so starting either ends the other,
    /// at the moment this one begins rather than at some rounded-off time. Enforced here rather
    /// than on the page, so it holds however the session was started.
    /// </summary>
    public async Task<StartResult> StartAsync(int babyId, EventKind kind, CancellationToken ct = default)
    {
        var running = await GetRunningAsync(babyId, kind, ct);
        if (running is not null)
        {
            return new StartResult(running, null, false);
        }

        var now = DateTime.UtcNow;
        await using var db = await factory.CreateDbContextAsync(ct);

        var others = await db.Events
            .Where(e => e.BabyId == babyId
                        && e.EndUtc == null
                        && e.Kind != kind
                        && BabyEvent.LastingKinds.Contains(e.Kind))
            .ToListAsync(ct);

        // Tapping sleep and then feed, seconds apart, is the mis-tap the ten-minute floor is
        // there for, so the session being cut short here is thrown away on the same terms as
        // one stopped by hand. Same media exception, for the same reason.
        var withMedia = others.Count == 0
            ? []
            : await db.Media.Where(m => others.Select(o => o.Id).Contains(m.BabyEventId))
                .Select(m => m.BabyEventId)
                .Distinct()
                .ToListAsync(ct);

        BabyEvent? ended = null;
        var endedWasDiscarded = false;

        foreach (var other in others)
        {
            var discard = !withMedia.Contains(other.Id)
                          && BabyEvent.TooShortToKeep(other.Kind, now - other.StartUtc);

            if (discard)
            {
                db.Events.Remove(other);
            }
            else
            {
                other.EndUtc = now;
            }

            // Only one lasting kind can be running — this method is what guarantees it — so the
            // loop finds at most one. Reporting the first keeps that true if it ever finds two.
            if (ended is null)
            {
                (ended, endedWasDiscarded) = (other, discard);
            }
        }

        var ev = new BabyEvent { BabyId = babyId, Kind = kind, StartUtc = now };
        db.Events.Add(ev);
        await db.SaveChangesAsync(ct);
        NotifyChanged(babyId);
        return new StartResult(ev, ended, endedWasDiscarded);
    }

    /// <summary>What starting this kind would end, so the page can say so before it happens.</summary>
    public async Task<BabyEvent?> WouldEndAsync(int babyId, EventKind starting, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Events
            .Where(e => e.BabyId == babyId
                        && e.EndUtc == null
                        && e.Kind != starting
                        && BabyEvent.LastingKinds.Contains(e.Kind))
            .OrderByDescending(e => e.StartUtc)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Stops a running session — or throws it away, if it was too short to have been meant. See
    /// <see cref="BabyEvent.TooShortToKeep"/> for which those are and why.
    /// </summary>
    public async Task<StopResult> StopAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (ev is null || ev.EndUtc is not null)
        {
            return new StopResult(StopOutcome.NotRunning, TimeSpan.Zero, null);
        }

        var now = DateTime.UtcNow;
        var elapsed = now - ev.StartUtc;

        // A photo hanging off it is somebody having gone to the trouble, so whatever the clock
        // says the entry was meant. Barely reachable — it needs the editor opened mid-sleep and
        // the timer stopped inside ten minutes — but the alternative is deleting their file.
        var hasMedia = await db.Media.AnyAsync(m => m.BabyEventId == ev.Id, ct);

        if (!hasMedia && BabyEvent.TooShortToKeep(ev.Kind, elapsed))
        {
            // Copied out before the delete, so the page can offer to put it back.
            var discarded = new BabyEvent
            {
                BabyId = ev.BabyId,
                Kind = ev.Kind,
                StartUtc = ev.StartUtc,
                EndUtc = now,
                Notes = ev.Notes
            };

            db.Events.Remove(ev);
            await db.SaveChangesAsync(ct);
            NotifyChanged(discarded.BabyId);
            return new StopResult(StopOutcome.Discarded, elapsed, discarded);
        }

        ev.EndUtc = now;
        await db.SaveChangesAsync(ct);
        NotifyChanged(ev.BabyId);
        return new StopResult(StopOutcome.Stopped, elapsed, null);
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
