using Simplz.Babytracker.Data;

namespace Simplz.Babytracker.Services;

/// <summary>What the baby is doing, as far as the log knows, in one stretch of the day.</summary>
public enum DayState
{
    Awake = 0,
    Asleep = 1,
    Feeding = 2
}

/// <summary>One half-hour of the average day: the state the baby was most often in then.</summary>
/// <param name="DaysAgreeing">How many of the counted days were in that state at that time.</param>
/// <param name="DaysCounted">
/// How many days had anything to say about this half-hour. Today's hours that have not
/// happened yet are not counted, so an afternoon strip drawn at noon is not half "awake".
/// </param>
public sealed record DaySlot(int Index, DayState State, int DaysAgreeing, int DaysCounted)
{
    public TimeSpan Start => TimeSpan.FromMinutes(Index * DayShape.SlotMinutes);
}

/// <summary>
/// A run of half-hours in the same state — a sleep, a feed, a stretch of being awake.
/// </summary>
/// <param name="End">Exclusive, and past 24h when the window runs through midnight.</param>
/// <param name="Presence">
/// How consistently the window is there: the mean over its slots of the share of days that
/// agreed with it. 1.0 is every day, every half-hour.
/// </param>
/// <param name="AvgMl">For a feed: the mean millilitres of the bottles given inside it.</param>
/// <param name="AvgRunMinutes">
/// How long this stretch actually lasted, on average — measured on each day as the real run of
/// this state around the window, not merely the part inside it. That is what makes the change
/// figure able to see a night growing past its window.
///
/// It is emphatically <em>not</em> this window's share of the day, and these must never be
/// summed. On a day where two windows the average keeps apart are really one unbroken stretch,
/// both measure that same stretch and report it — correctly, since each is answering "how long
/// is the stretch you are in at this hour". Adding them counts those minutes twice. For how much
/// of a day a state takes up, use <see cref="DayShapeResult.AvgAsleepMinutes"/>, which is
/// measured per day and cannot overlap.
/// </param>
/// <param name="MeasuredDays">
/// How many days the stretch could actually be measured on. A run reaching the edge of the
/// range, or hours not yet lived, is unknown rather than short, so it is left out — and a
/// change resting on very few days is worth knowing about.
/// </param>
/// <param name="DeltaMinutes">
/// The later half of the range against the earlier half, in minutes a day. Null when either
/// half has fewer than two measured days — one day against one day is a coin toss, not a trend.
/// </param>
public sealed record DayWindow(
    DayState State, TimeSpan Start, TimeSpan End, double Presence,
    int? AvgMl, int AvgRunMinutes, int MeasuredDays, int? DeltaMinutes)
{
    public TimeSpan Length => End - Start;

    public bool WrapsMidnight => End > TimeSpan.FromHours(24);
}

/// <param name="AvgAsleepMinutes">
/// Minutes a day spent asleep, averaged over the days in the range. Measured a day at a time
/// over every half-hour, so it counts each minute once and agrees with the sleep drawn on the
/// bar chart below it. The windows' own lengths cannot be summed to get here — see
/// <see cref="DayWindow.AvgRunMinutes"/>.
/// </param>
public sealed record DayShapeResult(
    IReadOnlyList<DaySlot> Slots, IReadOnlyList<DayWindow> Windows, int DaysCounted, bool IsAverage,
    int AvgAsleepMinutes, int AvgFeedingMinutes)
{
    public static readonly DayShapeResult Empty = new([], [], 0, false, 0, 0);
}

/// <summary>
/// The shape of an average day: for each half-hour, whether the baby was more often asleep,
/// feeding or awake across the days in a range, and how each stretch is changing.
///
/// Kept out of the component on purpose. The bucketing, the vote across days, the windows and
/// the deltas are the part that can be wrong in ways a screenshot will not show, so they live
/// where the harness can drive them.
///
/// Wall clock throughout, pauses included: a forty minute bottle with twenty paused is still
/// forty minutes of the day spent being fed. This is the day's shape, not the timer.
/// </summary>
public static class DayShape
{
    public const int SlotMinutes = 30;
    public const int SlotsPerDay = 24 * 60 / SlotMinutes;

    /// <summary>
    /// A bottle logged in one tap has no length at all, and would otherwise fail to mark its
    /// half-hour as feeding. It is given this much, all of it in the half-hour it happened in,
    /// and a half-hour with at least this much feeding in it counts as feeding. A real feed that
    /// only clips a slot by a few minutes on its way in or out does not flip that slot.
    /// </summary>
    public const int OneTapFeedMinutes = 10;

    /// <summary>Fewer measured days than this on either side and no delta is offered.</summary>
    public const int MinDaysPerHalf = 2;

