using Simplz.Babytracker.Data;
using Simplz.Babytracker.Services;

namespace Simplz.Babytracker.Checks;

/// <summary>
/// The shape of the average day on Trends: the vote, the windows, the change figures — and the
/// day totals, which once counted the same sleep twice.
/// </summary>
internal static class DayShapeChecks
{
    public static Task RunAsync(Harness h)
    {
        TheShape(h);
        CountingOnce(h);
        return Task.CompletedTask;
    }

    private static void TheShape(Harness h)
    {
        h.Section("day shape: the vote and the windows");

        // Six days. Each night asleep 22:00 to 06:00; a 90 ml bottle in one tap at 07:00; a
        // twenty-minute breast feed at noon. The last three nights run half an hour longer.
        var dayZero = new DateTime(2026, 9, 1);
        var events = new List<BabyEvent>();
        for (var d = 0; d < 6; d++)
        {
            var day = dayZero.AddDays(d);
            var wake = day.AddDays(1).AddHours(6).AddMinutes(d >= 3 ? 30 : 0);
            events.Add(Sleep(day.AddHours(22), wake));
            events.Add(new BabyEvent
            {
                Kind = EventKind.BottleFeed,
                Milk = MilkKind.Formula,
                AmountMl = 90,
                StartUtc = Display.ToUtc(day.AddHours(7)),
                EndUtc = Display.ToUtc(day.AddHours(7))
            });
            events.Add(new BabyEvent
            {
                Kind = EventKind.BreastFeed,
                StartUtc = Display.ToUtc(day.AddHours(12)),
                EndUtc = Display.ToUtc(day.AddHours(12).AddMinutes(20))
            });
        }

        // The night before the range, so the first morning is asleep too.
        events.Add(Sleep(dayZero.AddHours(-2), dayZero.AddHours(6)));

        var shape = DayShape.Build(events, dayZero, dayZero.AddDays(5), Display.ToUtc(dayZero.AddDays(10)));

        h.Check("48 half-hours", shape.Slots.Count, 48);
        h.Check("six days counted", shape.DaysCounted, 6);
        h.Check("it is an average", shape.IsAverage, true);
        h.Check("03:00 asleep", shape.Slots[6].State, DayState.Asleep);
        h.Check("03:00 agreed by all six days", shape.Slots[6].DaysAgreeing, 6);
        h.Check("07:00 feeding — a one-tap bottle registers", shape.Slots[14].State, DayState.Feeding);
        h.Check("12:00 feeding", shape.Slots[24].State, DayState.Feeding);
        h.Check("15:00 awake", shape.Slots[30].State, DayState.Awake);
        h.Check("23:00 asleep", shape.Slots[46].State, DayState.Asleep);

        var night = shape.Windows.Single(w => w.State == DayState.Asleep);
        h.Check("the night is one window through midnight", night.WrapsMidnight, true);
        h.Check("it starts at 22:00", DayShape.Clock(night.Start), "22:00");
        h.Check("it ends at 06:00 — only two mornings ran to 06:30, four did not", DayShape.Clock(night.End), "06:00");
        h.Check("the later nights ran thirty minutes longer", night.DeltaMinutes, 30);
        h.Check("its length is the real stretch, not just the window", night.AvgRunMinutes >= 480, true);

        var morningFeed = shape.Windows.First(w => w.State == DayState.Feeding);
        h.Check("the morning bottle is a window at 07:00", DayShape.Clock(morningFeed.Start), "07:00");
        h.Check("with its average millilitres", morningFeed.AvgMl, 90);
        h.Check("there every day", morningFeed.Presence, 1.0);

        var noonFeed = shape.Windows.Last(w => w.State == DayState.Feeding);
        h.Check("a breast feed has no millilitres", noonFeed.AvgMl, null);
        h.Check("and no change, nothing having changed", noonFeed.DeltaMinutes, 0);

        h.Check("the list reads from where the night ends", DayShape.Clock(shape.Windows[0].Start), "06:00");
        h.Check("the windows tile the whole day", shape.Windows.Sum(w => (int)w.Length.TotalMinutes), 24 * 60);

        var three = DayShape.Build(events, dayZero, dayZero.AddDays(2), Display.ToUtc(dayZero.AddDays(10)));
        h.Check("over three days there is no change figure — one against one is a coin toss",
            three.Windows.All(w => w.DeltaMinutes is null), true);

        var one = DayShape.Build(events, dayZero, dayZero, Display.ToUtc(dayZero.AddDays(10)));
        h.Check("a single day is not an average", one.IsAverage, false);
        h.Check("and still shows the night", one.Slots[6].State, DayState.Asleep);

        var soFar = DayShape.Build(events, dayZero, dayZero, Display.ToUtc(dayZero.AddHours(12)));
        h.Check("today at noon: the afternoon is not counted", soFar.Slots[30].DaysCounted, 0);
        h.Check("but the morning is", soFar.Slots[6].DaysCounted, 1);

        h.Check("a clock past midnight reads as the clock does", DayShape.Clock(TimeSpan.FromHours(30)), "06:00");
    }

