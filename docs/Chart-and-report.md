# The chart and the report

Both take a range at the top: **Today**, **7 days**, **30 days**, or **Custom** for two dates you
pick.

## Chart

The paper chart from the hospital, rebuilt from what you logged. One row per feed, with the nappies
and spit-ups that followed it on the same line.

```
TIME    BREAST                  BOTTLE   💧  💩  🤮
16:20   16:20 → 17:00   40m     75  BM   +   +
11:45   11:45 → 12:45   1h 00m  90  F    +
04:30   04:30 → 04:50   20m     90  F    +   +
```

Two rules decide what shares a row, and they are worth knowing because they are why the chart looks
tidier than the raw log:

- **A bottle within 20 minutes of a breast feed joins that feed's row** — a top-up is part of the
  same feed, not a separate one.
- **A nappy or a spit-up within 4 hours of a feed is recorded against that feed** — so you can see
  which feed a stool followed, which is usually the question being asked.

Days run newest first, and so do the entries inside each day.

With the parent password every cell and every `+` is a button that opens that entry for editing.
With the doctor password it is the same chart, without the buttons.

Sleep is not on the chart. Sleeps do not line up with feeds, so a column here would say something
untrue about when they happened — the report is where sleep is counted.

## Report

Totals across the top:

- **Breast feeds** — how many, total time, average length
- **Sleep** — total, how many, and the longest single stretch
- **Bottles** — how many, total millilitres, split between formula and breast milk
- **Poops**, **Urine**, **Vomits** — how many, and a per-day average when the range is longer than
  a day

Below that, a row per day covering every day in the range including the quiet ones, and then the
entries themselves. Each is editable with the parent password.

Only finished sleeps and feeds count towards the totals — one still running has no length yet.

## Trends

The same measures as the report, drawn instead of listed: sleep, breast feeds, bottles and poops,
one small bar chart each.

Over a range of days it draws a bar a day, so a week or a month has a shape — feeding settling
down, sleep stretching out, nappies steady. On **Today** it draws a bar an *hour* instead, which
tells you when things happened; a single day as one bar would say nothing at all.

### Reading the exact figures

**Over a week or less, every bar carries its own number** underneath it, so the whole range reads
at a glance with nothing to tap. The unit is left off there — the chart's own title already says
whether it is hours, millilitres or a count — and a day's sleep is shortened to *12h20*.

**Over a longer range, or by hour, there is no room for that.** So the tallest bar keeps its
figure, to give the scale, and **Every day** (or **Every hour**) under the chart opens the full
list: one line per bucket, with a dash for the ones where nothing happened. It is an ordinary
expander, so it works even when the connection has dropped.

The figure beside a chart's title is always the whole range.

There is deliberately one measure per chart rather than everything on one — sleep is in hours and
bottles are in millilitres, and putting two scales on one axis draws something that looks like a
fact and is not one.

## Which to show a doctor

The chart, generally: it is the format they already read, and the groupings answer *what went in
and what came out, in what order*. The report is better for *how much, over how long*.
