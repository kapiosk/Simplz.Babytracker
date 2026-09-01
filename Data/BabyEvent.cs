using System.ComponentModel.DataAnnotations;

namespace Simplz.Babytracker.Data;

public enum EventKind
{
    BreastFeed = 0,
    BottleFeed = 1,
    Poop = 2,
    Urine = 3,
    Vomit = 4
}

public enum MilkKind
{
    Formula = 0,
    BreastMilk = 1
}

public class BabyEvent
{
    public int Id { get; set; }

    public EventKind Kind { get; set; }

    /// <summary>When the event happened (UTC). For breast feeding this is the moment feeding started.</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>Only used by breast feeding: when feeding stopped. Null while a session is still running.</summary>
    public DateTime? EndUtc { get; set; }

    /// <summary>Only used by bottle feeding.</summary>
    public MilkKind? Milk { get; set; }

    /// <summary>Only used by bottle feeding: millilitres given.</summary>
    public int? AmountMl { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public bool IsRunning => Kind == EventKind.BreastFeed && EndUtc is null;

    public TimeSpan? Duration => EndUtc is null ? null : EndUtc.Value - StartUtc;
}
