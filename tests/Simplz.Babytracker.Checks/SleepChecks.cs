using Simplz.Babytracker.Data;
using Simplz.Babytracker.Services;

namespace Simplz.Babytracker.Checks;

/// <summary>The ten-minute floor on sleep, and the mutual exclusion between the lasting kinds.</summary>
internal static class SleepChecks
{
    public static async Task RunAsync(Harness h)
    {
        h.Section("sleep: the ten-minute floor");
        await h.ResetAsync();

        var tapped = await h.Events.StartAsync(h.BabyId, EventKind.Sleep);
        var stop = await h.Events.StopAsync(tapped.Started.Id);
        h.Check("a sleep stopped within seconds is thrown away", stop.Outcome, StopOutcome.Discarded);
        h.Check("and is not left in the log", await h.CountAsync(EventKind.Sleep), 0);
        h.Check("a copy is handed back for Undo", stop.Discarded is not null, true);

        await h.Events.AddAsync(stop.Discarded!);
        h.Check("Undo puts it back", await h.CountAsync(EventKind.Sleep), 1);

        await h.ResetAsync();
        var nap = await h.Events.StartAsync(h.BabyId, EventKind.Sleep);
        await h.BackdateAsync(nap.Started.Id, TimeSpan.FromMinutes(45));
        h.Check("a 45-minute sleep is kept", (await h.Events.StopAsync(nap.Started.Id)).Outcome, StopOutcome.Stopped);

        var edge = await h.Events.StartAsync(h.BabyId, EventKind.Sleep);
        await h.BackdateAsync(edge.Started.Id, TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
        h.Check("ten minutes and a second is long enough", (await h.Events.StopAsync(edge.Started.Id)).Outcome, StopOutcome.Stopped);

        var quickFeed = await h.Events.StartAsync(h.BabyId, EventKind.BreastFeed);
        h.Check("a short breast feed is kept — the floor is sleep only",
            (await h.Events.StopAsync(quickFeed.Started.Id)).Outcome, StopOutcome.Stopped);

        h.Section("sleep: displaced by a feed");
        await h.ResetAsync();
        await h.Events.StartAsync(h.BabyId, EventKind.Sleep);
        var feed = await h.Events.StartAsync(h.BabyId, EventKind.BreastFeed);
        h.Check("starting a feed reports the sleep it ended", feed.Ended?.Kind, EventKind.Sleep);
        h.Check("a sleep of seconds is discarded rather than ended", feed.EndedWasDiscarded, true);
        h.Check("and is gone from the log", await h.CountAsync(EventKind.Sleep), 0);
        h.Check("the feed is running", (await h.EventAsync(feed.Started.Id)).IsRunning, true);

        await h.ResetAsync();
        var night = await h.Events.StartAsync(h.BabyId, EventKind.Sleep);
        await h.BackdateAsync(night.Started.Id, TimeSpan.FromHours(2));
        var morningFeed = await h.Events.StartAsync(h.BabyId, EventKind.BreastFeed);
        h.Check("a long sleep displaced by a feed is kept", morningFeed.EndedWasDiscarded, false);
        h.Check("and ended rather than removed", (await h.EventAsync(night.Started.Id)).EndUtc is not null, true);

        h.Section("sleep: the exceptions to the floor");
        await h.ResetAsync();
        var photographed = await h.Events.StartAsync(h.BabyId, EventKind.Sleep);
        await using (var db = await h.Factory.CreateDbContextAsync())
        {
            db.Media.Add(new EventMedia
            {
                BabyEventId = photographed.Started.Id,
                FileName = "x.jpg",
                ContentType = "image/jpeg",
                OriginalName = "x.jpg",
                Bytes = 1,
                AddedUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        h.Check("a short sleep with a photo is kept",
            (await h.Events.StopAsync(photographed.Started.Id)).Outcome, StopOutcome.Stopped);

        await h.Events.AddAsync(new BabyEvent
        {
            BabyId = h.BabyId,
            Kind = EventKind.Sleep,
            StartUtc = DateTime.UtcNow.AddMinutes(-4),
            EndUtc = DateTime.UtcNow
        });
        h.Check("a four-minute sleep typed in by hand is kept", await h.CountAsync(EventKind.Sleep), 2);
    }
}
