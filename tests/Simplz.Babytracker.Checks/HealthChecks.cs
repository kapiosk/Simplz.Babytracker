using Simplz.Babytracker.Components;
using Simplz.Babytracker.Data;
using Simplz.Babytracker.Services;

namespace Simplz.Babytracker.Checks;

/// <summary>Temperatures, medicines and their doses, and diarrhoea as a kind of poop.</summary>
internal static class HealthChecks
{
    public static async Task RunAsync(Harness h)
    {
        Readings(h);
        DoseRules(h);
        await Storing(h);
        await Diarrhoea(h);
        await SpitUpAndVomit(h);
    }

    private static void Readings(Harness h)
    {
        h.Section("health: what a reading is called");

        h.Check("38.0 is high — the NHS's own line", HealthService.IsHigh(38.0), true);
        h.Check("37.9 is not", HealthService.IsHigh(37.9), false);
        h.Check("said to one decimal", Display.Celsius(38), "38.0°C");
        h.Check("a typing slip is not a reading", HealthService.Plausible(3.8), false);
        h.Check("nor is a fever no thermometer reads", HealthService.Plausible(48.0), false);
        h.Check("an ordinary one is", HealthService.Plausible(36.8), true);

        h.Section("health: the NHS's two cases for babies");
        var born = new DateOnly(2026, 6, 10);

        h.Check("eight weeks at 38.0", HealthService.UrgentAgeRule(38.0, born, born.AddDays(56)) is not null, true);
        h.Check("eight weeks at 37.9", HealthService.UrgentAgeRule(37.9, born, born.AddDays(56)), null);
        h.Check("the day before three months still counts as under",
            HealthService.UrgentAgeRule(38.2, born, born.AddMonths(3).AddDays(-1))?.StartsWith("under 3 months"), true);
        h.Check("three months at 38.5 is the other rule, and under it",
            HealthService.UrgentAgeRule(38.5, born, born.AddMonths(3)), null);
        h.Check("three months at 39.0", HealthService.UrgentAgeRule(39.0, born, born.AddMonths(3))?.StartsWith("3 to 6 months"), true);
        h.Check("six months at 39.5 is past both", HealthService.UrgentAgeRule(39.5, born, born.AddMonths(6)), null);
        h.Check("with no birth date there is no age to apply", HealthService.UrgentAgeRule(39.5, null, born), null);
    }

