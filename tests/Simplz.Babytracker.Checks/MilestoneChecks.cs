using System.Text.RegularExpressions;
using Simplz.Babytracker.Services;

namespace Simplz.Babytracker.Checks;

/// <summary>The milestone guide, what it offers when, and recording against it.</summary>
internal static class MilestoneChecks
{
    public static async Task RunAsync(Harness h)
    {
        TheGuide(h);
        await Recording(h);
        await Earlier(h);
    }

    private static void TheGuide(Harness h)
    {
        h.Section("milestones: the guide");

        var guide = Milestones.Guide;
        h.Check("has entries", guide.Count > 10, true);
        h.Check("every range is a range, never a single age", guide.All(g => g.ToWeeks > g.FromWeeks), true);
        h.Check("keys are unique", guide.Select(g => g.Key).Distinct().Count(), guide.Count);
        h.Check("in the order a baby meets them", guide.Zip(guide.Skip(1)).All(p => p.First.FromWeeks <= p.Second.FromWeeks), true);
        h.Check("a known one is found", Milestones.Find("smiles")?.Title, "Smiles at you");
        h.Check("an unknown one is not", Milestones.Find("nope"), null);

        h.Section("milestones: nothing ever judges the baby");

        var smile = Milestones.Find("smiles")!;
        h.Check("before the range", Milestones.When(smile, 2).Contains("usually"), true);
        h.Check("inside the range", Milestones.When(smile, 6), "around now, for many babies");
        h.Check("with no age known", Milestones.When(smile, null).Contains("usually"), true);

        // Every phrasing of every entry, at four ages. Whole words: "later" is reassurance and
        // "late" is a verdict, and a substring match cannot tell them apart.
        string[] forbidden = ["late", "missed", "overdue", "behind", "delayed", "fail", "should"];
        var phrasings = guide
            .SelectMany(g => new[] { Milestones.When(g, null), Milestones.When(g, 1), Milestones.When(g, g.FromWeeks), Milestones.When(g, 500) })
            .ToList();
        var judging = phrasings
            .SelectMany(t => Regex.Matches(t.ToLowerInvariant(), "[a-z]+").Select(m => m.Value))
            .Where(w => forbidden.Contains(w))
            .Distinct();
        h.Check("no judging word anywhere", string.Join(",", judging), "");
        h.Check("and the reassuring one is still said", phrasings.Any(t => t.Contains("later")), true);
    }

    private static async Task Recording(Harness h)
    {
        h.Section("milestones: recording");
        await h.ResetAsync();

        var born = new DateOnly(2026, 1, 15);

        await h.Milestones.ReachedAsync(h.BabyId, "smiles", born.AddDays(40), "in the bath");
        var list = await h.Milestones.ForBabyAsync(h.BabyId);
        h.Check("one row", list.Count, 1);
        h.Check("the key", list[0].Key, "smiles");
        h.Check("the day it happened, not the day it was typed", list[0].ReachedOn, born.AddDays(40));
        h.Check("the note", list[0].Notes, "in the bath");

        await h.Milestones.ReachedAsync(h.BabyId, "smiles", born.AddDays(38));
        list = await h.Milestones.ForBabyAsync(h.BabyId);
        h.Check("recording it again does not duplicate it", list.Count, 1);
        h.Check("it moves the date", list[0].ReachedOn, born.AddDays(38));
        h.Check("and keeps the note", list[0].Notes, "in the bath");

        await h.Milestones.AddOwnAsync(h.BabyId, "Slept through", born.AddDays(120));
        await h.Milestones.AddOwnAsync(h.BabyId, "   ", born.AddDays(121));
        list = await h.Milestones.ForBabyAsync(h.BabyId);
        h.Check("one of your own is added", list.Count, 2);
        h.Check("an empty one is not", list.Count(m => m.Title == "Slept through"), 1);
        h.Check("your own has no key", list.Single(m => m.Title == "Slept through").Key, null);
        h.Check("newest first", list[0].Title, "Slept through");

        await h.Milestones.AddOwnAsync(h.BabyId, "Slept through", born.AddDays(150));
        h.Check("two of your own may say the same thing",
            (await h.Milestones.ForBabyAsync(h.BabyId)).Count(m => m.Title == "Slept through"), 2);

        var own = (await h.Milestones.ForBabyAsync(h.BabyId)).First(m => m.Key is null);
        await h.Milestones.RemoveAsync(own.Id);
        h.Check("removing one", (await h.Milestones.ForBabyAsync(h.BabyId)).Any(m => m.Id == own.Id), false);
        await h.Milestones.RemoveAsync(999_999);
        h.Check("removing one that is not there is harmless", true, true);

        h.Section("milestones: the birth date");
        await h.Babies.SetBornOnAsync(h.BabyId, born);
        h.Check("is saved", (await h.Babies.GetAsync(h.BabyId))?.BornOn, born);
        await h.Babies.SetBornOnAsync(h.BabyId, null);
        h.Check("and can be cleared", (await h.Babies.GetAsync(h.BabyId))?.BornOn, null);

        h.Section("milestones: each baby its own");
        var other = await h.AddBabyAsync("Second");
        h.Check("another baby starts with none", (await h.Milestones.ForBabyAsync(other)).Count, 0);
        await h.Milestones.ReachedAsync(other, "smiles", born.AddDays(50));
        h.Check("recording there leaves the first alone",
            (await h.Milestones.ForBabyAsync(h.BabyId)).Single(m => m.Key == "smiles").ReachedOn, born.AddDays(38));

        await using (var db = await h.Factory.CreateDbContextAsync())
        {
            db.Babies.Remove((await db.Babies.FindAsync(other))!);
            await db.SaveChangesAsync();
        }

        h.Check("a deleted baby takes its milestones with it", (await h.Milestones.ForBabyAsync(other)).Count, 0);
    }

