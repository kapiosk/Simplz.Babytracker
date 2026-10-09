using Simplz.Babytracker.Data;

namespace Simplz.Babytracker.Checks;

/// <summary>Bottles logged in one tap, timed bottles, and their place among the lasting kinds.</summary>
internal static class BottleChecks
{
    public static async Task RunAsync(Harness h)
    {
        h.Section("bottles: logged in one tap");
        await h.ResetAsync();

        var quick = await h.Events.LogBottleAsync(h.BabyId, MilkKind.Formula, 90);
        h.Check("a one-tap bottle is not running", quick.IsRunning, false);
        h.Check("and has no length", quick.Duration, TimeSpan.Zero);

        // The failure the 1.14 migration existed to prevent: a lasting kind with no end reads as
        // running, and starting a sleep ends every running session of another kind.
        var endBefore = quick.EndUtc;
        await h.Events.StartAsync(h.BabyId, EventKind.Sleep);
        h.Check("starting a sleep does not rewrite a logged bottle", (await h.EventAsync(quick.Id)).EndUtc, endBefore);

        h.Section("bottles: timed, with a pause");
        await h.ResetAsync();

        var timed = await h.Events.StartBottleAsync(h.BabyId, MilkKind.BreastMilk, null, null, null);
        h.Check("a timed bottle is running", timed.Started.IsRunning, true);

        // Thirty minutes, ten of them paused: twenty of feeding.
        await h.BackdateAsync(timed.Started.Id, TimeSpan.FromMinutes(30));
        await h.Events.PauseAsync(timed.Started.Id, true);
        h.Check("pausing marks it paused", (await h.EventAsync(timed.Started.Id)).IsPaused, true);

        await h.SetPausedAtAsync(timed.Started.Id, DateTime.UtcNow.AddMinutes(-10));
        h.Check("paused time comes off the live figure",
            (int)(await h.EventAsync(timed.Started.Id)).ElapsedAt(DateTime.UtcNow).TotalMinutes, 20);

        await h.Events.PauseAsync(timed.Started.Id, false);
        var resumed = await h.EventAsync(timed.Started.Id);
        h.Check("resuming banks the pause", resumed.PausedSeconds / 60, 10);
        h.Check("resuming clears the paused flag", resumed.IsPaused, false);
        h.Check("and it is still running", resumed.IsRunning, true);

        await h.Events.StopBottleAsync(timed.Started.Id, MilkKind.Formula, 110, null, "left a bit");
        var done = await h.EventAsync(timed.Started.Id);
        h.Check("finishing keeps the amount", done.AmountMl, 110);
        h.Check("finishing keeps the milk", done.Milk, MilkKind.Formula);
        h.Check("a finished bottle is not running", done.IsRunning, false);
        h.Check("pauses come off the recorded length", (int)done.Duration!.Value.TotalMinutes, 20);

        h.Section("bottles: finished while paused");
        await h.ResetAsync();

        var stillPaused = await h.Events.StartBottleAsync(h.BabyId, MilkKind.BreastMilk, null, null, null);
        await h.BackdateAsync(stillPaused.Started.Id, TimeSpan.FromMinutes(20));
        await h.Events.PauseAsync(stillPaused.Started.Id, true);
        await h.SetPausedAtAsync(stillPaused.Started.Id, DateTime.UtcNow.AddMinutes(-5));
        await h.Events.StopBottleAsync(stillPaused.Started.Id, MilkKind.BreastMilk, 60, null, null);

        var ended = await h.EventAsync(stillPaused.Started.Id);
        h.Check("the last stretch is banked, not counted as feeding", (int)ended.Duration!.Value.TotalMinutes, 15);
        h.Check("and it is not left paused", ended.PausedAtUtc, null);

        h.Section("bottles: one of the lasting kinds");
        await h.ResetAsync();

        var sleep = await h.Events.StartAsync(h.BabyId, EventKind.Sleep);
        await h.BackdateAsync(sleep.Started.Id, TimeSpan.FromHours(1));
        var bottle = await h.Events.StartBottleAsync(h.BabyId, MilkKind.Formula, null, null, null);
        h.Check("starting a bottle ends a running sleep", bottle.Ended?.Kind, EventKind.Sleep);
        h.Check("and keeps it, an hour being a real sleep", await h.CountAsync(EventKind.Sleep), 1);

        var breast = await h.Events.StartAsync(h.BabyId, EventKind.BreastFeed);
        h.Check("starting a breast feed ends the bottle", breast.Ended?.Kind, EventKind.BottleFeed);

        await h.ResetAsync();
        var once = await h.Events.StartBottleAsync(h.BabyId, MilkKind.Formula, null, null, null);
        var twice = await h.Events.StartBottleAsync(h.BabyId, MilkKind.BreastMilk, null, null, null);
        h.Check("starting a bottle twice is the same bottle", twice.Started.Id, once.Started.Id);
        h.Check("so there is only one", await h.CountAsync(EventKind.BottleFeed), 1);
    }
}
