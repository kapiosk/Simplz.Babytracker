using System.ComponentModel.DataAnnotations;

namespace Simplz.Babytracker.Data;

public enum EventKind
{
    BreastFeed = 0,
    BottleFeed = 1,
    Poop = 2,
    Urine = 3,
    Vomit = 4,
    Sleep = 5
}

public enum MilkKind
{
    Formula = 0,
    BreastMilk = 1
}

/// <summary>
/// Which batch of breast milk: what was expressed in the morning or what was expressed at
/// night. They differ — night milk carries the melatonin — so bags get labelled and the night
/// ones get kept for the night. Always set by hand: the clock can say when a session happened,
/// but only whoever made up the bottle knows which bag it came from, and the boundary between
/// the two is theirs to draw.
/// </summary>
public enum MilkTime
{
    Morning = 0,
    Night = 1
}

public class BabyEvent
{
    public int Id { get; set; }

    /// <summary>Which baby the entry belongs to. See <see cref="Baby"/>.</summary>
    public int BabyId { get; set; }

    public EventKind Kind { get; set; }

    /// <summary>When the event happened (UTC). For the kinds that last, the moment it started.</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>Only for the kinds that last: when it stopped. Null while still running.</summary>
    public DateTime? EndUtc { get; set; }

    /// <summary>Only used by bottle feeding.</summary>
    public MilkKind? Milk { get; set; }

    /// <summary>Only used by bottle feeding: millilitres given.</summary>
    public int? AmountMl { get; set; }

    /// <summary>
    /// Only used by a bottle of breast milk: which batch it was made up from. Null when nobody
    /// said, which the stock ledger keeps as its own line rather than guessing a side.
    /// </summary>
    public MilkTime? MilkTime { get; set; }

    /// <summary>
    /// Only used by bottle feeding: when the feed was paused, while it is paused. Null the rest
    /// of the time, including once it has finished — ending a paused feed folds the last stretch
    /// into <see cref="PausedSeconds"/> and clears this, so a finished entry never reads as
    /// paused.
    /// </summary>
    public DateTime? PausedAtUtc { get; set; }

    /// <summary>
    /// Only used by bottle feeding: how long this feed has spent paused, across every pause so
    /// far, not counting one still running. Seconds rather than a TimeSpan because SQLite has no
    /// interval type and an integer column is one less thing to get wrong.
    /// </summary>
    public int PausedSeconds { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    /// <summary>
    /// The kinds that are a stretch of time rather than a moment, and so are started and stopped.
    /// Only one of them runs at a time: a baby cannot be feeding and asleep at once, so starting
    /// either ends the other. An array rather than a pattern so a query can use it too.
    /// </summary>
    public static readonly EventKind[] LastingKinds = [EventKind.BreastFeed, EventKind.Sleep, EventKind.BottleFeed];

    public static bool Lasts(EventKind kind) => LastingKinds.Contains(kind);

    /// <summary>
    /// The kinds that can be paused: the two sorts of feed. A sleep cannot — a baby who wakes and
    /// resettles is one sleep or two, not a paused one — which is also why only these two have a
    /// feeding time that can differ from the stretch between their start and their stop.
    /// </summary>
    public static bool CanPause(EventKind kind) =>
        kind is EventKind.BreastFeed or EventKind.BottleFeed;

    /// <summary>
    /// How much of a window was not spent doing the thing, given how much of it was:
    ///
    ///     stop − start  =  spent  +  paused
    ///
    /// The editors hold the spent figure and let this decide the pause, which is what stops
    /// nudging a stop time from rewriting how long a baby fed for.
    ///
    /// Clamped both ways. A pause cannot be negative, so a figure longer than its own window is
    /// taken to be the whole window — there is no fitting forty minutes of feeding into thirty.
    /// Shared with the pump editor, which does the same arithmetic on its own entries.
    /// </summary>
    public static int PausedSecondsFor(TimeSpan window, TimeSpan spent)
    {
        if (window < TimeSpan.Zero)
        {
            window = TimeSpan.Zero;
        }

        if (spent > window)
        {
            spent = window;
        }

        if (spent < TimeSpan.Zero)
        {
            spent = TimeSpan.Zero;
        }

        return (int)Math.Round((window - spent).TotalSeconds);
    }

    /// <summary>
    /// Below this, a sleep is taken to be a mis-tap rather than a nap. See
    /// <see cref="TooShortToKeep"/>.
    /// </summary>
    public static readonly TimeSpan ShortestSleep = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Whether a session that has just been stopped is short enough to be thrown away instead
    /// of recorded.
    ///
    /// Sleep only, deliberately. A sleep of three minutes is almost always the wrong button, or
    /// the right button and the baby did not settle, and either way it drags the averages down.
    /// A breast feed of three minutes is an ordinary thing — a comfort feed, a one-sided top-up —
    /// and discarding those would be throwing away real entries.
    ///
    /// Only the timer goes through this. An entry typed into the editor is somebody saying what
    /// happened on purpose, and is kept whatever its length.
    /// </summary>
    public static bool TooShortToKeep(EventKind kind, TimeSpan length) =>
        kind == EventKind.Sleep && length < ShortestSleep;

    public bool IsRunning => Lasts(Kind) && EndUtc is null;

    /// <summary>Paused right now — only ever true of a bottle feed that is still running.</summary>
    public bool IsPaused => IsRunning && PausedAtUtc is not null;

    /// <summary>
    /// Time actually spent feeding as at <paramref name="nowUtc"/>, with every pause taken out.
    ///
    /// Pauses come off rather than counting, which is the whole point of being able to pause: a
    /// bottle set down for twenty minutes while the baby is winded was not a fifty minute feed.
    /// The wall clock is still there in <see cref="StartUtc"/> and <see cref="EndUtc"/> for
    /// anyone who wants it.
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

        // Clock changes and hand-edited times can both put these the wrong way round, and a
        // negative duration further up reads as a corrupted entry rather than a short one.
        return spent < TimeSpan.Zero ? TimeSpan.Zero : spent;
    }

    /// <summary>
    /// Everything spent paused so far, counting a pause still running. The banked figure on its
    /// own would say "4m" during a pause that had already reached seven.
    /// </summary>
    public TimeSpan PausedFor(DateTime nowUtc)
    {
        var paused = TimeSpan.FromSeconds(PausedSeconds);
        return PausedAtUtc is { } since && nowUtc > since ? paused + (nowUtc - since) : paused;
    }

    public TimeSpan? Duration => EndUtc is null ? null : ElapsedAt(EndUtc.Value);
}
