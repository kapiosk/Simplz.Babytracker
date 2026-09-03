# Simplz.Babytracker

A small Blazor Server PWA for tracking a newborn: breast feeding (start/stop with a live timer),
bottle feeding (formula or breast milk, with amount), poops, urine and vomits — plus a report view.
More than one baby can be tracked, each with its own log.
It is behind a password: one for the parents, one that gives a doctor the same views read only.
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
which feed. With the parent password every cell and every `+` is a button that opens that entry for editing.

**Report** (`/report`) — today / 7 days / 30 days / custom range, with:

- totals per type: feed count, total and average feeding time, bottle count and total ml split by milk type,
  nappy and vomit counts with a per-day average,
- a day-by-day table covering every day of the range,
- the full list of entries, each editable with the parent password.

Both pages are live: log something on one phone and any other open phone updates itself.

## Photos and clips

The pencil on any entry opens the editor, and at the bottom of it is somewhere to attach photos
and video — a stool worth showing a doctor, mostly. They appear as thumbnails under the entry in
the recent log and on the report, and tapping one opens it full size.

The read-only password sees them too. That is the point: a doctor being able to look at the
photograph is most of the reason for taking one. It cannot add or remove any.

- Up to **25 MB** a photo and **200 MB** a clip, stored exactly as they arrive — a Pi is no place
  to be re-encoding video, so nothing is resized or transcoded.
- The upload is an ordinary form post rather than anything travelling over the circuit. Blazor
  would carry the bytes through SignalR in small pieces, which is slow for a photo and hopeless
  for a video, and the circuit is the least reliable thing here.
- Files live in `/data/media` beside the database, named by a generated id rather than anything
  the phone sent, so a copy of `/data` is still the whole backup. Deleting an entry deletes its
  files with it.
- They are served through `/media/{id}`, which is behind the password like every other page —
  not a public folder. **Serve the app over HTTPS** before there are photographs of a child on
  it; over plain HTTP they cross the network in the clear like everything else.

An entry has to exist before anything can be attached to it, so a new one is saved first and then
reopened.

## More than one baby

Every entry belongs to a baby. With one baby nothing changes — the header stays as it was and the
five buttons keep the screen. Add a second on **Babies** (the person icon in the header) and a row
of names appears under the header: tap one to switch, and **Track**, **Chart** and **Report** all
follow. Each baby keeps its own log entirely; nothing is ever shown side by side.

The choice rides on the sign-in cookie as a claim rather than in a cookie of its own. The pages are
rendered on the server and already receive the signed-in user, so the selection arrives with them —
and it lasts as long as the sign-in does, which is to say months. Switching is a form post, not a
button on the circuit, so it still works when the connection does not. It is per device: one phone
can sit on one baby while another sits on the other.

The doctor password sees both, read only, and can switch between them.

## Signing in

There are no user accounts, only two shared passwords:

| Password | What it gives |
| --- | --- |
| `parent` | Everything: logging, editing, deleting. Lands on **Track**. |
| `doctor` | The same **Track**, **Chart** and **Report**, with every button that writes removed. Lands on **Chart**. |

Read-only is not just hidden UI. The pages are rendered on the server, so a read-only visitor's page
has no edit handlers in it at all, and each write path re-checks the role before touching the database.

The container starts with the passwords in `compose.yaml` (or the `Auth__ParentPassword` and
`Auth__DoctorPassword` environment variables). Either can then be changed from **Passwords**, linked
from the Babies screen — a password set there is stored hashed (PBKDF2) in the database and wins over
the configured one, so the value in `compose.yaml` becomes only the starting point. Changing one asks
for the current parent password first, so an unlocked phone left on a table is not enough on its own.

The sign-in cookie lasts 180 days and slides, so a phone with the app on its home screen stays signed
in; the keys that sign it are kept in `/data/keys`, next to the database, so rebuilding the container
does not sign everybody out.

Because of that, **changing a password does not sign anyone out** — a phone already signed in stays
signed in for months whatever the password becomes. When that is the point of changing it, tick *sign
out every device*, which moves a stamp the cookies are issued under and makes every one of them,
including the one doing the asking, worthless.

## Running it

