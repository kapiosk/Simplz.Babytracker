# Checks

```bash
dotnet run --project tests/Simplz.Babytracker.Checks
```

Drives the real services against a fresh SQLite file in a temp folder, migrated from nothing, and
prints one line per check. The last line is the tally; any failure is listed again under it, and
the process exits 1 so a script can stop on it.

## Why a console project

It runs with one command and nothing to install, and the output reads as sentences — *a sleep
stopped within seconds is thrown away* — which is what you want to see when one breaks. It was
first written as a throwaway in a scratch folder, and lost with it; living here is the fix.

## Adding to it

Each area is one class with a `RunAsync(Harness h)` — sleep, bottles, pause, the editor, stock,
display, the average day, milestones. Add a check to the area it belongs to, or a new class and a
line in `Program.cs`.

- Call `h.ResetAsync()` at the start of a group, so it never depends on what ran before it.
- The services stamp *now*, so for something that needs to have lasted a while, create it and then
  move its start back with `h.BackdateAsync`.
- `h.Check(what, actual, expected)` compares by value and falls back to the printed form, so an
  `int` and a `long`, or two `DateOnly`s, agree when they say the same thing.
- Write the expectation from the rule, worked out by hand in a comment where it is not obvious —
  not from whatever the code printed the first time.

## How it stays out of the app

The app's project sits at the repository root with the default globs, which would pull every `.cs`
under `tests/` into the app. `Simplz.Babytracker.csproj` excludes `tests/**`, and `.dockerignore`
leaves the folder out of the image build entirely. There is deliberately no solution file at the
root: the `Dockerfile` runs a bare `dotnet publish` there and would refuse to choose.
