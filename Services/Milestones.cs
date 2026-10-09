using Microsoft.EntityFrameworkCore;
using Simplz.Babytracker.Data;

namespace Simplz.Babytracker.Services;

/// <summary>Roughly what a milestone is about, so the list can be read in groups.</summary>
public enum MilestoneArea
{
    Movement = 0,
    Communication = 1,
    Social = 2,
    Feeding = 3
}

/// <summary>
/// One milestone from the guide.
/// </summary>
/// <param name="FromWeeks">The early end of when babies typically do this.</param>
/// <param name="ToWeeks">
/// The late end. Deliberately wide: these are ranges within which most babies arrive, not dates
/// anything is due. Nothing in the app ever calls a milestone late or missed.
/// </param>
public sealed record MilestoneGuide(
    string Key, string Title, MilestoneArea Area, int FromWeeks, int ToWeeks);

/// <summary>
/// The milestone guide, and the record of which ones a baby has reached.
///
/// The guide follows the NHS's own framing — Start4Life and the Birth to five information the
/// red book is built around — which is consistently that babies vary enormously and that these
/// are typical ranges rather than targets. That framing is the point, not a disclaimer bolted
/// on: a list presented as things due by a date is worrying rather than useful to a parent
/// whose baby is perfectly well and simply doing it next month.
///
/// So: every entry carries a range of weeks, never a single age. Nothing is ever shown as
/// overdue, late or missed, and nothing is counted against the baby. The app records what
/// happened, which is what it does everywhere else; it does not assess anybody, and it says
/// where to take a worry instead.
/// </summary>
public sealed class Milestones(IDbContextFactory<AppDbContext> factory, ILogger<Milestones> log)
{
    /// <summary>Raised when a milestone is recorded or removed, carrying the baby it was for.</summary>
    public event Action<int>? Changed;

    /// <summary>
    /// The guide, in the order a baby meets it. Ranges in weeks from birth.
    ///
    /// Kept broad on purpose and limited to the well-known ones. A longer list of finer-grained
    /// items would read as a checklist to be completed, which is exactly what this should not be.
    /// </summary>
    public static readonly IReadOnlyList<MilestoneGuide> Guide =
    [
        new("smiles", "Smiles at you", MilestoneArea.Social, 4, 8),
        new("follows", "Follows a face or a toy with their eyes", MilestoneArea.Social, 4, 10),
        new("head-up", "Holds their head up during tummy time", MilestoneArea.Movement, 6, 16),
        new("coos", "Makes cooing and gurgling sounds", MilestoneArea.Communication, 6, 14),
        new("laughs", "Laughs out loud", MilestoneArea.Social, 10, 20),
        new("grasps", "Reaches for and holds a toy", MilestoneArea.Movement, 12, 22),
        new("rolls", "Rolls over", MilestoneArea.Movement, 16, 30),
        new("babbles", "Babbles — bababa, dadada", MilestoneArea.Communication, 20, 34),
        new("solids", "First tastes of solid food", MilestoneArea.Feeding, 24, 30),
        new("sits", "Sits up without support", MilestoneArea.Movement, 26, 38),
        new("name", "Responds to their own name", MilestoneArea.Communication, 28, 44),
        new("crawls", "Crawls or shuffles about", MilestoneArea.Movement, 30, 52),
        new("pincer", "Picks up small things between finger and thumb", MilestoneArea.Movement, 34, 48),
        new("stands", "Pulls themselves up to stand", MilestoneArea.Movement, 36, 56),
        new("waves", "Waves bye-bye", MilestoneArea.Social, 36, 56),
        new("first-word", "First word with meaning", MilestoneArea.Communication, 44, 70),
        new("cruises", "Walks holding on to the furniture", MilestoneArea.Movement, 44, 66),
        new("walks", "Walks on their own", MilestoneArea.Movement, 48, 78),
        new("cup", "Drinks from a cup", MilestoneArea.Feeding, 48, 78),
        new("two-words", "Puts two words together", MilestoneArea.Communication, 70, 104)
    ];

    public static MilestoneGuide? Find(string key) => Guide.FirstOrDefault(g => g.Key == key);

    /// <summary>
    /// What to say about where a baby is against one of these. Never "late" and never "missed" —
    /// the latest this says is that the range has passed, which for most babies means nothing
    /// at all and for the rest is a conversation with a health visitor, not with an app.
    /// </summary>
    public static string When(MilestoneGuide g, int? ageWeeks) => ageWeeks switch
    {
        null => $"usually {Band(g)}",
        var w when w < g.FromWeeks => $"usually {Band(g)}",
        var w when w <= g.ToWeeks => "around now, for many babies",
        _ => "many have by now; plenty later, which is just as usual"
    };