    private static void CountingOnce(Harness h)
    {
        h.Section("day shape: a day counted once");

        // Five days. Asleep 22:00 to 06:00 every night. In the afternoon: two days sleep 13:00 to
        // 17:00 unbroken; three sleep 13:00 to 13:30 and again 14:00 to 17:00.
        //
        // The vote at 13:30 is two asleep to three awake, so the average splits the afternoon
        // into two windows — while on two of the days it is one stretch. Each window measures
        // that stretch, correctly; adding the two counts it twice. That was the 1.20 bug.
        var start = new DateTime(2026, 10, 1);
        var events = new List<BabyEvent>();
        for (var d = 0; d < 5; d++)
        {
            var day = start.AddDays(d);
            events.Add(Sleep(day.AddHours(22), day.AddDays(1).AddHours(6)));

            if (d < 2)
            {
                events.Add(Sleep(day.AddHours(13), day.AddHours(17)));
            }
            else
            {
                events.Add(Sleep(day.AddHours(13), day.AddHours(13.5)));
                events.Add(Sleep(day.AddHours(14), day.AddHours(17)));
            }
        }

        events.Add(Sleep(start.AddHours(-2), start.AddHours(6)));

        var shape = DayShape.Build(events, start, start.AddDays(4), Display.ToUtc(start.AddDays(9)));

        // By hand: eight hours of night inside each day. Afternoons 240 twice and 210 three
        // times, so (480 + 630) / 5 = 222. 480 + 222 = 702.
        h.Check("asleep a day, each minute once", shape.AvgAsleepMinutes, 702);
        h.Check("nothing feeding", shape.AvgFeedingMinutes, 0);

        var afternoon = shape.Windows
            .Where(w => w.State == DayState.Asleep && w.Start >= TimeSpan.FromHours(12) && w.Start < TimeSpan.FromHours(18))
            .ToList();
        h.Check("the average splits the afternoon in two", afternoon.Count, 2);

        // 13:00-13:30: 240 on the unbroken days, 30 on the rest — (480 + 90) / 5 = 114.
        // 14:00-17:00: 240 on the unbroken days, 180 on the rest — (480 + 540) / 5 = 204.
        h.Check("the early window measures the stretch it is part of", afternoon[0].AvgRunMinutes, 114);
        h.Check("so does the later one", afternoon[1].AvgRunMinutes, 204);
        h.Check("together they overshoot a true 222 by exactly the double count",
            afternoon.Sum(w => w.AvgRunMinutes) - 222, 96);
        h.Check("which is why the total is not their sum",
            shape.Windows.Where(w => w.State == DayState.Asleep).Sum(w => w.AvgRunMinutes) > shape.AvgAsleepMinutes, true);

        // Where nothing overlaps, the per-day figure is the obvious one.
        var clean = new List<BabyEvent>();
        for (var d = 0; d < 4; d++)
        {
            var day = start.AddDays(d);
            clean.Add(Sleep(day.AddHours(22), day.AddDays(1).AddHours(6)));
            clean.Add(Sleep(day.AddHours(13), day.AddHours(15)));
        }

        clean.Add(Sleep(start.AddHours(-2), start.AddHours(6)));

        var cleanShape = DayShape.Build(clean, start, start.AddDays(3), Display.ToUtc(start.AddDays(9)));
        h.Check("eight hours of night and a two-hour nap", cleanShape.AvgAsleepMinutes, 600);

        var feeds = new List<BabyEvent>();
        for (var d = 0; d < 4; d++)
        {
            foreach (var hour in new[] { 8, 12, 16, 20 })
            {
                var at = start.AddDays(d).AddHours(hour);
                feeds.Add(new BabyEvent
                {
                    Kind = EventKind.BottleFeed,
                    Milk = MilkKind.Formula,
                    AmountMl = 100,
                    StartUtc = Display.ToUtc(at),
                    EndUtc = Display.ToUtc(at.AddMinutes(30))
                });
            }
        }

        var feedShape = DayShape.Build(feeds, start, start.AddDays(3), Display.ToUtc(start.AddDays(9)));
        h.Check("four half-hour bottles a day is two hours of feeding", feedShape.AvgFeedingMinutes, 120);

        var partial = DayShape.Build(clean, start, start, Display.ToUtc(start.AddHours(12)));
        h.Check("today so far counts only the hours lived", partial.AvgAsleepMinutes, 360);
    }

    private static BabyEvent Sleep(DateTime localStart, DateTime localEnd) => new()
    {
        Kind = EventKind.Sleep,
        StartUtc = Display.ToUtc(localStart),
        EndUtc = Display.ToUtc(localEnd)
    };
}
