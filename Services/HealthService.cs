using Microsoft.EntityFrameworkCore;
using Simplz.Babytracker.Data;

namespace Simplz.Babytracker.Services;

/// <summary>Where a medicine stands right now: when it was last given, and its own rules applied.</summary>
/// <param name="LastUtc">The most recent dose at or before now. Null when there is none.</param>
/// <param name="InLast24h">Doses in the 24 hours up to now.</param>
/// <param name="GapEndsUtc">
/// When the medicine's own rules next allow a dose — the gap after the last one, or the oldest
/// dose in the last 24 hours dropping out when the daily limit has been reached. Null when
/// neither rule is holding anything back.
/// </param>
public sealed record DoseStatus(DateTime? LastUtc, int InLast24h, DateTime? GapEndsUtc);

/// <summary>A dose at one moment, measured against the medicine's rules and its other doses.</summary>
/// <param name="Nearest">The other dose closest to it, either side. Null when it is the only one.</param>
/// <param name="TooClose">Within the medicine's minimum gap of another dose.</param>
/// <param name="MostIn24h">The most doses any 24 hours containing this one would hold, this one included.</param>
/// <param name="OverMax">More than the medicine's daily limit in some 24 hours.</param>
public sealed record DoseCheck(MedicineDose? Nearest, bool TooClose, int MostIn24h, bool OverMax);

/// <summary>
/// Temperatures and medicines. Diarrhoea is not here: it is a poop with a flag, and goes through
/// <see cref="EventService"/> like any other nappy.
/// </summary>
public sealed class HealthService(IDbContextFactory<AppDbContext> factory, ILogger<HealthService> log)
{
    /// <summary>
    /// The NHS's line for a high temperature in a child: 38°C or more. Used to say "high", in
    /// words, beside a reading — never to say what to do about it, which is the NHS's to say.
    /// </summary>
    public const double HighFrom = 38.0;

    /// <summary>Outside this a reading is almost certainly a typing slip, and is refused.</summary>
    public const double LowestReading = 34.0;

    public const double HighestReading = 43.0;

    public static bool IsHigh(double celsius) => celsius >= HighFrom;

    public static bool Plausible(double celsius) => celsius is >= LowestReading and <= HighestReading;

    /// <summary>
    /// Whether a reading falls in one of the two cases the NHS singles out for babies — under 3
    /// months at 38°C or more, or 3 to 6 months at 39°C or more — where it says to call 111 or a
    /// GP urgently. Says which, so the page can quote the right one. Null for neither, and null
    /// with no birth date, since the age is the whole of the rule.
    /// </summary>
    public static string? UrgentAgeRule(double celsius, DateOnly? bornOn, DateOnly on)
    {
        if (bornOn is not { } born)
        {
            return null;
        }

        if (on < born.AddMonths(3))
        {
            return celsius >= 38.0 ? "under 3 months old with a temperature of 38°C or higher" : null;
        }

        if (on < born.AddMonths(6))
        {
            return celsius >= 39.0 ? "3 to 6 months old with a temperature of 39°C or higher" : null;
        }

        return null;
    }

    public static DoseStatus Status(Medicine medicine, IEnumerable<MedicineDose> doses, DateTime nowUtc)
    {
        var mine = doses
            .Where(d => d.MedicineId == medicine.Id && d.GivenUtc <= nowUtc)
            .OrderBy(d => d.GivenUtc)
            .ToList();

        if (mine.Count == 0)
        {
            return new DoseStatus(null, 0, null);
        }

        var last = mine[^1].GivenUtc;
        var window = mine.Where(d => d.GivenUtc > nowUtc.AddHours(-24)).ToList();

        DateTime? gapEnds = null;

        if (medicine.MinHoursBetween is { } hours and > 0)
        {
            gapEnds = last.AddHours(hours);
        }

        // At the limit, the next one fits once enough of the oldest have aged out of the last
        // 24 hours that one more would not break it.
        if (medicine.MaxPerDay is { } max and > 0 && window.Count >= max)
        {
            var dropsOut = window[window.Count - max].GivenUtc.AddHours(24);
            gapEnds = gapEnds is { } g && g > dropsOut ? g : dropsOut;
        }

        return new DoseStatus(last, window.Count, gapEnds > nowUtc ? gapEnds : null);
    }

