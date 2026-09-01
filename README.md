# Simplz.Babytracker

A small Blazor Server PWA for tracking a newborn: breast feeding (start/stop with a live timer),
bottle feeding (formula or breast milk, with amount), poops, urine and vomits — plus a report view.
Data lives in a single SQLite file, and the whole thing ships as one Docker container.

## What it does

**Track** (`/`) — five large buttons, sized for one thumb at 3 a.m.

- **Breast feed** — tap once to start, the button turns into a running timer, tap again to stop.
  A session survives a page reload, a phone lock, or logging from a different device.
- **Bottle** — opens a sheet: breast milk or formula, amount in ml (presets or free text), optional note.
- **Poop**, **Urine**, **Vomit** — one tap each, logged at the current time.

Nothing has to be logged as it happens. Under the buttons is the recent log; the pencil on any row opens
an editor where the type, the times, the milk, the amount and the notes can be corrected, or the entry
deleted. Every time field has −1h/−15m/−5m/+5m/+15m/+1h nudges and a *Now* button next to the picker, so
correcting a feed to "actually, it started twenty minutes ago" is two taps.

- **Add an entry from earlier** (under the log on both pages) creates an entry after the fact, pre-filled
  as a feed that started 15 minutes ago and has just ended — including both the start *and* the stop time.
- A feed that is currently running shows **Started at HH:MM — adjust** under the buttons: nudge the start
  time backwards and the live timer follows, without stopping the feed.
- *Still feeding* on the stop field clears it again, turning a finished feed back into a running one.

**Chart** (`/chart`) — the paper-chart view: one row per feed, with the nappies and spit-ups recorded
against it, grouped under a header per day.

```
TIME    BREAST                  BOTTLE   💧  💩  🤮
04:30   04:30 → 04:50   20m     90  F    +   +
11:45   11:45 → 12:45   1h 00m  90  F    +
16:20   16:20 → 17:00   40m     75  BM   +   +
19:00                           90  F    +   +
```

A breast feed and a bottle within 20 minutes of each other share a row (breast plus a top-up), and an
output within 4 hours of a feed is recorded against that feed — so it's obvious which stool followed
which feed. Every cell and every `+` is a button that opens that entry for editing.

**Report** (`/report`) — today / 7 days / 30 days / custom range, with:

- totals per type: feed count, total and average feeding time, bottle count and total ml split by milk type,
  nappy and vomit counts with a per-day average,
- a day-by-day table covering every day of the range,
- the full list of entries, each editable.

Both pages are live: log something on one phone and any other open phone updates itself.

## Running it

### Docker (how it is meant to be deployed)

```bash
docker compose up -d --build
```

The app is then on <http://localhost:8080>. Two things to check in `compose.yaml` before the first run:

- `TZ` — set it to your own timezone. It decides which day an entry belongs to and what times are shown.
- the port mapping, if 8080 is taken.

The database lives in the `babytracker-data` volume as `/data/babytracker.db`. The container runs as a
non-root user (uid 1654), and a named volume inherits that ownership, which is why it is the default.
To keep the file next to the compose file instead, swap the volume for a bind mount as described in
`compose.yaml` — the host folder has to be chowned to 1654 first.

Back it up by copying the file out:

```bash
docker cp babytracker:/data/babytracker.db ./babytracker-backup.db
```

### Locally, without Docker

```bash
dotnet run
```

The database goes to `App_Data/babytracker.db`. The schema is created and migrated automatically at startup.

## Installing it on a phone

Open the app in the phone's browser and use *Add to home screen*. It then runs full screen with its own
icon, like a native app.

Two caveats worth knowing:

- **Serve it over HTTPS.** Browsers only offer installation (and only register the service worker) on
  `https://` origins or on `localhost`. Put the container behind a reverse proxy that terminates TLS —
  the app already honours `X-Forwarded-Proto`/`X-Forwarded-For`.
- **It is not an offline app.** Blazor Server renders over a live connection, so entries can only be logged
  while the phone can reach the server. The service worker caches the shell and shows a friendly
  "you're offline" page instead of a browser error; it deliberately never caches the app's HTML or its
  SignalR traffic.

## Layout

| Path | What's in it |
| --- | --- |
| `Data/` | `BabyEvent` entity, `AppDbContext`, EF Core migrations |
| `Services/EventService.cs` | All reads and writes, plus a `Changed` event that pushes updates to open pages |
| `Services/Display.cs` | UTC↔local conversion and the shared label/duration formatting |
| `Services/ChartGrouping.cs` | Rebuilds the flat log into paper-chart rows (feed + its outputs) |
| `Components/Pages/` | `Home.razor` (the buttons), `Chart.razor`, `Report.razor` |
| `Components/` | `EventList`, `BottleDialog`, `EditEventDialog`, `TimeField`, `RangePicker`, `OutputMarks`, `Icon` |
| `wwwroot/` | `app.css`, `manifest.webmanifest`, `service-worker.js`, `offline.html`, icons |

Times are stored in UTC and rendered in the server's local timezone (`TZ`).

## Changing the schema

```bash
dotnet tool restore
dotnet dotnet-ef migrations add <Name> -o Data/Migrations
```

Migrations are applied automatically the next time the app starts.

## License

MIT — see [LICENSE](LICENSE).