### Docker (how it is meant to be deployed)

```bash
docker compose up -d --build
```

The app is then on <http://localhost:4549>. Three things to check in `compose.yaml` before the first run:

- `Auth__ParentPassword` / `Auth__DoctorPassword` — change them from the defaults.
- `TZ` — set it to your own timezone. It decides which day an entry belongs to and what times are shown.
- the port mapping, if 4549 is taken.

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
  the app already honours `X-Forwarded-Proto`/`X-Forwarded-For`. Over plain HTTP the password and the
  sign-in cookie cross the network in the clear, so TLS matters here beyond installability.
- **It is not an offline app.** Blazor Server renders over a live connection, so entries can only be logged
  while the phone can reach the server. The service worker caches the shell and shows a friendly
  "you're offline" page instead of a browser error; it deliberately never caches the app's HTML or its
  SignalR traffic.

## When the connection drops

Blazor Server renders over a live SignalR circuit: every tap travels to the container and the
server sends the new HTML back. When that circuit dies — the phone slept, the wifi dropped, the
container restarted — the page keeps looking perfectly fine while nothing it does reaches the
server. Buttons that do nothing, and no error to explain it.

Three things deal with that:

- **A Reload button** (the ↻ in the header, and a second one in the banner). It is plain JavaScript
  rather than a Blazor click handler, because the circuit is exactly what is broken when it is
  needed. Nothing is lost by reloading: a running feed lives in the database, not in the page.
- **`wwwroot/circuit-watchdog.js`** asks the server directly instead of waiting for Blazor's own
  30 second timeout to notice. A tiny round trip over the circuit (`Services/CircuitPing.cs`) runs
  whenever the page comes back on screen, when the network returns, and every 20 seconds while the
  page is being looked at. No answer within 5 seconds means the circuit is dead: it rejoins, and
  reloads the page if rejoining fails. A red banner says which is happening.
- **Reconnecting keeps trying.** Blazor gives up after 30 attempts by default, having already
  slowed to one attempt every 30 seconds; here it retries for as long as it takes, and disconnected
  circuits are kept on the server for 15 minutes instead of 3, so a phone that was locked for a
  while gets its own session back rather than a reload.

## Layout

| Path | What's in it |
| --- | --- |
| `Data/` | `Baby`, `BabyEvent`, `EventMedia` and `AppSetting` entities, `AppDbContext`, EF Core migrations |
| `Services/EventService.cs` | All reads and writes, plus a `Changed` event that pushes updates to open pages |
| `Services/Display.cs` | UTC↔local conversion and the shared label/duration formatting |
| `Services/ChartGrouping.cs` | Rebuilds the flat log into paper-chart rows (feed + its outputs) |
| `Services/BabyService.cs` | The babies, and which one a device has selected (a claim on the sign-in cookie) |
| `Services/MediaService.cs` | The photos and clips: the files on the volume and the rows that point at them |
| `Services/CircuitPing.cs` | The round trip the browser makes to check its own circuit is still alive |
| `Services/Auth.cs` | The roles, the configured passwords, and the check the pages use before rendering anything that writes |
| `Services/Credentials.cs` | Which password lets you in: stored and hashed if it has been changed, from the configuration if not |
| `Components/Pages/` | `Home.razor` (the buttons), `Chart.razor`, `Report.razor`, `ManageBabies.razor`, `Password.razor`, `Login.razor` |
| `Components/` | `EventList`, `BottleDialog`, `EditEventDialog`, `TimeField`, `RangePicker`, `OutputMarks`, `Icon` |
| `wwwroot/` | `app.css`, `circuit-watchdog.js`, `media-upload.js`, `manifest.webmanifest`, `service-worker.js`, `offline.html`, icons |

Times are stored in UTC and rendered in the server's local timezone (`TZ`). Everything that has to
survive a rebuild is under `/data`: the database, the attachments in `media/`, and the keys that
sign the cookie in `keys/`.

## Changing the schema

```bash
dotnet tool restore
dotnet dotnet-ef migrations add <Name> -o Data/Migrations
```

Migrations are applied automatically the next time the app starts.

## License

MIT — see [LICENSE](LICENSE).
