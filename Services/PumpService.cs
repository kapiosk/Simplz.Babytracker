using Microsoft.EntityFrameworkCore;
using Simplz.Babytracker.Data;

namespace Simplz.Babytracker.Services;

/// <summary>
/// What is in the fridge, and the arithmetic behind it.
/// </summary>
/// <param name="Ml">
/// The figure to show. Can be negative, and is left that way on purpose: negative means more
/// breast milk has been given than was ever pumped, which is the ledger being wrong rather than
/// the fridge owing anybody milk. Showing it is what prompts somebody to put it right; clamping
/// it to zero would hide a number that is asking to be corrected.
/// </param>
/// <param name="OpeningMl">Where the count started — the last hand-set figure, or zero.</param>
/// <param name="FromUtc">
/// When the count started. Null when there is nothing to count from: no pumping has ever been
/// logged and the stock has never been set.
/// </param>
/// <param name="WasSet">Whether <paramref name="FromUtc"/> is a correction somebody made.</param>
public sealed record Stock(int Ml, int OpeningMl, DateTime? FromUtc, int PumpedMl, int TakenMl, bool WasSet)
{
    /// <summary>Nothing pumped, nothing set — there is no stock to speak of yet.</summary>
    public static readonly Stock Unknown = new(0, 0, null, 0, 0, false);

    public bool IsKnown => FromUtc is not null;
}

/// <summary>
/// Pumping sessions and the milk stock.
///
/// The stock is not a number kept anywhere; it is worked out on demand from what happened. It
/// starts at the last figure somebody set by hand, then adds every session pumped since and
/// subtracts every breast-milk bottle given since. A stored counter would drift, and drift with
/// nothing to compare against is unfixable — you can only overwrite it and hope. A ledger can
/// always say why it reads what it reads, which is also what makes "set the stock" an ordinary
/// entry rather than a special case: it is simply the newest thing the count starts from.
///
/// Breast-milk bottles live in <see cref="BabyEvent"/> and are logged on the Track page, so the
/// stock changes without this service being called at all. Anything showing it wants
/// <see cref="EventService.Changed"/> as well as <see cref="Changed"/>.
/// </summary>
public sealed class PumpService(IDbContextFactory<AppDbContext> factory, ILogger<PumpService> log)
{
    /// <summary>Raised when a pump entry changes, carrying the baby it was for.</summary>
    public event Action<int>? Changed;

