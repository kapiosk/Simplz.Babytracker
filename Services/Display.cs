using Simplz.Babytracker.Data;

namespace Simplz.Babytracker.Services;

/// <summary>Presentation helpers shared by the button grid, the log and the report.</summary>
public static class Display
{
    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.Local);

    public static DateTime ToUtc(DateTime local)
    {
        var wallClock = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        // On the morning the clocks go forward an hour simply does not exist locally, and
        // converting a reading from inside it throws. Someone writing down 03:30 that morning
        // meant 04:30 — the clock had already moved on — so read it against standard time.
        if (TimeZoneInfo.Local.IsInvalidTime(wallClock))
        {
            return DateTime.SpecifyKind(wallClock - TimeZoneInfo.Local.BaseUtcOffset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(wallClock, TimeZoneInfo.Local);
    }

    /// <summary>
    /// The moment an entry is shown against.
    ///
    /// Everything is shown by when it started, except a bottle, which is shown by when it
    /// finished. The start of a bottle is roughly when somebody picked it up; the end is when
    /// the baby stopped drinking, which is the moment worth reading off the log. A bottle
    /// logged in one tap ends where it starts, so nothing changes for those.
    ///
    /// Display only. Grouping and ordering stay on <see cref="BabyEvent.StartUtc"/> — the day
    /// an entry belongs to, the order of the log, the buckets on the trends. The one visible
    /// consequence is a timed bottle running through midnight, which is counted under the day
    /// it began and shown with the time it ended.
    /// </summary>
    public static DateTime Stamp(BabyEvent e) =>
        e.Kind == EventKind.BottleFeed && e.EndUtc is { } end ? end : e.StartUtc;

    public static string Label(BabyEvent e) => e.Kind switch
    {
        EventKind.BreastFeed => "Breast feed",
        EventKind.BottleFeed => e.Milk switch
        {
            MilkKind.Formula => "Bottle · formula",
            MilkKind.BreastMilk when e.MilkTime is { } batch => $"Bottle · {Batch(batch)} milk",
            MilkKind.BreastMilk => "Bottle · breast milk",
            _ => "Bottle"
        },
        EventKind.Poop => "Poop",
        EventKind.Urine => "Urine",
        EventKind.Vomit => "Vomit",
        EventKind.Sleep => "Sleep",
        _ => e.Kind.ToString()
    };

    public static string Label(EventKind kind) => kind switch
    {
        EventKind.BreastFeed => "Breast feed",
        EventKind.BottleFeed => "Bottle feed",
        EventKind.Poop => "Poop",
        EventKind.Urine => "Urine",
        EventKind.Vomit => "Vomit",
        EventKind.Sleep => "Sleep",
        _ => kind.ToString()
    };

    /// <summary>The batch, lower case, for the middle of a sentence: "night milk".</summary>
    public static string Batch(MilkTime batch) => batch == MilkTime.Night ? "night" : "morning";

    /// <summary>CSS class suffix used for the per-kind colour scheme.</summary>
    public static string Css(EventKind kind) => kind switch
    {
        EventKind.BreastFeed => "breast",
        EventKind.BottleFeed => "bottle",
        EventKind.Poop => "poop",
        EventKind.Urine => "urine",
        EventKind.Vomit => "vomit",
        EventKind.Sleep => "sleep",
        _ => "other"
    };

    public static string Clock(TimeSpan t) =>
        t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";

    /// <summary>Compact duration such as "12m" or "1h 04m".</summary>
    public static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : $"{(int)t.TotalMinutes}m";

    /// <summary>
    /// Shorter still — "12h20", "45m" — for the one place that has no room for the spaces:
    /// under a bar on the trends page, where a day's sleep has about 42px to fit into.
    /// </summary>
    public static string Compact(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h{t.Minutes:00}" : $"{(int)t.TotalMinutes}m";

    public static string Ago(DateTime utc)
    {
        var t = DateTime.UtcNow - utc;
        if (t.TotalMinutes < 1) return "just now";
        if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes}m ago";
        if (t.TotalHours < 24) return $"{(int)t.TotalHours}h {(int)t.Minutes}m ago";
        return $"{(int)t.TotalDays}d ago";
    }
}