    public static DayShapeResult Build(
        IReadOnlyList<BabyEvent> events, DateTime fromLocalDate, DateTime toLocalDate, DateTime nowUtc)
    {
        var days = new List<DateTime>();
        for (var d = fromLocalDate.Date; d <= toLocalDate.Date; d = d.AddDays(1))
        {
            days.Add(d);
        }

        if (days.Count == 0)
        {
            return DayShapeResult.Empty;
        }

        var nowLocal = Display.ToLocal(nowUtc);
        var grid = new Grid(days, nowLocal);

        foreach (var e in events)
        {
            var state = e.Kind switch
            {
                EventKind.Sleep => DayState.Asleep,
                EventKind.BreastFeed or EventKind.BottleFeed => DayState.Feeding,
                _ => DayState.Awake
            };

            if (state == DayState.Awake)
            {
                continue;
            }

            var start = Display.ToLocal(e.StartUtc);

            // Still running: it is happening right up to now. Never before it started.
            var end = e.EndUtc is { } endUtc ? Display.ToLocal(endUtc) : nowLocal;
            if (end < start)
            {
                end = start;
            }

            if (state == DayState.Feeding && end == start)
            {
                grid.PaintPoint(start, state, OneTapFeedMinutes);
            }
            else
            {
                grid.Paint(start, end, state);
            }
        }

        // One state per slot of the day: the vote across days.
        var slots = new List<DaySlot>(SlotsPerDay);
        for (var s = 0; s < SlotsPerDay; s++)
        {
            var votes = new int[3];
            var total = new int[3];
            var counted = 0;

            for (var d = 0; d < days.Count; d++)
            {
                if (!grid.Counted(d, s))
                {
                    continue;
                }

                counted++;
                votes[(int)grid.StateOf(d, s)]++;
                total[(int)DayState.Asleep] += grid.Minutes(d, s, DayState.Asleep);
                total[(int)DayState.Feeding] += grid.Minutes(d, s, DayState.Feeding);
            }

            if (counted == 0)
            {
                slots.Add(new DaySlot(s, DayState.Awake, 0, 0));
                continue;
            }

            // Most votes; a tie goes to whichever state had more minutes overall.
            var best = DayState.Awake;
            var bestVotes = votes[0];
            foreach (var candidate in new[] { DayState.Feeding, DayState.Asleep })
            {
                var v = votes[(int)candidate];
                if (v > bestVotes || (v == bestVotes && v > 0 && total[(int)candidate] > total[(int)best]))
                {
                    (best, bestVotes) = (candidate, v);
                }
            }

            slots.Add(new DaySlot(s, best, bestVotes, counted));
        }

        // How much of a day each state takes up: summed per day over the half-hours that day
        // reached, then averaged. Every minute lands in exactly one half-hour of one day, so
        // this counts each of them once — which summing the windows does not.
        var asleepPerDay = new List<int>();
        var feedingPerDay = new List<int>();
        for (var d = 0; d < days.Count; d++)
        {
            var a = 0;
            var f = 0;
            var any = false;

            for (var s = 0; s < SlotsPerDay; s++)
            {
                if (!grid.Counted(d, s))
                {
                    continue;
                }

                any = true;
                a += grid.Minutes(d, s, DayState.Asleep);
                f += grid.Minutes(d, s, DayState.Feeding);
            }

            if (any)
            {
                asleepPerDay.Add(a);
                feedingPerDay.Add(f);
            }
        }

        var windows = Windows(slots, grid, events);
        return new DayShapeResult(
            slots, windows, days.Count, days.Count > 1,
            asleepPerDay.Count == 0 ? 0 : (int)Math.Round(asleepPerDay.Average()),
            feedingPerDay.Count == 0 ? 0 : (int)Math.Round(feedingPerDay.Average()));
    }

