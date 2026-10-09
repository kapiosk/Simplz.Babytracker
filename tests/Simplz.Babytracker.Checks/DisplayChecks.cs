using Simplz.Babytracker.Data;
using Simplz.Babytracker.Services;

namespace Simplz.Babytracker.Checks;

/// <summary>How things are said: which time an entry is shown by, durations, and ages.</summary>
internal static class DisplayChecks
{
    public static async Task RunAsync(Harness h)
    {
        h.Section("display: the time an entry is shown by");
        await h.ResetAsync();

        var timed = await h.Events.StartBottleAsync(h.BabyId, MilkKind.Formula, null, null, null);
        await h.BackdateAsync(timed.Started.Id, TimeSpan.FromMinutes(20));
        await h.Events.StopBottleAsync(timed.Started.Id, MilkKind.Formula, 100, null, null);
        var finished = await h.EventAsync(timed.Started.Id);
        h.Check("a timed bottle is shown by when it finished", Display.Stamp(finished), finished.EndUtc);
        h.Check("which really is a different time", finished.EndUtc == finished.StartUtc, false);

        var tapped = await h.Events.LogBottleAsync(h.BabyId, MilkKind.BreastMilk, 60);
        h.Check("a one-tap bottle by when it was logged", Display.Stamp(tapped), tapped.StartUtc);

        var running = await h.Events.StartBottleAsync(h.BabyId, MilkKind.Formula, null, null, null);
        h.Check("a running bottle, with no end yet, by its start", Display.Stamp(running.Started), running.Started.StartUtc);

        await h.ResetAsync();
        var feed = await h.Events.StartAsync(h.BabyId, EventKind.BreastFeed);
        await h.BackdateAsync(feed.Started.Id, TimeSpan.FromMinutes(20));
        await h.Events.StopAsync(feed.Started.Id);
        var fed = await h.EventAsync(feed.Started.Id);
        h.Check("a breast feed is still shown by its start", Display.Stamp(fed), fed.StartUtc);

        var poop = await h.Events.LogAsync(h.BabyId, EventKind.Poop);
        h.Check("a moment by its own time", Display.Stamp(poop), poop.StartUtc);

        h.Section("display: durations");
        h.Check("under an hour", Display.Duration(TimeSpan.FromMinutes(12)), "12m");
        h.Check("over an hour", Display.Duration(TimeSpan.FromMinutes(64)), "1h 04m");
        h.Check("compact, for under a bar", Display.Compact(TimeSpan.FromMinutes(740)), "12h20");
        h.Check("compact under an hour", Display.Compact(TimeSpan.FromMinutes(45)), "45m");

        h.Section("display: age");
        var born = new DateOnly(2026, 1, 15);

        // Days for the first fortnight, then weeks, then months, then years.
        h.Check("the day itself", Display.Age(born, born), "born today");
        h.Check("one day", Display.Age(born, born.AddDays(1)), "1 day");
        h.Check("nine days", Display.Age(born, born.AddDays(9)), "9 days");
        h.Check("a fortnight turns to weeks", Display.Age(born, born.AddDays(14)), "2 weeks");
        h.Check("ten weeks", Display.Age(born, born.AddDays(70)), "10 weeks");
        h.Check("weeks run to fourteen", Display.Age(born, born.AddDays(97)), "13 weeks");
        h.Check("then months — 98 days is three months and eight days", Display.Age(born, born.AddDays(98)), "3 months 1w");
        h.Check("a whole month reads clean", Display.Age(born, new DateOnly(2026, 5, 15)), "4 months");
        h.Check("months and a bit", Display.Age(born, new DateOnly(2026, 5, 29)), "4 months 2w");
        h.Check("the day before a monthly anniversary does not round up",
            Display.Age(born, new DateOnly(2026, 6, 14)), "4 months 4w");
        h.Check("a year is still months", Display.Age(born, new DateOnly(2027, 1, 15)), "12 months");
        h.Check("two years", Display.Age(born, new DateOnly(2028, 1, 15)), "2 years");
        h.Check("years and months", Display.Age(born, new DateOnly(2028, 4, 15)), "2y 3m");
        h.Check("before the birth itself", Display.Age(born, born.AddDays(-1)), "due");

        var endOfMonth = new DateOnly(2026, 1, 31);
        h.Check("born on the 31st, read on the 28th of a short month",
            Display.Age(endOfMonth, new DateOnly(2026, 2, 28)), "4 weeks");
        h.Check("born on the 31st, read at the end of a 30-day month",
            Display.Age(endOfMonth, new DateOnly(2026, 6, 30)), "4 months 4w");

        var leapDay = new DateOnly(2024, 2, 29);
        h.Check("a leap-day baby the day before a year without one",
            Display.Age(leapDay, new DateOnly(2025, 2, 28)), "11 months 4w");
        h.Check("and the day after", Display.Age(leapDay, new DateOnly(2025, 3, 1)), "12 months");

        h.Check("age in weeks", Display.AgeWeeks(born, born.AddDays(70)), 10);
        h.Check("age in weeks is never negative", Display.AgeWeeks(born, born.AddDays(-30)), 0);
        h.Check("weeks as months", Display.Months(52), "12 months");
        h.Check("weeks as years", Display.Months(104), "2 years");
    }
}
