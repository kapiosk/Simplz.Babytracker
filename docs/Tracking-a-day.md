# Tracking a day

Everything here is on **Track**, the first screen.

## The two that run

**Start breast feed** and **Start sleep** are stretches of time. Tap once to start and the button
becomes a running timer. The timer keeps going if you lock the phone, close the app, or pick it up
on a different phone — it lives on the server, not in the page.

A running **breast feed** carries two buttons, the same as a timed bottle:

- **Pause** for whatever interrupts it. Paused time does not count towards the feed, so a feed
  paused for ten of its forty minutes is recorded as thirty. The tile drains of colour while it is
  paused, so a glance says it is not counting.
- **Finish** stops it and writes it down.

A running **sleep** is still a single tap to wake — there is no pausing it. A baby who wakes and
resettles is either one sleep or two, not a paused one.

A baby cannot be feeding and asleep at the same time, so **starting one ends the other**, at the
moment the new one begins. The message at the bottom of the screen says when that has happened.

Pumping is the exception, and lives on its own tab — see [Pumping](Pumping). Starting it ends
neither of these, because the baby can be asleep while you pump.

### Sleeps under ten minutes are not recorded

A sleep stopped less than ten minutes after it started is taken to be the wrong button rather than
a nap, and is thrown away instead of logged — otherwise every mis-tap sits in the averages pulling
them down. The message at the bottom says when that has happened and offers **Undo**, so a real
eight-minute doze in the car seat is one tap to keep.

This applies to sleep only. A short **breast feed** is kept however brief it was, because six
minutes of feeding is simply a feed. It also applies only to the timer: anything you type in
yourself, or add from earlier, is kept whatever length you give it.

Both lists — **Recent** on Track and the entries on the report — show how long each one lasted
beside its time, or *still asleep* / *still feeding* while it is running.

## Bottles, either way

**Bottle** opens a sheet: breast milk or formula, an amount in millilitres from the presets or
typed in, and a note if you want one. **Save** writes it down at the time you tapped, which is the
quick way and still the usual one — most of the time you are recording a bottle rather than
starting one, often one already half drunk.

Underneath is **Start timing instead**, for when you want the clock. The tile becomes a running
timer with two buttons:

- **Pause** for winding, a nappy, or a baby who has stopped. Paused time does not count towards
  the feed, so twenty minutes of feeding spread over three quarters of an hour is recorded as
  twenty. The tile drains of colour while it is paused, so a glance tells you it is not counting.
- **Finish** asks how much went in, with the length already filled in beside the question.

A timed bottle is one of the things that cannot happen at once with the others: starting one ends
a running sleep or breast feed, and starting either of those ends the bottle.

In the lists a bottle shows both figures — *120 ml · 20m*. One saved in a single tap has no
length, so it just shows the amount.

A bottle is listed against the time it **finished**, not the time it started — when the baby
stopped drinking is the moment you are usually looking for. Everything else is listed by when it
began. A bottle saved in one tap ends where it starts, so there is no difference for those.

## The three that are moments

**Poop**, **Urine** and **Vomit** are logged at the time you tap them: a single tap and nothing
else.

## Nothing has to be logged as it happens

This is the part worth knowing, because it is the difference between a tracker you keep up and one
you abandon at four in the morning.

### Feeding time is its own figure

A feed that was paused did not last the whole stretch between its start and its stop, so the
editor keeps the two apart:

```
stop − start  =  feeding time  +  paused
```

**Moving the stop time does not change the feeding time.** The difference goes to the paused
time, which is what you want when the real correction is "I left the timer running ten minutes
too long" rather than "the baby fed for ten minutes less".

**Feeding time** has its own box if you want to correct it directly, and the line underneath says
how much of the window that leaves as paused. The only time the feeding time gives way is when you
make the window shorter than it — there is no fitting forty minutes of feeding into thirty, so it
is trimmed and says so.

Pump sessions work the same way, as **Pumping time**.

**To fix an entry**, tap the pencil beside it in the log. You can change what it was, when it
started, when it stopped, the amount, the milk and the note. Every time field has one-tap nudges —
−1h, −15m, −5m, +5m, +15m, +1h — and a **Now** button, so correcting a feed to *actually, that
started twenty minutes ago* is two taps rather than a fight with a date picker.

**To add something that already happened**, use **+ Add an entry from earlier**, under the log. It
opens pre-filled as a feed that started fifteen minutes ago and has just ended, so most of the time
you only adjust one field.

**To correct a feed or sleep that is still running**, the line under the buttons — *Breast feed
started at 04:12 — adjust* — opens it. Nudge the start time back and the running timer follows,
without stopping it.

**To turn a finished feed back into a running one**, open it and tap *Still feeding* (or *Still
asleep*) on the stop field. That clears the end time and it starts counting again.

## Everything updates everywhere

Log something on one phone and every other phone showing that baby updates itself. There is nothing
to refresh.