    private static string Band(MilestoneGuide g) =>
        g.FromWeeks >= 52
            ? $"between {Display.Months(g.FromWeeks)} and {Display.Months(g.ToWeeks)}"
            : $"between {g.FromWeeks} and {g.ToWeeks} weeks";

    public async Task<List<Milestone>> ForBabyAsync(int babyId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Milestones
            .Where(m => m.BabyId == babyId)
            .OrderByDescending(m => m.ReachedOn)
            .ThenByDescending(m => m.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Records one from the guide. Recording the same one twice moves the date rather than
    /// writing a second row — a first smile happens once, however many times it is tapped.
    /// </summary>
    public async Task ReachedAsync(
        int babyId, string key, DateOnly on, string? notes = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.Milestones.FirstOrDefaultAsync(m => m.BabyId == babyId && m.Key == key, ct);

        if (existing is null)
        {
            db.Milestones.Add(new Milestone { BabyId = babyId, Key = key, ReachedOn = on, Notes = Clean(notes) });
        }
        else
        {
            existing.ReachedOn = on;
            existing.Notes = Clean(notes) ?? existing.Notes;
        }

        await db.SaveChangesAsync(ct);
        NotifyChanged(babyId);
    }

    /// <summary>Records one somebody wrote themselves.</summary>
    public async Task AddOwnAsync(
        int babyId, string title, DateOnly on, string? notes = null, CancellationToken ct = default)
    {
        var cleaned = title.Trim();
        if (cleaned.Length == 0)
        {
            return;
        }

        if (cleaned.Length > 120)
        {
            cleaned = cleaned[..120];
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        db.Milestones.Add(new Milestone { BabyId = babyId, Title = cleaned, ReachedOn = on, Notes = Clean(notes) });
        await db.SaveChangesAsync(ct);
        NotifyChanged(babyId);
    }

    public async Task RemoveAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var babyId = await db.Milestones.Where(m => m.Id == id).Select(m => m.BabyId).FirstOrDefaultAsync(ct);
        if (babyId == 0)
        {
            return;
        }

        await db.Milestones.Where(m => m.Id == id).ExecuteDeleteAsync(ct);
        NotifyChanged(babyId);
    }

    /// <summary>
    /// The handful worth offering now: not yet recorded, with a range that is open at this age
    /// or opens within the next month. Earliest first.
    ///
    /// Only ones whose range is still open. This used to take anything whose range had started,
    /// which for an older baby with little recorded meant the newborn ones — smiling, following
    /// a face — filled every place, and the ones actually around now never appeared. Those
    /// older ones now have a section of their own: see <see cref="Earlier"/>.
    /// </summary>
    public static IEnumerable<MilestoneGuide> NextUp(
        IReadOnlyList<Milestone> reached, int? ageWeeks, int take = 4)
    {
        var candidates = NotRecorded(reached);

        return ageWeeks is { } w
            ? candidates.Where(g => g.ToWeeks >= w && g.FromWeeks <= w + 4).OrderBy(g => g.FromWeeks).Take(take)
            : candidates.Take(take);
    }

    /// <summary>
    /// The ones from earlier that were never written down: not recorded, and the range already
    /// closed. For filling in after the fact — a first smile that happened, was noticed, and
    /// never made it into the app.
    ///
    /// Without a birth date there is no "earlier" to speak of, so this is empty and the whole
    /// guide is the place to find them.
    /// </summary>
    public static IEnumerable<MilestoneGuide> Earlier(IReadOnlyList<Milestone> reached, int? ageWeeks) =>
        ageWeeks is { } w
            ? NotRecorded(reached).Where(g => g.ToWeeks < w).OrderBy(g => g.FromWeeks)
            : [];

    /// <summary>Whether a range has already closed at this age — and so recording it is a backfill.</summary>
    public static bool HasPassed(MilestoneGuide g, int? ageWeeks) => ageWeeks is { } w && g.ToWeeks < w;

    private static IEnumerable<MilestoneGuide> NotRecorded(IReadOnlyList<Milestone> reached)
    {
        var done = reached.Where(m => m.Key is not null).Select(m => m.Key!).ToHashSet();
        return Guide.Where(g => !done.Contains(g.Key));
    }

    private static string? Clean(string? notes) =>
        string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

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
                log.LogError(ex, "A page failed to handle a change to the milestones.");
            }
        }
    }
}
