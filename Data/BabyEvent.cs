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

    [MaxLength(500)]
    public string? Notes { get; set; }

    /// <summary>
    /// The kinds that are a stretch of time rather than a moment, and so are started and stopped.
    /// Only one of them runs at a time: a baby cannot be feeding and asleep at once, so starting
    /// either ends the other. An array rather than a pattern so a query can use it too.
    /// </summary>
    public static readonly EventKind[] LastingKinds = [EventKind.BreastFeed, EventKind.Sleep];

    public static bool Lasts(EventKind kind) => LastingKinds.Contains(kind);

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

    public TimeSpan? Duration => EndUtc is null ? null : EndUtc.Value - StartUtc;
}