    /// <summary>
    /// Measures a dose against the others, given at <paramref name="atUtc"/>. Looks both ways,
    /// because a dose is often written down after the fact, between two already logged.
    /// </summary>
    /// <param name="others">The medicine's other doses — leave out the one being edited.</param>
    public static DoseCheck Check(Medicine medicine, IEnumerable<MedicineDose> others, DateTime atUtc)
    {
        var mine = others.Where(d => d.MedicineId == medicine.Id).ToList();

        var nearest = mine.MinBy(d => Math.Abs((d.GivenUtc - atUtc).Ticks));

        var tooClose = medicine.MinHoursBetween is { } hours and > 0
                       && nearest is not null
                       && Math.Abs((nearest.GivenUtc - atUtc).TotalHours) < hours;

        // Every 24 hours that contains this dose starts at a dose — this one or an earlier one
        // within the day before it — so trying each of those as the start finds the fullest.
        var times = mine.Select(d => d.GivenUtc).Append(atUtc).OrderBy(t => t).ToList();
        var most = times
            .Where(start => start <= atUtc && start > atUtc.AddHours(-24))
            .Max(start => times.Count(t => t >= start && t < start.AddHours(24)));

        var overMax = medicine.MaxPerDay is { } max and > 0 && most > max;

        return new DoseCheck(nearest, tooClose, most, overMax);
    }

    public event Action<int>? Changed;