    private static void DoseRules(Harness h)
    {
        h.Section("health: a medicine's own rules");

        // At least four hours apart, no more than four in 24 hours — the shape of most labels.
        var med = new Medicine { Id = 1, Name = "Infant paracetamol", Dose = "2.5 ml", MinHoursBetween = 4, MaxPerDay = 4 };
        var noon = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        MedicineDose At(int id, double hoursFromNoon) => new() { Id = id, MedicineId = 1, GivenUtc = noon.AddHours(hoursFromNoon) };

        var none = HealthService.Status(med, [], noon);
        h.Check("never given: no last dose", none.LastUtc, null);
        h.Check("and nothing holding it back", none.GapEndsUtc, null);

        var one = HealthService.Status(med, [At(1, -1)], noon);
        h.Check("an hour after a dose, the gap runs three more", one.GapEndsUtc, noon.AddHours(3));
        h.Check("one in the last 24 hours", one.InLast24h, 1);

        h.Check("five hours after, nothing holds it back",
            HealthService.Status(med, [At(1, -5)], noon).GapEndsUtc, null);

        // Four in the day, the last of them five hours ago: the gap is over, the limit is not.
        var full = HealthService.Status(med, [At(1, -20), At(2, -15), At(3, -10), At(4, -5)], noon);
        h.Check("at the limit", full.InLast24h, 4);
        h.Check("the next waits for the oldest to be 24 hours old", full.GapEndsUtc, noon.AddHours(4));

        h.Check("a dose 25 hours ago is outside the day",
            HealthService.Status(med, [At(1, -25)], noon).InLast24h, 0);
        h.Check("another medicine's doses are not this one's",
            HealthService.Status(med, [new MedicineDose { MedicineId = 2, GivenUtc = noon.AddHours(-1) }], noon).LastUtc, null);
        h.Check("a dose logged for later is not the last one yet",
            HealthService.Status(med, [At(1, 2)], noon).LastUtc, null);

        var noRules = new Medicine { Id = 1, Name = "Cream" };
        h.Check("with no rules on the label, nothing is ever held back",
            HealthService.Status(noRules, [At(1, -0.1), At(2, -0.2), At(3, -0.3), At(4, -0.4), At(5, -0.5)], noon).GapEndsUtc, null);

        h.Section("health: checking a dose before it is recorded");

        var tooSoon = HealthService.Check(med, [At(1, -2)], noon);
        h.Check("two hours after the last is too close", tooSoon.TooClose, true);
        h.Check("and says which dose it is close to", tooSoon.Nearest?.Id, 1);

        h.Check("four hours after is not", HealthService.Check(med, [At(1, -4)], noon).TooClose, false);
        h.Check("too close looks forwards as well — one written in between two",
            HealthService.Check(med, [At(1, -6), At(2, 1)], noon).TooClose, true);

        var fifth = HealthService.Check(med, [At(1, -20), At(2, -15), At(3, -10), At(4, -5)], noon);
        h.Check("a fifth inside 24 hours breaks the limit", fifth.OverMax, true);
        h.Check("and counts five", fifth.MostIn24h, 5);
        h.Check("while the gap itself is fine", fifth.TooClose, false);

        // By hand: doses at −20h, −15h, noon, +5h, +10h. The fullest 24 hours holds three of them
        // — −20 to noon, −15 to +5, or noon to +10 — since −15 to +10 is already 25 hours. Counting
        // everything within a day either side would say five, and wrongly call it over the limit.
        var spread = HealthService.Check(med, [At(1, -20), At(2, -15), At(3, 5), At(4, 10)], noon);
        h.Check("doses a day either side are not one day's worth", spread.MostIn24h, 3);
        h.Check("so the limit holds", spread.OverMax, false);

        h.Check("hours said as people say them", Display.Hours(4), "4h");
        h.Check("half hours too", Display.Hours(4.5), "4½h");
        h.Check("anything else in minutes", Display.Hours(1.25), "1h 15m");
    }

