using System.ComponentModel.DataAnnotations;

namespace Simplz.Babytracker.Data;

/// <summary>
/// Where the thermometer went. Worth keeping because the same baby reads differently by each,
/// and because whoever is asked about it later — the GP, 111 — will ask. Underarm first: it is
/// what the NHS suggests for a baby.
/// </summary>
public enum TemperatureMethod
{
    Underarm = 0,
    Ear = 1,
    Forehead = 2
}

/// <summary>One reading: when it was taken and what it said.</summary>
public class TemperatureReading
{
    public int Id { get; set; }

    public int BabyId { get; set; }

    /// <summary>When it was taken, which is not always when it was typed in.</summary>
    public DateTime TakenUtc { get; set; }

    /// <summary>Degrees Celsius, to one decimal place, as a thermometer shows it.</summary>
    public double Celsius { get; set; }

    /// <summary>Null when nobody said.</summary>
    public TemperatureMethod? Method { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }
}

/// <summary>
/// A medicine as written on the prescription or the label: what it is, how much, and the rules
/// for giving it. The rules are whatever was written down, typed in by hand — this app never
/// suggests a dose or an interval, it only holds the ones it is given and says when a dose
/// would break them.
/// </summary>
public class Medicine
{
    public int Id { get; set; }

    public int BabyId { get; set; }

    [MaxLength(80)]
    public string Name { get; set; } = "";

    /// <summary>How much at a time, as written — "2.5 ml", "one sachet". Free text on purpose.</summary>
    [MaxLength(60)]
    public string? Dose { get; set; }

    /// <summary>The shortest gap the label allows between two doses, in hours. Null for none given.</summary>
    public double? MinHoursBetween { get; set; }

    /// <summary>The most doses the label allows in any 24 hours. Null for none given.</summary>
    public int? MaxPerDay { get; set; }

    /// <summary>The last day of a course, for something prescribed for so many days.</summary>
    public DateOnly? EndsOn { get; set; }

    /// <summary>
    /// Put away by hand. A finished medicine keeps its doses — the history is the point — and
    /// moves out of the way of the ones being given now.
    /// </summary>
    public bool Finished { get; set; }

    /// <summary>What it is for, who prescribed it, anything else on the label.</summary>
    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime AddedUtc { get; set; }

    /// <summary>Being given now: not put away, and its course, if it has one, not over.</summary>
    public bool IsCurrent(DateOnly today) => !Finished && (EndsOn is not { } last || last >= today);
}

/// <summary>One dose of one medicine, and when it was given.</summary>
public class MedicineDose
{
    public int Id { get; set; }

    public int MedicineId { get; set; }

    public DateTime GivenUtc { get; set; }

    /// <summary>Only when this dose differed from the medicine's usual one. Null means the usual.</summary>
    [MaxLength(60)]
    public string? Dose { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }
}