    private static async Task Earlier(Harness h)
    {
        h.Section("milestones: around now, and from earlier");
        await h.ResetAsync();

        var none = await h.Milestones.ForBabyAsync(h.BabyId);

        // Thirty weeks, nothing recorded.
        var earlier = Milestones.Earlier(none, 30).ToList();
        var now = Milestones.NextUp(none, 30).ToList();

        h.Check("earlier holds the ones whose range has closed", earlier.All(g => g.ToWeeks < 30), true);
        h.Check("all of them", earlier.Count, Milestones.Guide.Count(g => g.ToWeeks < 30));
        h.Check("smiling is among them at thirty weeks", earlier.Any(g => g.Key == "smiles"), true);
        h.Check("earliest first", earlier.Zip(earlier.Skip(1)).All(p => p.First.FromWeeks <= p.Second.FromWeeks), true);

        // The regression: an older baby with little recorded used to be offered the newborn ones
        // as "around now", which crowded out what actually was.
        h.Check("around now holds only ranges still open", now.All(g => g.ToWeeks >= 30), true);
        h.Check("so never smiling, at thirty weeks", now.Any(g => g.Key == "smiles"), false);
        h.Check("and nothing far ahead", now.All(g => g.FromWeeks <= 34), true);
        h.Check("the two never overlap", now.Select(g => g.Key).Intersect(earlier.Select(g => g.Key)).Any(), false);

        var everything = Milestones.Guide.Select(g => g.Key).ToHashSet();
        var accounted = earlier.Select(g => g.Key)
            .Concat(Milestones.Guide.Where(g => g.ToWeeks >= 30).Select(g => g.Key))
            .ToHashSet();
        h.Check("every one is either earlier or still to come — none falls through", accounted.SetEquals(everything), true);

        await h.Milestones.ReachedAsync(h.BabyId, "smiles", new DateOnly(2026, 2, 20));
        var afterRecording = Milestones.Earlier(await h.Milestones.ForBabyAsync(h.BabyId), 30).ToList();
        h.Check("recording one takes it out of earlier", afterRecording.Any(g => g.Key == "smiles"), false);
        h.Check("and only that one", afterRecording.Count, earlier.Count - 1);

        h.Check("without a birth date there is no earlier", Milestones.Earlier(none, null).Any(), false);
        h.Check("but there is still something to offer", Milestones.NextUp(none, null).Any(), true);

        var smile = Milestones.Find("smiles")!;
        var crawl = Milestones.Find("crawls")!;
        h.Check("recording a closed range is a backfill", Milestones.HasPassed(smile, 30), true);
        h.Check("an open one is not", Milestones.HasPassed(crawl, 30), false);
        h.Check("nor anything without an age", Milestones.HasPassed(smile, null), false);

        h.Check("at nine weeks with nothing recorded, only smiling has closed",
            string.Join(",", Milestones.Earlier(none, 9).Select(g => g.Key)), "smiles");
    }
}
