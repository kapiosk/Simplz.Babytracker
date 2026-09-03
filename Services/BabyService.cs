using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Simplz.Babytracker.Data;

namespace Simplz.Babytracker.Services;

/// <summary>
/// The babies being tracked, and which one the device signing in has picked.
///
/// The selection rides on the sign-in cookie as a claim rather than in a cookie of its own.
/// The pages are rendered on the server and already receive the signed-in user, so the choice
/// arrives with them — no extra round trip, and it survives a reload, a restart and a phone
/// being closed for a month, exactly as the sign-in does.
/// </summary>
public sealed class BabyService(IDbContextFactory<AppDbContext> factory, ILogger<BabyService> log)
{
    /// <summary>The claim on the sign-in cookie holding the selected baby's id.</summary>
    public const string ClaimType = "baby";

    /// <summary>Raised when a baby is added or renamed, so open pages pick it up.</summary>
    public event Action? Changed;

    public async Task<List<Baby>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Babies.OrderBy(b => b.Id).ToListAsync(ct);
    }

    public async Task<Baby?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Babies.FirstOrDefaultAsync(b => b.Id == id, ct);
    }

    /// <summary>How many entries each baby has, so the manage screen can say which is which.</summary>
    public async Task<Dictionary<int, int>> EntryCountsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Events
            .GroupBy(e => e.BabyId)
            .Select(g => new { BabyId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BabyId, x => x.Count, ct);
    }

    public async Task<Baby> AddAsync(string name, CancellationToken ct = default)
    {
        var baby = new Baby { Name = Clean(name) };
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Babies.Add(baby);
        await db.SaveChangesAsync(ct);
        NotifyChanged();
        return baby;
    }

    public async Task RenameAsync(int id, string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            // An emptied box is a slip, not a request to be called nothing.
            return;
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var baby = await db.Babies.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (baby is null)
        {
            return;
        }

        baby.Name = Clean(name);
        await db.SaveChangesAsync(ct);
        NotifyChanged();
    }

    /// <summary>
    /// The baby this device is looking at: the one on the sign-in cookie, or the first one when
    /// that claim is missing (nobody has chosen yet) or names a baby that no longer exists.
    /// Returns null only when there are no babies at all, which the migration rules out.
    /// </summary>
    public async Task<Baby?> SelectedAsync(Task<AuthenticationState>? state, CancellationToken ct = default)
    {
        var babies = await ListAsync(ct);
        if (babies.Count == 0)
        {
            return null;
        }

        if (state is null)
        {
            return babies[0];
        }

        var claim = (await state).User.FindFirst(ClaimType)?.Value;
        return int.TryParse(claim, out var id)
            ? babies.FirstOrDefault(b => b.Id == id) ?? babies[0]
            : babies[0];
    }

    /// <summary>Trimmed, and never empty — an unnamed chip is not something you can tap with confidence.</summary>
    private static string Clean(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length > 60)
        {
            trimmed = trimmed[..60];
        }

        return trimmed.Length == 0 ? "Baby" : trimmed;
    }

    private void NotifyChanged()
    {
        if (Changed is not { } subscribers)
        {
            return;
        }

        foreach (var subscriber in subscribers.GetInvocationList())
        {
            try
            {
                ((Action)subscriber)();
            }
            catch (Exception ex)
            {
                log.LogError(ex, "A page failed to handle a change to the list of babies.");
            }
        }
    }
}