    private static List<DayWindow> Windows(List<DaySlot> slots, Grid grid, IReadOnlyList<BabyEvent> events)
    {
        // Runs of the same state across the average day.
        var runs = new List<(DayState State, int First, int Last)>();
        var i = 0;
        while (i < slots.Count)
        {
            var j = i;
            while (j + 1 < slots.Count && slots[j + 1].State == slots[i].State)
            {
                j++;
            }

            runs.Add((slots[i].State, i, j));
            i = j + 1;
        }

        // A night's sleep is one thing even though the day boundary cuts it in two. If the day
        // ends and begins in the same state, the last run wraps onto the first.
        var wrapped = false;
        if (runs.Count > 1 && runs[0].State == runs[^1].State)
        {
            var last = runs[^1];
            var first = runs[0];
            runs.RemoveAt(runs.Count - 1);
            runs[0] = (first.State, last.First, first.Last + SlotsPerDay);
            wrapped = true;
        }

        var dayCount = grid.DayCount;
        var laterFrom = dayCount / 2 + dayCount % 2;   // the odd day goes to the earlier half
        var canDelta = laterFrom >= MinDaysPerHalf && dayCount - laterFrom >= MinDaysPerHalf;

        var result = new List<DayWindow>(runs.Count);
        foreach (var (state, first, last) in runs)
        {
            var slotIndexes = Enumerable.Range(first, last - first + 1).Select(s => s % SlotsPerDay).ToList();

            // Presence: how much of the range agreed with this window, slot by slot.
            var presence = slotIndexes
                .Select(s => slots[s])
                .Where(sl => sl.DaysCounted > 0)
                .Select(sl => (double)sl.DaysAgreeing / sl.DaysCounted)
                .DefaultIfEmpty(0)
                .Average();

            // How long the stretch really was on each day. The window is only the seed: on each
            // day the run of this state is followed outwards from it in both directions, so a
            // night that ends later on later days is measured as longer, not as the same window
            // with the extra outside it. A night starts on the evening it begins, and runs into
            // the next day; the last evening in the range has no next day, so it is not measured.
            var perDay = new List<(int Day, int Minutes)>();
            for (var d = 0; d < dayCount; d++)
            {
                if (grid.MeasureRun(d, first, last, state) is { } minutes)
                {
                    perDay.Add((d, minutes));
                }
            }

            var avgMinutes = perDay.Count == 0 ? 0 : (int)Math.Round(perDay.Average(p => p.Minutes));

            int? delta = null;
            if (canDelta)
            {
                var earlier = perDay.Where(p => p.Day < laterFrom).Select(p => p.Minutes).ToList();
                var later = perDay.Where(p => p.Day >= laterFrom).Select(p => p.Minutes).ToList();
                if (earlier.Count >= MinDaysPerHalf && later.Count >= MinDaysPerHalf)
                {
                    delta = (int)Math.Round(later.Average() - earlier.Average());
                }
            }

            int? avgMl = null;
            if (state == DayState.Feeding)
            {
                var startMinute = first * SlotMinutes;
                var endMinute = (last + 1) * SlotMinutes;
                var mls = events
                    .Where(e => e.Kind == EventKind.BottleFeed && e.AmountMl is > 0)
                    .Select(e => (Ml: e.AmountMl!.Value, Minute: MinuteOfDay(Display.ToLocal(e.StartUtc))))
                    .Where(x => InWindow(x.Minute, startMinute, endMinute))
                    .Select(x => x.Ml)
                    .ToList();
                if (mls.Count > 0)
                {
                    avgMl = (int)Math.Round(mls.Average());
                }
            }

            result.Add(new DayWindow(
                state,
                TimeSpan.FromMinutes(first * SlotMinutes),
                TimeSpan.FromMinutes((last + 1) * SlotMinutes),
                presence, avgMl, avgMinutes, perDay.Count, delta));
        }

        // Reading order is the day as it is lived: from waking, round to the night. So the
        // wrapped night goes last rather than sitting at the top because it touches midnight.
        if (wrapped && result.Count > 1)
        {
            var night = result[0];
            result.RemoveAt(0);
            result.Add(night);
        }

        return result;
    }

    private static int MinuteOfDay(DateTime local) => local.Hour * 60 + local.Minute;

    /// <summary>Whether a minute of the day falls inside a window that may run past midnight.</summary>
    private static bool InWindow(int minute, int startMinute, int endMinute) =>
        endMinute <= 24 * 60
            ? minute >= startMinute && minute < endMinute
            : minute >= startMinute || minute < endMinute - 24 * 60;

    public static string Label(DayState state) => state switch
    {
        DayState.Asleep => "asleep",
        DayState.Feeding => "feeding",
        _ => "awake"
    };

    /// <summary>"21:30" — and "05:45" for a time past midnight, since that is what the clock said.</summary>
    public static string Clock(TimeSpan t)
    {
        var minutes = ((int)t.TotalMinutes) % (24 * 60);
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }

    /// <summary>
    /// Minutes of each state in each half-hour of each day, addressed either as (day, slot) or
    /// as one linear index across the whole range so a run can be followed past midnight into
    /// the next day.
    /// </summary>
    private sealed class Grid
    {
        private readonly List<DateTime> days;
        private readonly DateTime nowLocal;
        private readonly int[] asleep;
        private readonly int[] feeding;

        public Grid(List<DateTime> days, DateTime nowLocal)
        {
            this.days = days;
            this.nowLocal = nowLocal;
            asleep = new int[days.Count * SlotsPerDay];
            feeding = new int[days.Count * SlotsPerDay];
        }

        public int DayCount => days.Count;

        private int Length => asleep.Length;

        private static int Linear(int day, int slot) => day * SlotsPerDay + slot;