    /// <summary>
    /// One subscriber at a time, each in its own try — see the same note on
    /// <see cref="EventService"/>. A page that cannot cope with the news must not take down the
    /// circuit of whoever happened to log the entry.
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
                log.LogError(ex, "A page failed to handle a change to the pump log.");
            }
        }
    }

    public async Task<PumpEntry?> GetRunningAsync(int babyId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.PumpEntries
            .Where(e => e.BabyId == babyId && e.Kind == PumpEntryKind.Session && e.EndUtc == null)
            .OrderByDescending(e => e.StartUtc)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Starts a session, or hands back the one already running rather than opening a second.</summary>
    public async Task<PumpEntry> StartAsync(int babyId, CancellationToken ct = default)
    {
        if (await GetRunningAsync(babyId, ct) is { } running)
        {
            return running;
        }

        var entry = new PumpEntry
        {
            BabyId = babyId,
            Kind = PumpEntryKind.Session,
            StartUtc = DateTime.UtcNow
        };

        await using var db = await factory.CreateDbContextAsync(ct);
        db.PumpEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        NotifyChanged(babyId);
        return entry;
    }

    /// <summary>
    /// Stops a running session and records what it produced.
    ///
    /// No ten-minute floor here, unlike a sleep. A short session still produced milk, and the
    /// millilitres are the point of the entry — the clock is the incidental part.
    /// </summary>
    public async Task StopAsync(int id, int? amountMl, string? notes, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entry = await db.PumpEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null || entry.EndUtc is not null)
        {
            return;
        }

        entry.EndUtc = DateTime.UtcNow;
        entry.AmountMl = amountMl;
        entry.Notes = Clean(notes);
        await db.SaveChangesAsync(ct);
        NotifyChanged(entry.BabyId);
    }

    /// <summary>
    /// Records that the stock is <paramref name="ml"/> as of now. Everything before this stops
    /// counting: this figure becomes what the ledger starts from.
    /// </summary>
    public async Task SetStockAsync(int babyId, int ml, string? notes, CancellationToken ct = default)
    {
        var entry = new PumpEntry
        {
            BabyId = babyId,
            Kind = PumpEntryKind.StockSet,
            StartUtc = DateTime.UtcNow,
            AmountMl = Math.Max(0, ml),
            Notes = Clean(notes)
        };

        await using var db = await factory.CreateDbContextAsync(ct);
        db.PumpEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        NotifyChanged(babyId);
    }

    /// <summary>
    /// Works the stock out from the log. See the note on the class for why it is not stored.
    /// </summary>
    public async Task<Stock> GetStockAsync(int babyId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var lastSet = await db.PumpEntries
            .Where(e => e.BabyId == babyId && e.Kind == PumpEntryKind.StockSet)
            .OrderByDescending(e => e.StartUtc)
            .FirstOrDefaultAsync(ct);

        DateTime fromUtc;
        int opening;
        bool wasSet;

        if (lastSet is not null)
        {
            (fromUtc, opening, wasSet) = (lastSet.StartUtc, lastSet.AmountMl ?? 0, true);
        }
        else
        {
            // Nobody has set it, so the count starts from the first session ever pumped, at zero.
            //
            // Which is the point of starting there rather than at the beginning of time: the app
            // recorded breast-milk bottles long before it could record pumping, and counting
            // those against a stock that was never filled would open on a large negative number
            // and look broken. Milk given before the first pump came from a fridge this ledger
            // knows nothing about.
            var firstSession = await db.PumpEntries
                .Where(e => e.BabyId == babyId && e.Kind == PumpEntryKind.Session)
                .OrderBy(e => e.StartUtc)
                .FirstOrDefaultAsync(ct);

            if (firstSession is null)
            {
                return Stock.Unknown;
            }

            (fromUtc, opening, wasSet) = (firstSession.StartUtc, 0, false);
        }

        // A session in progress has not produced anything yet, so it does not count until it is
        // stopped and the amount is entered.
        var pumped = await db.PumpEntries
            .Where(e => e.BabyId == babyId
                        && e.Kind == PumpEntryKind.Session
                        && e.StartUtc >= fromUtc
                        && e.EndUtc != null)
            .SumAsync(e => e.AmountMl ?? 0, ct);

        var taken = await db.Events
            .Where(e => e.BabyId == babyId
                        && e.Kind == EventKind.BottleFeed
                        && e.Milk == MilkKind.BreastMilk
                        && e.StartUtc >= fromUtc)
            .SumAsync(e => e.AmountMl ?? 0, ct);

        return new Stock(opening + pumped - taken, opening, fromUtc, pumped, taken, wasSet);
    }

    /// <summary>How much was pumped, and over how many sessions, between two instants.</summary>
    public async Task<(int Ml, int Sessions, TimeSpan Time)> TotalsAsync(
        int babyId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var sessions = await db.PumpEntries
            .Where(e => e.BabyId == babyId
                        && e.Kind == PumpEntryKind.Session
                        && e.StartUtc >= fromUtc
                        && e.StartUtc < toUtc
                        && e.EndUtc != null)
            .ToListAsync(ct);

        return (
            sessions.Sum(e => e.AmountMl ?? 0),
            sessions.Count,
            sessions.Aggregate(TimeSpan.Zero, (total, e) => total + (e.Duration ?? TimeSpan.Zero)));
    }

    public async Task<List<PumpEntry>> GetRecentAsync(int babyId, int count = 20, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.PumpEntries
            .Where(e => e.BabyId == babyId)
            .OrderByDescending(e => e.StartUtc)
            .Take(count)
            .ToListAsync(ct);
    }

    /// <summary>Adds a session that happened earlier and is being written down after the fact.</summary>
    public async Task<PumpEntry> AddAsync(PumpEntry entry, CancellationToken ct = default)
    {
        entry.Id = 0;
        entry.Notes = Clean(entry.Notes);
        await using var db = await factory.CreateDbContextAsync(ct);
        db.PumpEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        NotifyChanged(entry.BabyId);
        return entry;
    }

    public async Task UpdateAsync(PumpEntry updated, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entry = await db.PumpEntries.FirstOrDefaultAsync(e => e.Id == updated.Id, ct);
        if (entry is null)
        {
            return;
        }

        // Neither the baby nor which sort of entry it is are things the editor changes.
        entry.StartUtc = updated.StartUtc;
        entry.EndUtc = updated.EndUtc;
        entry.AmountMl = updated.AmountMl;
        entry.Notes = Clean(updated.Notes);
        await db.SaveChangesAsync(ct);
        NotifyChanged(entry.BabyId);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        // Read the baby before the row is gone; afterwards there is nothing left to ask.
        var babyId = await db.PumpEntries.Where(e => e.Id == id).Select(e => e.BabyId).FirstOrDefaultAsync(ct);
        if (babyId == 0)
        {
            return;
        }

        await db.PumpEntries.Where(e => e.Id == id).ExecuteDeleteAsync(ct);
        NotifyChanged(babyId);
    }

    private static string? Clean(string? notes) =>
        string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
}
