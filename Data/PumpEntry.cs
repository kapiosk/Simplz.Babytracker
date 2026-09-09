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

    [MaxLength(500)]
    public string? Notes { get; set; }

    public bool IsRunning => Kind == PumpEntryKind.Session && EndUtc is null;

    public TimeSpan? Duration => EndUtc is null ? null : EndUtc.Value - StartUtc;
}
