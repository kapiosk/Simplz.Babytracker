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
    /// A feed and a sleep can both be running at once; they have nothing to do with each other.
    /// </summary>
    public static bool Lasts(EventKind kind) => kind is EventKind.BreastFeed or EventKind.Sleep;

    public bool IsRunning => Lasts(Kind) && EndUtc is null;

    public TimeSpan? Duration => EndUtc is null ? null : EndUtc.Value - StartUtc;
}
