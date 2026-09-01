using Simplz.Babytracker.Data;

namespace Simplz.Babytracker.Services;

/// <summary>One line of the chart: a feed and the nappies and spit-ups recorded around it.</summary>
public class ChartRow
{
    public DateTime StartUtc { get; init; }

    public List<BabyEvent> Breast { get; } = [];
    public List<BabyEvent> Bottles { get; } = [];
    public List<BabyEvent> Urine { get; } = [];
    public List<BabyEvent> Stools { get; } = [];
    public List<BabyEvent> Emesis { get; } = [];

    public int TotalMl => Bottles.Sum(b => b.AmountMl ?? 0);

    public TimeSpan BreastTime =>
        Breast.Where(b => b.Duration is not null).Aggregate(TimeSpan.Zero, (sum, b) => sum + b.Duration!.Value);

    public void Add(BabyEvent e)
    {
        switch (e.Kind)
        {
            case EventKind.BreastFeed: Breast.Add(e); break;
            case EventKind.BottleFeed: Bottles.Add(e); break;
            case EventKind.Urine: Urine.Add(e); break;
            case EventKind.Poop: Stools.Add(e); break;
            case EventKind.Vomit: Emesis.Add(e); break;
        }
    }
}

/// <summary>A day of chart rows, newest row first.</summary>
public record ChartDay(DateTime Date, List<ChartRow> Rows)
{
    public int Feeds => Rows.Sum(r => r.Breast.Count + r.Bottles.Count);
    public int Ml => Rows.Sum(r => r.TotalMl);
    public TimeSpan BreastTime => Rows.Aggregate(TimeSpan.Zero, (sum, r) => sum + r.BreastTime);
    public int Stools => Rows.Sum(r => r.Stools.Count);
    public int Urine => Rows.Sum(r => r.Urine.Count);
}

/// <summary>
/// Turns the flat event log back into something shaped like the paper chart: one row per feed,
/// with the outputs that followed it on the same line.
/// </summary>
public static class ChartGrouping
{
    /// <summary>A feed this close to the row's first entry is part of the same feed (breast plus a top-up bottle).</summary>
    public static readonly TimeSpan FeedWindow = TimeSpan.FromMinutes(20);

    /// <summary>A nappy or spit-up within this long after a feed is recorded against it.</summary>
    public static readonly TimeSpan OutputWindow = TimeSpan.FromHours(4);

    public static List<ChartDay> Build(IEnumerable<BabyEvent> events)
    {
        var rows = new List<ChartRow>();
        ChartRow? current = null;

        // Feeds first within the same minute: an output logged at the same time as a feed belongs
        // to that feed, not to the previous one.
        var ordered = events
            .OrderBy(x => x.StartUtc)
            .ThenByDescending(x => x.Kind is EventKind.BreastFeed or EventKind.BottleFeed)
            .ThenBy(x => x.Id);

        foreach (var e in ordered)
        {
            var window = e.Kind is EventKind.BreastFeed or EventKind.BottleFeed ? FeedWindow : OutputWindow;

            var fits = current is not null
                && e.StartUtc - current.StartUtc <= window
                && Display.ToLocal(e.StartUtc).Date == Display.ToLocal(current.StartUtc).Date;

            if (!fits)
            {
                current = new ChartRow { StartUtc = e.StartUtc };
                rows.Add(current);
            }

            current!.Add(e);
        }

        return rows
            .GroupBy(r => Display.ToLocal(r.StartUtc).Date)
            .OrderByDescending(g => g.Key)
            // Days newest first, but the rows inside a day read top-to-bottom like the paper chart.
            .Select(g => new ChartDay(g.Key, g.OrderBy(r => r.StartUtc).ToList()))
            .ToList();
    }
}