        public int Minutes(int day, int slot, DayState state) => MinutesAt(Linear(day, slot), state);

        private int MinutesAt(int at, DayState state) => state switch
        {
            DayState.Asleep => asleep[at],
            DayState.Feeding => feeding[at],
            _ => Math.Max(0, SlotMinutes - asleep[at] - feeding[at])
        };

        /// <summary>A half-hour counts only once the day has reached it.</summary>
        public bool Counted(int day, int slot) => CountedAt(Linear(day, slot));

        private bool CountedAt(int at) =>
            at >= 0 && at < Length && days[at / SlotsPerDay].AddMinutes((at % SlotsPerDay) * SlotMinutes) <= nowLocal;

        public DayState StateOf(int day, int slot) => StateAt(Linear(day, slot));

        private DayState StateAt(int at)
        {
            var a = asleep[at];
            var f = feeding[at];
            var w = Math.Max(0, SlotMinutes - a - f);

            // Feeding is the rarest thing and the one a parent is looking for, so a half-hour
            // with a feed of any real length in it is a feeding half-hour, even where most of
            // the rest was sleep or being awake. Otherwise the biggest share wins.
            if (f >= OneTapFeedMinutes || (f >= a && f > w)) return DayState.Feeding;
            if (a > f && a > w) return DayState.Asleep;
            if (a > 0 && a == w) return DayState.Asleep;
            return DayState.Awake;
        }

        /// <summary>Adds a stretch of one state, clipped to the range and to each half-hour.</summary>
        public void Paint(DateTime start, DateTime end, DayState state)
        {
            var target = state == DayState.Asleep ? asleep : feeding;
            var rangeStart = days[0];
            var rangeEnd = days[^1].AddDays(1);

            var from = start > rangeStart ? start : rangeStart;
            var to = end < rangeEnd ? end : rangeEnd;
            if (to <= from)
            {
                return;
            }

            var firstAt = (int)((from - rangeStart).TotalMinutes / SlotMinutes);
            var lastAt = Math.Min(Length - 1, (int)Math.Ceiling((to - rangeStart).TotalMinutes / SlotMinutes) - 1);

            for (var at = firstAt; at <= lastAt; at++)
            {
                var slotStart = rangeStart.AddMinutes(at * SlotMinutes);
                var slotEnd = slotStart.AddMinutes(SlotMinutes);
                var a = from > slotStart ? from : slotStart;
                var b = to < slotEnd ? to : slotEnd;
                var overlap = (int)Math.Round((b - a).TotalMinutes);
                if (overlap > 0)
                {
                    target[at] = Math.Min(SlotMinutes, target[at] + overlap);
                }
            }
        }

        /// <summary>A moment with no length, given a nominal length in the half-hour it fell in.</summary>
        public void PaintPoint(DateTime at, DayState state, int minutes)
        {
            var rangeStart = days[0];
            if (at < rangeStart || at >= days[^1].AddDays(1))
            {
                return;
            }

            var index = (int)((at - rangeStart).TotalMinutes / SlotMinutes);
            var target = state == DayState.Asleep ? asleep : feeding;
            target[index] = Math.Min(SlotMinutes, target[index] + minutes);
        }

        /// <summary>
        /// How long the real stretch of <paramref name="state"/> around a window lasted on one
        /// day, in minutes — or null where it cannot be known: the window runs off the end of
        /// the range, or into hours that have not happened yet.
        /// </summary>
        public int? MeasureRun(int day, int firstSlot, int lastSlot, DayState state)
        {
            var from = Linear(day, firstSlot);
            var to = Linear(day, lastSlot);     // may run into the next day when the window wraps

            if (to >= Length)
            {
                return null;
            }

            // Every half-hour of the seed must be known, or there is nothing to measure.
            for (var at = from; at <= to; at++)
            {
                if (!CountedAt(at))
                {
                    return null;
                }
            }

            // Follow the run outwards. Reaching the edge of what is known means the true length
            // is not known either, so the day is left out rather than measured short.
            var lo = from;
            while (lo - 1 >= 0 && StateAt(lo - 1) == state)
            {
                if (!CountedAt(lo - 1))
                {
                    return null;
                }

                lo--;
            }

            if (lo == 0 && StateAt(lo) == state)
            {
                return null;   // ran off the start of the range: it began before we were looking
            }

            var hi = to;
            while (hi + 1 < Length && StateAt(hi + 1) == state)
            {
                if (!CountedAt(hi + 1))
                {
                    return null;
                }

                hi++;
            }

            if (hi == Length - 1 && StateAt(hi) == state)
            {
                return null;   // ran off the end of the range, or into the unknown
            }

            var sum = 0;
            for (var at = lo; at <= hi; at++)
            {
                sum += MinutesAt(at, state);
            }

            return sum;
        }
    }
}
