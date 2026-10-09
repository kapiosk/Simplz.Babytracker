using Simplz.Babytracker.Data;

namespace Simplz.Babytracker.Checks;

/// <summary>
/// Editing a stop time must not rewrite the feeding time: stop − start = spent + paused, with
/// the spent figure held and the pause taking the difference.
/// </summary>
internal static class EditorChecks
{
    public static async Task RunAsync(Harness h)
    {
        h.Section("editor: the arithmetic");

        h.Check("the pause is the slack", BabyEvent.PausedSecondsFor(TimeSpan.FromMinutes(40), TimeSpan.FromMinutes(20)) / 60, 20);
        h.Check("no slack when all of it counts", BabyEvent.PausedSecondsFor(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(25)), 0);
        h.Check("a figure longer than its window is trimmed, not a negative pause",
            BabyEvent.PausedSecondsFor(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(40)), 0);
        h.Check("a backwards window cannot make a negative pause",
            BabyEvent.PausedSecondsFor(TimeSpan.FromMinutes(-5), TimeSpan.FromMinutes(10)), 0);

        h.Check("sleep cannot pause", BabyEvent.CanPause(EventKind.Sleep), false);
        h.Check("bottles can", BabyEvent.CanPause(EventKind.BottleFeed), true);
        h.Check("breast feeds can", BabyEvent.CanPause(EventKind.BreastFeed), true);

        h.Section("editor: moving the stop time of a feed");
        await h.ResetAsync();

        // A forty-minute window, twenty of it paused: twenty minutes of feeding.
        var bottle = await h.Events.StartBottleAsync(h.BabyId, MilkKind.Formula, 100, null, null);
        await h.BackdateAsync(bottle.Started.Id, TimeSpan.FromMinutes(40));
        await h.Events.StopBottleAsync(bottle.Started.Id, MilkKind.Formula, 100, null, null);

        await using (var db = await h.Factory.CreateDbContextAsync())
        {
            var e = await db.Events.FindAsync(bottle.Started.Id);
            e!.PausedSeconds = 20 * 60;
            await db.SaveChangesAsync();
        }

        var before = await h.EventAsync(bottle.Started.Id);
        h.Check("set up: twenty minutes of feeding", (int)before.Duration!.Value.TotalMinutes, 20);

        // Pull the stop back ten minutes the way the editor does: the feeding time is held.
        var earlierEnd = before.EndUtc!.Value.AddMinutes(-10);
        await h.Events.UpdateAsync(Edited(before, earlierEnd,
            BabyEvent.PausedSecondsFor(earlierEnd - before.StartUtc, before.Duration!.Value)));

        var moved = await h.EventAsync(bottle.Started.Id);
        h.Check("the feeding time survives the stop moving", (int)moved.Duration!.Value.TotalMinutes, 20);
        h.Check("the pause took the difference instead", moved.PausedSeconds / 60, 10);
        h.Check("and the window really did shrink", (int)(moved.EndUtc!.Value - moved.StartUtc).TotalMinutes, 30);

        // Typing a feeding time directly moves the pause from the other side.
        await h.Events.UpdateAsync(Edited(moved, moved.EndUtc,
            BabyEvent.PausedSecondsFor(moved.EndUtc!.Value - moved.StartUtc, TimeSpan.FromMinutes(25))));

        var overridden = await h.EventAsync(bottle.Started.Id);
        h.Check("an overridden feeding time takes", (int)overridden.Duration!.Value.TotalMinutes, 25);
        h.Check("and the pause follows", overridden.PausedSeconds / 60, 5);

        // Squeezing the window below the feeding time trims the feeding.
        var squeezedEnd = overridden.StartUtc.AddMinutes(15);
        await h.Events.UpdateAsync(Edited(overridden, squeezedEnd,
            BabyEvent.PausedSecondsFor(squeezedEnd - overridden.StartUtc, overridden.Duration!.Value)));

        var squeezed = await h.EventAsync(bottle.Started.Id);
        h.Check("a window squeezed below the feeding time trims it", (int)squeezed.Duration!.Value.TotalMinutes, 15);
        h.Check("and leaves no pause", squeezed.PausedSeconds, 0);

        h.Section("editor: the same for a pump session");
        await h.ResetAsync();

        var session = await h.Pump.StartAsync(h.BabyId);
        await h.SetPumpTimesAsync(session.Id, DateTime.UtcNow.AddMinutes(-30));
        await h.Pump.StopAsync(session.Id, 120, null, null);

        await using (var db = await h.Factory.CreateDbContextAsync())
        {
            var e = await db.PumpEntries.FindAsync(session.Id);
            e!.PausedSeconds = 10 * 60;
            await db.SaveChangesAsync();
        }

        var pumped = await h.PumpEntryAsync(session.Id);
        h.Check("set up: twenty minutes of pumping", (int)pumped.Duration!.Value.TotalMinutes, 20);

        var newEnd = pumped.EndUtc!.Value.AddMinutes(-5);
        await h.Pump.UpdateAsync(new PumpEntry
        {
            Id = pumped.Id,
            BabyId = h.BabyId,
            Kind = PumpEntryKind.Session,
            StartUtc = pumped.StartUtc,
            EndUtc = newEnd,
            AmountMl = 120,
            PausedSeconds = BabyEvent.PausedSecondsFor(newEnd - pumped.StartUtc, pumped.Duration!.Value)
        });

        var after = await h.PumpEntryAsync(session.Id);
        h.Check("the pumping time survives the stop moving", (int)after.Duration!.Value.TotalMinutes, 20);
        h.Check("the pause absorbed it", after.PausedSeconds / 60, 5);
        h.Check("the amount is untouched", after.AmountMl, 120);
    }

    private static BabyEvent Edited(BabyEvent from, DateTime? endUtc, int pausedSeconds) => new()
    {
        Id = from.Id,
        Kind = from.Kind,
        StartUtc = from.StartUtc,
        EndUtc = endUtc,
        Milk = from.Milk,
        AmountMl = from.AmountMl,
        MilkTime = from.MilkTime,
        Notes = from.Notes,
        PausedSeconds = pausedSeconds,
        PausedAtUtc = null
    };
}
