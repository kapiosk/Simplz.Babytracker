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

    public static string Label(BabyEvent e) => e.Kind switch
    {
        EventKind.BreastFeed => "Breast feed",
        EventKind.BottleFeed => e.Milk switch
        {
            MilkKind.Formula => "Bottle · formula",
            MilkKind.BreastMilk => "Bottle · breast milk",
            _ => "Bottle"
        },
        EventKind.Poop => "Poop",
        EventKind.Urine => "Urine",
        EventKind.Vomit => "Vomit",
        _ => e.Kind.ToString()
    };

    public static string Label(EventKind kind) => kind switch
    {
        EventKind.BreastFeed => "Breast feed",
        EventKind.BottleFeed => "Bottle feed",
        EventKind.Poop => "Poop",
        EventKind.Urine => "Urine",
        EventKind.Vomit => "Vomit",
        _ => kind.ToString()
    };

    /// <summary>CSS class suffix used for the per-kind colour scheme.</summary>
    public static string Css(EventKind kind) => kind switch
    {
        EventKind.BreastFeed => "breast",
        EventKind.BottleFeed => "bottle",
        EventKind.Poop => "poop",
        EventKind.Urine => "urine",
        EventKind.Vomit => "vomit",
        _ => "other"
    };

    public static string Clock(TimeSpan t) =>
        t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";

    /// <summary>Compact duration such as "12m" or "1h 04m".</summary>
    public static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : $"{(int)t.TotalMinutes}m";

    public static string Ago(DateTime utc)
    {
        var t = DateTime.UtcNow - utc;
        if (t.TotalMinutes < 1) return "just now";
        if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes}m ago";
        if (t.TotalHours < 24) return $"{(int)t.TotalHours}h {(int)t.Minutes}m ago";
        return $"{(int)t.TotalDays}d ago";
    }
}