    private static async Task Storing(Harness h)
    {
        h.Section("health: storing readings");
        await h.ResetAsync();

        var taken = DateTime.UtcNow.AddMinutes(-30);
        var saved = await h.Health.SaveTemperatureAsync(new TemperatureReading
        {
            BabyId = h.BabyId, TakenUtc = taken, Celsius = 38.44, Method = TemperatureMethod.Underarm, Notes = "  flushed  "
        });
        h.Check("a reading is saved", saved is not null, true);
        h.Check("to one decimal", saved!.Celsius, 38.4);
        h.Check("with the time it was taken, not saved", saved.TakenUtc, taken);
        h.Check("and a tidy note", saved.Notes, "flushed");

        h.Check("a slip of 3.8 is refused",
            await h.Health.SaveTemperatureAsync(new TemperatureReading { BabyId = h.BabyId, TakenUtc = taken, Celsius = 3.8 }), null);

        saved.Celsius = 37.6;
        await h.Health.SaveTemperatureAsync(saved);
        var list = await h.Health.TemperaturesAsync(h.BabyId, DateTime.UtcNow.AddDays(-1));
        h.Check("editing changes it rather than adding one", list.Count, 1);
        h.Check("to the new figure", list[0].Celsius, 37.6);

        h.Check("an old reading is outside a recent window",
            (await h.Health.TemperaturesAsync(h.BabyId, DateTime.UtcNow)).Count, 0);

        await h.Health.DeleteTemperatureAsync(saved.Id);
        h.Check("and one can be deleted", (await h.Health.TemperaturesAsync(h.BabyId, DateTime.UtcNow.AddDays(-1))).Count, 0);

        h.Section("health: storing medicines and doses");

        h.Check("a medicine with no name is not saved",
            await h.Health.SaveMedicineAsync(new Medicine { BabyId = h.BabyId, Name = "   " }), null);

        var med = await h.Health.SaveMedicineAsync(new Medicine
        {
            BabyId = h.BabyId, Name = " Amoxicillin ", Dose = "5 ml", MinHoursBetween = 8, MaxPerDay = 3,
            EndsOn = DateOnly.FromDateTime(DateTime.Now).AddDays(4)
        });
        h.Check("a medicine is saved, trimmed", med?.Name, "Amoxicillin");
        h.Check("its course is current", med!.IsCurrent(DateOnly.FromDateTime(DateTime.Now)), true);
        h.Check("and over the day after it ends", med.IsCurrent(med.EndsOn!.Value.AddDays(1)), false);

        var zero = await h.Health.SaveMedicineAsync(new Medicine { BabyId = h.BabyId, Name = "Drops", MinHoursBetween = 0, MaxPerDay = 0 });
        h.Check("a gap of nothing is no gap", zero!.MinHoursBetween, null);
        h.Check("a limit of nothing is no limit", zero.MaxPerDay, null);

        var given = DateTime.UtcNow.AddHours(-2);
        var dose = await h.Health.SaveDoseAsync(new MedicineDose { MedicineId = med.Id, GivenUtc = given });
        await h.Health.SaveDoseAsync(new MedicineDose { MedicineId = zero.Id, GivenUtc = given, Dose = "2 drops" });
        var doses = await h.Health.DosesAsync(h.BabyId, DateTime.UtcNow.AddDays(-1));
        h.Check("doses of both come back", doses.Count, 2);
        h.Check("a dose keeps when it was given", doses.Single(d => d.MedicineId == med.Id).GivenUtc, given);
        h.Check("a dose with no amount of its own means the usual", doses.Single(d => d.MedicineId == med.Id).Dose, null);

        h.Check("a dose for a medicine that is not there is refused",
            await h.Health.SaveDoseAsync(new MedicineDose { MedicineId = 999_999, GivenUtc = given }), null);

        var other = await h.AddBabyAsync("Second");
        h.Check("another baby has none of these doses", (await h.Health.DosesAsync(other, DateTime.UtcNow.AddDays(-1))).Count, 0);

        await h.Health.SetFinishedAsync(med.Id, true);
        h.Check("putting a medicine away keeps its doses",
            (await h.Health.DosesAsync(h.BabyId, DateTime.UtcNow.AddDays(-1))).Count(d => d.MedicineId == med.Id), 1);
        h.Check("and it is no longer current",
            (await h.Health.MedicinesAsync(h.BabyId)).Single(m => m.Id == med.Id).IsCurrent(DateOnly.FromDateTime(DateTime.Now)), false);

        await h.Health.DeleteDoseAsync(dose!.Id);
        h.Check("a single dose can be deleted",
            (await h.Health.DosesAsync(h.BabyId, DateTime.UtcNow.AddDays(-1))).Any(d => d.Id == dose.Id), false);

        await h.Health.DeleteMedicineAsync(zero.Id);
        h.Check("deleting a medicine takes its doses with it",
            (await h.Health.DosesAsync(h.BabyId, DateTime.UtcNow.AddDays(-1))).Count, 0);

        await h.Health.SaveTemperatureAsync(new TemperatureReading { BabyId = other, TakenUtc = taken, Celsius = 38.0 });
        var otherMed = await h.Health.SaveMedicineAsync(new Medicine { BabyId = other, Name = "Calpol" });
        await h.Health.SaveDoseAsync(new MedicineDose { MedicineId = otherMed!.Id, GivenUtc = given });
        await using (var db = await h.Factory.CreateDbContextAsync())
        {
            db.Babies.Remove((await db.Babies.FindAsync(other))!);
            await db.SaveChangesAsync();
        }

        await using (var db = await h.Factory.CreateDbContextAsync())
        {
            h.Check("a deleted baby takes its readings", db.Temperatures.Count(t => t.BabyId == other), 0);
            h.Check("its medicines", db.Medicines.Count(m => m.BabyId == other), 0);
            h.Check("and their doses", db.MedicineDoses.Count(d => d.MedicineId == otherMed.Id), 0);
        }
    }