    /// <summary>One subscriber at a time, each in its own try — see the note on EventService.</summary>
    private void NotifyChanged(int babyId)
    {
        if (Changed is not { } subscribers)
        {
            return;
        }

        foreach (var subscriber in subscribers.GetInvocationList())
        {
            try
            {
                ((Action<int>)subscriber)(babyId);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "A page failed to handle a change to the health log.");
            }
        }
    }

    // ---------- temperatures ----------

    public async Task<List<TemperatureReading>> TemperaturesAsync(int babyId, DateTime sinceUtc, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Temperatures
            .Where(t => t.BabyId == babyId && t.TakenUtc >= sinceUtc)
            .OrderByDescending(t => t.TakenUtc)
            .ToListAsync(ct);
    }

    /// <summary>Adds a new reading, or saves changes to one already there.</summary>
    public async Task<TemperatureReading?> SaveTemperatureAsync(TemperatureReading reading, CancellationToken ct = default)
    {
        if (!Plausible(reading.Celsius))
        {
            return null;
        }

        await using var db = await factory.CreateDbContextAsync(ct);

        TemperatureReading row;
        if (reading.Id == 0)
        {
            row = new TemperatureReading { BabyId = reading.BabyId };
            db.Temperatures.Add(row);
        }
        else
        {
            row = await db.Temperatures.FirstOrDefaultAsync(t => t.Id == reading.Id, ct)
                  ?? throw new InvalidOperationException("That reading has gone.");
        }

        row.TakenUtc = reading.TakenUtc;
        row.Celsius = Math.Round(reading.Celsius, 1);
        row.Method = reading.Method;
        row.Notes = Clean(reading.Notes);

        await db.SaveChangesAsync(ct);
        NotifyChanged(row.BabyId);
        return row;
    }

    public async Task DeleteTemperatureAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var babyId = await db.Temperatures.Where(t => t.Id == id).Select(t => t.BabyId).FirstOrDefaultAsync(ct);
        if (babyId == 0)
        {
            return;
        }

        await db.Temperatures.Where(t => t.Id == id).ExecuteDeleteAsync(ct);
        NotifyChanged(babyId);
    }

    // ---------- medicines ----------

    /// <summary>All of them, current and finished, in the order they were added.</summary>
    public async Task<List<Medicine>> MedicinesAsync(int babyId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Medicines
            .Where(m => m.BabyId == babyId)
            .OrderBy(m => m.AddedUtc)
            .ToListAsync(ct);
    }

    /// <summary>Adds a medicine, or saves changes to one. A blank name is not a medicine.</summary>
    public async Task<Medicine?> SaveMedicineAsync(Medicine medicine, CancellationToken ct = default)
    {
        var name = medicine.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        await using var db = await factory.CreateDbContextAsync(ct);

        Medicine row;
        if (medicine.Id == 0)
        {
            row = new Medicine { BabyId = medicine.BabyId, AddedUtc = DateTime.UtcNow };
            db.Medicines.Add(row);
        }
        else
        {
            row = await db.Medicines.FirstOrDefaultAsync(m => m.Id == medicine.Id, ct)
                  ?? throw new InvalidOperationException("That medicine has gone.");
        }

        row.Name = name.Length > 80 ? name[..80] : name;
        row.Dose = Clean(medicine.Dose, 60);
        row.MinHoursBetween = medicine.MinHoursBetween is > 0 ? medicine.MinHoursBetween : null;
        row.MaxPerDay = medicine.MaxPerDay is > 0 ? medicine.MaxPerDay : null;
        row.EndsOn = medicine.EndsOn;
        row.Finished = medicine.Finished;
        row.Notes = Clean(medicine.Notes);

        await db.SaveChangesAsync(ct);
        NotifyChanged(row.BabyId);
        return row;
    }

    public async Task SetFinishedAsync(int id, bool finished, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Medicines.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (row is null)
        {
            return;
        }

        row.Finished = finished;
        await db.SaveChangesAsync(ct);
        NotifyChanged(row.BabyId);
    }

    /// <summary>Deletes a medicine and every dose of it. Putting it away is what keeps the history.</summary>
    public async Task DeleteMedicineAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var babyId = await db.Medicines.Where(m => m.Id == id).Select(m => m.BabyId).FirstOrDefaultAsync(ct);
        if (babyId == 0)
        {
            return;
        }

        await db.Medicines.Where(m => m.Id == id).ExecuteDeleteAsync(ct);
        NotifyChanged(babyId);
    }

    // ---------- doses ----------

    /// <summary>Every dose of every medicine this baby has, given since a moment, newest first.</summary>
    public async Task<List<MedicineDose>> DosesAsync(int babyId, DateTime sinceUtc, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.MedicineDoses
            .Where(d => d.GivenUtc >= sinceUtc
                        && db.Medicines.Any(m => m.Id == d.MedicineId && m.BabyId == babyId))
            .OrderByDescending(d => d.GivenUtc)
            .ToListAsync(ct);
    }

    /// <summary>Records a dose, or saves changes to one.</summary>
    public async Task<MedicineDose?> SaveDoseAsync(MedicineDose dose, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var babyId = await db.Medicines.Where(m => m.Id == dose.MedicineId).Select(m => m.BabyId).FirstOrDefaultAsync(ct);
        if (babyId == 0)
        {
            return null;
        }

        MedicineDose row;
        if (dose.Id == 0)
        {
            row = new MedicineDose { MedicineId = dose.MedicineId };
            db.MedicineDoses.Add(row);
        }
        else
        {
            row = await db.MedicineDoses.FirstOrDefaultAsync(d => d.Id == dose.Id, ct)
                  ?? throw new InvalidOperationException("That dose has gone.");
        }

        row.GivenUtc = dose.GivenUtc;
        row.Dose = Clean(dose.Dose, 60);
        row.Notes = Clean(dose.Notes);

        await db.SaveChangesAsync(ct);
        NotifyChanged(babyId);
        return row;
    }

    public async Task DeleteDoseAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var babyId = await db.MedicineDoses
            .Where(d => d.Id == id)
            .Join(db.Medicines, d => d.MedicineId, m => m.Id, (d, m) => m.BabyId)
            .FirstOrDefaultAsync(ct);
        if (babyId == 0)
        {
            return;
        }

        await db.MedicineDoses.Where(d => d.Id == id).ExecuteDeleteAsync(ct);
        NotifyChanged(babyId);
    }

    private static string? Clean(string? text, int max = 500)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
