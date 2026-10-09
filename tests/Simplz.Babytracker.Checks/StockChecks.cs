using Simplz.Babytracker.Data;
using Simplz.Babytracker.Services;

namespace Simplz.Babytracker.Checks;

/// <summary>The fridge: the stock ledger, its corrections, and the morning and night batches.</summary>
internal static class StockChecks
{
    public static async Task RunAsync(Harness h)
    {
        h.Section("stock: the ledger");
        await h.ResetAsync();

        // The history the app already had: breast-milk bottles from before pumping existed.
        for (var i = 0; i < 5; i++)
        {
            await h.Events.LogBottleAsync(h.BabyId, MilkKind.BreastMilk, 100);
        }

        var none = await h.Pump.GetStockAsync(h.BabyId);
        h.Check("with no pumping the stock is unknown, not zero", none.IsKnown, false);
        h.Check("and reads nothing", none.Ml, 0);

        var first = await h.Pump.StartAsync(h.BabyId);
        await h.Pump.StopAsync(first.Id, 120, null, null);
        var afterFirst = await h.Pump.GetStockAsync(h.BabyId);
        h.Check("the first session opens the count", afterFirst.Ml, 120);
        h.Check("bottles from before it are not counted against it", afterFirst.TakenMl, 0);
        h.Check("and it is not a hand-set baseline", afterFirst.WasSet, false);

        await h.Events.LogBottleAsync(h.BabyId, MilkKind.BreastMilk, 50);
        h.Check("a breast-milk bottle comes out of it", (await h.Pump.GetStockAsync(h.BabyId)).Ml, 70);

        await h.Events.LogBottleAsync(h.BabyId, MilkKind.Formula, 90);
        h.Check("formula does not touch it", (await h.Pump.GetStockAsync(h.BabyId)).Ml, 70);

        var running = await h.Pump.StartAsync(h.BabyId);
        h.Check("a session still running adds nothing yet", (await h.Pump.GetStockAsync(h.BabyId)).Ml, 70);
        await h.Pump.StopAsync(running.Id, 80, null, null);
        h.Check("stopping it adds what it made", (await h.Pump.GetStockAsync(h.BabyId)).Ml, 150);

        await h.Pump.SetStockAsync(h.BabyId, 300, 200, "counted the freezer");
        var set = await h.Pump.GetStockAsync(h.BabyId);
        h.Check("a correction becomes the figure", set.Ml, 500);
        h.Check("it is the opening of the count", set.OpeningMl, 500);
        h.Check("nothing has gone in since", set.PumpedMl, 0);
        h.Check("nothing has come out since", set.TakenMl, 0);
        h.Check("and it is a hand-set baseline", set.WasSet, true);

        var third = await h.Pump.StartAsync(h.BabyId);
        await h.Pump.StopAsync(third.Id, 60, null, null);
        await h.Events.LogBottleAsync(h.BabyId, MilkKind.BreastMilk, 200);
        var carried = await h.Pump.GetStockAsync(h.BabyId);
        h.Check("the count carries on from the correction", carried.Ml, 360);
        h.Check("and its arithmetic adds up", carried.OpeningMl + carried.PumpedMl - carried.TakenMl, carried.Ml);

        await h.Events.LogBottleAsync(h.BabyId, MilkKind.BreastMilk, 500);
        h.Check("an overdrawn fridge shows negative rather than hiding it", (await h.Pump.GetStockAsync(h.BabyId)).Ml, -140);

        var unmeasured = await h.Pump.StartAsync(h.BabyId);
        await h.Pump.StopAsync(unmeasured.Id, null, null, null);
        h.Check("a session with no amount counts as nothing", (await h.Pump.GetStockAsync(h.BabyId)).Ml, -140);

        var today = DateTime.UtcNow.Date;
        var totals = await h.Pump.TotalsAsync(h.BabyId, today, today.AddDays(1));
        h.Check("today's millilitres", totals.Ml, 260);
        h.Check("today's sessions", totals.Sessions, 4);

        var other = await h.AddBabyAsync("Second");
        h.Check("another baby's fridge is its own", (await h.Pump.GetStockAsync(other)).IsKnown, false);

        h.Section("stock: morning and night");
        await h.ResetAsync();

        var plain = await h.Pump.StartAsync(h.BabyId);
        await h.Pump.StopAsync(plain.Id, 100, null, null);
        await h.Events.LogBottleAsync(h.BabyId, MilkKind.BreastMilk, 40);
        var unlabelled = await h.Pump.GetStockAsync(h.BabyId);
        h.Check("with nothing labelled, the total is as before", unlabelled.Ml, 60);
        h.Check("and it all sits on the unlabelled line", unlabelled.Of(null)?.Ml, 60);
        h.Check("morning is empty", unlabelled.Of(MilkTime.Morning)?.Ml, 0);
        h.Check("night is empty", unlabelled.Of(MilkTime.Night)?.Ml, 0);
        h.Check("the lines add up to the total", unlabelled.Batches.Sum(b => b.Ml), unlabelled.Ml);

        var morning = await h.Pump.StartAsync(h.BabyId);
        await h.Pump.StopAsync(morning.Id, 120, MilkTime.Morning, null);
        var night = await h.Pump.StartAsync(h.BabyId);
        await h.Pump.StopAsync(night.Id, 80, MilkTime.Night, null);
        await h.Events.LogBottleAsync(h.BabyId, MilkKind.BreastMilk, 50, MilkTime.Night);
        var split = await h.Pump.GetStockAsync(h.BabyId);
        h.Check("a labelled session lands on its side", split.Of(MilkTime.Morning)?.Ml, 120);
        h.Check("and a labelled bottle comes off its side", split.Of(MilkTime.Night)?.Ml, 30);
        h.Check("the unlabelled line is untouched", split.Of(null)?.Ml, 60);
        h.Check("the total is all three", split.Ml, 210);

        await h.Pump.SetStockAsync(h.BabyId, 300, 200, "counted properly");
        var corrected = await h.Pump.GetStockAsync(h.BabyId);
        h.Check("a correction sets both sides", corrected.Ml, 500);
        h.Check("morning opens on its figure", corrected.Of(MilkTime.Morning)?.OpeningMl, 300);
        h.Check("night opens on its figure", corrected.Of(MilkTime.Night)?.OpeningMl, 200);
        h.Check("and unlabelled starts again from nothing", corrected.Of(null)?.Ml, 0);

        var later = await h.Pump.StartAsync(h.BabyId);
        await h.Pump.StopAsync(later.Id, 40, MilkTime.Night, null);
        await h.Events.LogBottleAsync(h.BabyId, MilkKind.Formula, 90, MilkTime.Night);
        await h.Events.LogBottleAsync(h.BabyId, MilkKind.BreastMilk, 30);
        var onwards = await h.Pump.GetStockAsync(h.BabyId);
        h.Check("each side carries on from its own opening", onwards.Of(MilkTime.Night)?.Ml, 240);
        h.Check("the other side is left alone", onwards.Of(MilkTime.Morning)?.Ml, 300);
        h.Check("an unlabelled bottle shows as its own negative", onwards.Of(null)?.Ml, -30);
        h.Check("formula labelled night still does not count", onwards.TakenMl, 30);

        h.Section("stock: a batch only means anything on breast milk");
        var formula = await h.Events.LogBottleAsync(h.BabyId, MilkKind.Formula, 60, MilkTime.Morning);
        h.Check("formula never carries a batch", formula.MilkTime, null);

        var switched = await h.Events.StartBottleAsync(h.BabyId, MilkKind.BreastMilk, null, MilkTime.Night, null);
        await h.Events.StopBottleAsync(switched.Started.Id, MilkKind.Formula, 70, MilkTime.Night, null);
        h.Check("a bottle switched to formula drops its batch", (await h.EventAsync(switched.Started.Id)).MilkTime, null);

        var named = await h.Events.LogBottleAsync(h.BabyId, MilkKind.BreastMilk, 60, MilkTime.Night);
        h.Check("the batch shows in the label", Display.Label(named), "Bottle · night milk");
    }
}
