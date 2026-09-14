using System.ComponentModel.DataAnnotations;

namespace Simplz.Babytracker.Data;

public enum PumpEntryKind
{
    /// <summary>A pumping session: a stretch of time, and how much came out of it.</summary>
    Session = 0,

    /// <summary>
    /// The stock was counted or corrected by hand. <see cref="PumpEntry.AmountMl"/> is what the
    /// stock <em>is</em> from this moment on, not an amount added to it.
    /// </summary>
    StockSet = 1
}

/// <summary>
/// Pumping, and the milk it puts in the fridge.
///
/// Deliberately its own table rather than another <see cref="EventKind"/>. Pumping is something
/// the mother does, not something that happened to the baby, and every page in the app reads the
/// baby's log by range — the report, the chart, the trends. A new kind in there would have to be
/// filtered out of all of them, and forgetting it in one place would put pumping in the baby's
/// feed history, where it is simply wrong.
///
/// It also keeps <see cref="BabyEvent.LastingKinds"/> alone, which carries two meanings at once:
/// "has a duration" and "cannot run alongside the others". A pump session has a duration and can
/// perfectly well run while the baby is asleep, so it belongs to the first meaning and not the
/// second — and there is no way to say that in one array.
/// </summary>
public class PumpEntry
{
    public int Id { get; set; }

    /// <summary>Whose supply, so the stock is per baby as everything else here is.</summary>
    public int BabyId { get; set; }

    public PumpEntryKind Kind { get; set; }

    /// <summary>When the session started, or when the stock was set.</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>Only for a session: when it stopped. Null while it is still running.</summary>
    public DateTime? EndUtc { get; set; }

    /// <summary>Millilitres — pumped, for a session; the whole stock, for a correction.</summary>
    public int? AmountMl { get; set; }

    /// <summary>
    /// Only for a session: when it was paused, while it is paused. Cleared when it stops, the
    /// last stretch going into <see cref="PausedSeconds"/>, so a finished session never reads
    /// as paused. Mirrors the pair on <see cref="BabyEvent"/>, which does the same job there.
    /// </summary>
    public DateTime? PausedAtUtc { get; set; }

    /// <summary>
    /// Only for a session: how long it has spent paused across every pause so far, not counting
    /// one still running. Seconds, because SQLite has no interval type.
    /// </summary>
    public int PausedSeconds { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public bool IsRunning => Kind == PumpEntryKind.Session && EndUtc is null;

    public bool IsPaused => IsRunning && PausedAtUtc is not null;

    /// <summary>
    /// Time actually spent pumping as at <paramref name="nowUtc"/>, with pauses taken out. The
    /// wall clock stays in <see cref="StartUtc"/> and <see cref="EndUtc"/> for anyone who wants it.
    /// </summary>
    public TimeSpan ElapsedAt(DateTime nowUtc)
    {
        var end = EndUtc ?? nowUtc;
        var paused = TimeSpan.FromSeconds(PausedSeconds);

        if (PausedAtUtc is { } since && end > since)
        {
            paused += end - since;
        }

        var spent = end - StartUtc - paused;

        // Hand-edited times can put these the wrong way round, and a negative duration further
        // up reads as a corrupted entry rather than a short one.
        return spent < TimeSpan.Zero ? TimeSpan.Zero : spent;
    }

    /// <summary>Everything spent paused so far, counting a pause still running.</summary>
    public TimeSpan PausedFor(DateTime nowUtc)
    {
        var paused = TimeSpan.FromSeconds(PausedSeconds);
        return PausedAtUtc is { } since && nowUtc > since ? paused + (nowUtc - since) : paused;
    }

    public TimeSpan? Duration => EndUtc is null ? null : ElapsedAt(EndUtc.Value);
}
