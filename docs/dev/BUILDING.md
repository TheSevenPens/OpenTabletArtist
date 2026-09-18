# Building OpenTabletArtist from source

For end users, download a prebuilt release instead — see the [install guide](../user/INSTALL.md). This doc is
for building and running from source.

## Prerequisites

- **.NET 10 SDK** — builds and runs the app.
- **.NET 8 SDK** — the bundled OpenTabletDriver daemon (and our pen-dynamics plugin) target `net8.0`.
- **Git** — OpenTabletDriver is a submodule, so clone recursively.
- **Windows** for the full experience (VMulti / Windows Ink are Windows-only). The app builds and runs on
  macOS/Linux too, with the Windows-only surface hidden; see [design/macos/](../design/macos/) and
  [design/linux/](../design/linux/).

No separate OpenTabletDriver install is needed — it's built from the submodule and the app auto-starts it.

## Clone

```bash
git clone --recursive https://github.com/TheSevenPens/OpenTabletArtist.git
cd OpenTabletArtist
```

Already cloned without `--recursive`? Initialize the submodule: `git submodule update --init --recursive`.

## Build & run

```powershell
./scripts/build.ps1                  # the app, plus the OTD daemon it needs
dotnet run --project OpenTabletArtist
```

> **The daemon is not in the solution.** `dotnet build OpenTabletArtist.slnx` builds the app, the tests
> and the plugin — but **not** `OpenTabletDriver.Daemon.exe`, so the app will sit at **"Not connected"**
> with nothing to launch.
>
> That is deliberate (#786). OTA is moving to shipping OpenTabletDriver's *own released binary* rather
> than one we build, because the OTD maintainers cannot support a binary they did not produce. A build
> that quietly emits a daemon makes "which OpenTabletDriver am I actually running?" harder to answer, and
> that question is about to matter a great deal more.

To build it by hand instead of using the script:

```bash
dotnet build external/OpenTabletDriver/OpenTabletDriver.Daemon/OpenTabletDriver.Daemon.csproj -c Debug
```

Or skip it entirely and point the app at an OpenTabletDriver you already have installed — the app finds
one and adopts it.

On launch the app auto-starts the daemon if it isn't already running, then connects.

### Prefer the build script

`scripts/build.ps1` builds the solution **and the daemon**, and first clears the usual blockers — it
stops a running app/daemon that would lock the build outputs, initializes the OTD submodule if it's
missing, and confirms the daemon exe was produced:

```powershell
./scripts/build.ps1                       # Debug build: solution + daemon
./scripts/build.ps1 -Test                 # also run the xUnit suite
./scripts/build.ps1 -Configuration Release
./scripts/build.ps1 -SkipDaemon           # solution only, for driving an installed OTD
```

## Tests

```bash
dotnet test OpenTabletArtist.slnx
```

## Troubleshooting

**App sits at "Not connected" / the daemon page says `OpenTabletDriver.Daemon.exe` wasn't found.** The
daemon is not in the solution, so neither a solution build nor a test run produces it — it is a
standalone project the app launches as a separate process. Run `./scripts/build.ps1`, or build the daemon
project directly (see **Build & run** above), or install OpenTabletDriver and let the app adopt it.

**Build fails with "file is locked by OpenTabletArtist".** The running app or daemon holds the build
outputs (the app exe or the OTD DLLs) open. Use `scripts/build.ps1`, which stops those processes before
building so this can't happen. To fix it by hand, close the running app **and** stop the daemon (Task
Manager → `OpenTabletDriver.Daemon.exe`, or the tray's Quit + Stop) before rebuilding. If a previous
instance hasn't fully exited it may still hold the `.exe` (a cleaner-shutdown item is tracked in
[FUTURES.md](FUTURES.md)).

See [ARCHITECTURE.md](ARCHITECTURE.md) for the codebase layout, the daemon communication model, and how
releases are cut.
