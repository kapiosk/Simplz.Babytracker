using Simplz.Babytracker.Data;
using Simplz.Babytracker.Services;

namespace Simplz.Babytracker.Checks;

/// <summary>Pausing a breast feed and a pump session, and finishing either while paused.</summary>
internal static class PauseChecks
{
    public static async Task RunAsync(Harness h)
    {
        h.Section("pause: a breast feed");
        await h.ResetAsync();

        // Forty minutes, ten of them paused, stopped while still paused.
        var feed = await h.Events.StartAsync(h.BabyId, EventKind.BreastFeed);
        await h.BackdateAsync(feed.Started.Id, TimeSpan.FromMinutes(40));
        await h.Events.PauseAsync(feed.Started.Id, true);
        h.Check("a breast feed can be paused", (await h.EventAsync(feed.Started.Id)).IsPaused, true);
        await h.SetPausedAtAsync(feed.Started.Id, DateTime.UtcNow.AddMinutes(-10));

        var stop = await h.Events.StopAsync(feed.Started.Id);
        h.Check("stopping it is a normal stop", stop.Outcome, StopOutcome.Stopped);
        h.Check("the figure reported is feeding time, not the wall clock", (int)stop.Elapsed.TotalMinutes, 30);

        var done = await h.EventAsync(feed.Started.Id);
        h.Check("the stored length agrees", (int)done.Duration!.Value.TotalMinutes, 30);
        h.Check("the open pause is banked", done.PausedSeconds / 60, 10);
        h.Check("and a finished feed is never left paused", done.PausedAtUtc, null);

        await h.ResetAsync();
        var twice = await h.Events.StartAsync(h.BabyId, EventKind.BreastFeed);
        await h.Events.PauseAsync(twice.Started.Id, true);
        var firstInstant = (await h.EventAsync(twice.Started.Id)).PausedAtUtc;
        await h.Events.PauseAsync(twice.Started.Id, true);
        h.Check("pausing twice keeps the first instant — two phones can both tap it",
            (await h.EventAsync(twice.Started.Id)).PausedAtUtc, firstInstant);

        await h.ResetAsync();
        var sleep = await h.Events.StartAsync(h.BabyId, EventKind.Sleep);
        h.Check("sleep is unaffected and still discarded under ten minutes",
            (await h.Events.StopAsync(sleep.Started.Id)).Outcome, StopOutcome.Discarded);

        h.Section("pause: a pump session");
        await h.ResetAsync();

        // Twenty-five minutes, five paused.
        var session = await h.Pump.StartAsync(h.BabyId);
        await h.SetPumpTimesAsync(session.Id, DateTime.UtcNow.AddMinutes(-25));
        await h.Pump.PauseAsync(session.Id, true);
        h.Check("a pump session can be paused", (await h.PumpEntryAsync(session.Id)).IsPaused, true);
        await h.SetPumpTimesAsync(session.Id, (await h.PumpEntryAsync(session.Id)).StartUtc, DateTime.UtcNow.AddMinutes(-5));

        h.Check("paused time comes off the live figure",
            (int)(await h.PumpEntryAsync(session.Id)).ElapsedAt(DateTime.UtcNow).TotalMinutes, 20);

        await h.Pump.PauseAsync(session.Id, false);
        var resumed = await h.PumpEntryAsync(session.Id);
        h.Check("resuming banks the pause", resumed.PausedSeconds / 60, 5);
        h.Check("and it is still running", resumed.IsRunning, true);
        h.Check("and no longer paused", resumed.IsPaused, false);

        await h.Pump.StopAsync(session.Id, 140, null, null);
        var finished = await h.PumpEntryAsync(session.Id);
        h.Check("pauses come off the recorded length", (int)finished.Duration!.Value.TotalMinutes, 20);
        h.Check("the amount is kept", finished.AmountMl, 140);
        h.Check("and it is not left paused", finished.PausedAtUtc, null);

        var today = DateTime.UtcNow.Date;
        var totals = await h.Pump.TotalsAsync(h.BabyId, today, today.AddDays(1));
        h.Check("today's millilitres", totals.Ml, 140);
        h.Check("today's time leaves the pauses out", (int)totals.Time.TotalMinutes, 20);

        await h.ResetAsync();
        var whilePaused = await h.Pump.StartAsync(h.BabyId);
        await h.SetPumpTimesAsync(whilePaused.Id, DateTime.UtcNow.AddMinutes(-30), DateTime.UtcNow.AddMinutes(-12));
        await h.Pump.StopAsync(whilePaused.Id, 70, null, null);
        var stopped = await h.PumpEntryAsync(whilePaused.Id);
        h.Check("a session finished while paused banks the last stretch", (int)stopped.Duration!.Value.TotalMinutes, 18);
        h.Check("and is not left paused", stopped.PausedAtUtc, null);
    }
}