    private static async Task Diarrhoea(Harness h)
    {
        h.Section("health: diarrhoea is a poop");
        await h.ResetAsync();

        var loose = await h.Events.LogAsync(h.BabyId, EventKind.Poop, diarrhoea: true);
        h.Check("logged as a poop", loose.Kind, EventKind.Poop);
        h.Check("marked as diarrhoea", (await h.EventAsync(loose.Id)).Diarrhoea, true);
        h.Check("so the nappy count includes it", await h.CountAsync(EventKind.Poop), 1);
        h.Check("and it says so wherever it is listed", Display.Label(loose), "Poop · diarrhoea");

        var wet = await h.Events.LogAsync(h.BabyId, EventKind.Urine, diarrhoea: true);
        h.Check("only a poop can be diarrhoea", (await h.EventAsync(wet.Id)).Diarrhoea, false);

        var plain = await h.Events.LogAsync(h.BabyId, EventKind.Poop);
        h.Check("an ordinary poop is not", (await h.EventAsync(plain.Id)).Diarrhoea, false);
        h.Check("and reads as before", Display.Label(plain), "Poop");

        var edited = await h.EventAsync(loose.Id);
        edited.Kind = EventKind.Vomit;
        await h.Events.UpdateAsync(edited);
        h.Check("changing it to something else drops the mark", (await h.EventAsync(loose.Id)).Diarrhoea, false);

        edited.Kind = EventKind.Poop;
        edited.Diarrhoea = true;
        await h.Events.UpdateAsync(edited);
        h.Check("and the editor can set it back", (await h.EventAsync(loose.Id)).Diarrhoea, true);
    }

    private static async Task SpitUpAndVomit(Harness h)
    {
        h.Section("health: a spit-up is not a vomit");
        await h.ResetAsync();

        // The rename kept the stored number, so every entry logged as a vomit before 1.26 reads
        // as a spit-up now — which is what they were.
        h.Check("a spit-up is stored as the old vomit was", (int)EventKind.SpitUp, 4);
        h.Check("a vomit is a new value, clashing with nothing", (int)EventKind.Vomit, 6);
        h.Check("no two kinds share a value",
            Enum.GetValues<EventKind>().Select(k => (int)k).Distinct().Count(), Enum.GetValues<EventKind>().Length);

        await h.Events.LogAsync(h.BabyId, EventKind.SpitUp);
        var sick = await h.Events.LogAsync(h.BabyId, EventKind.Vomit);
        h.Check("they count separately: spit-ups", await h.CountAsync(EventKind.SpitUp), 1);
        h.Check("and vomits", await h.CountAsync(EventKind.Vomit), 1);
        h.Check("a spit-up is called one", Display.Label(EventKind.SpitUp), "Spit-up");
        h.Check("a vomit is called one", Display.Label(sick), "Vomit");
        h.Check("each has its own icon", Icon.For(EventKind.Vomit) != Icon.For(EventKind.SpitUp), true);
        h.Check("a vomit is a moment, not something started and stopped", BabyEvent.Lasts(EventKind.Vomit), false);

        var feedAt = DateTime.UtcNow.AddHours(-1);
        var day = ChartGrouping.Build(
        [
            new BabyEvent { Id = 1, Kind = EventKind.BreastFeed, StartUtc = feedAt, EndUtc = feedAt.AddMinutes(20) },
            new BabyEvent { Id = 2, Kind = EventKind.SpitUp, StartUtc = feedAt.AddMinutes(25) },
            new BabyEvent { Id = 3, Kind = EventKind.Vomit, StartUtc = feedAt.AddMinutes(40) }
        ]);
        var row = day.SelectMany(d => d.Rows).Single();
        h.Check("on the chart both sit in the one column, against the feed", row.Emesis.Count, 2);
    }
}
