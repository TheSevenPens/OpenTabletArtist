# Building OpenTabletArtist from source

For end users, download a prebuilt release instead — see the [install guide](INSTALL.md). This doc is
for building and running from source.

## Prerequisites

- **.NET 10 SDK** — builds and runs the app.
- **.NET 8 SDK** — the bundled OpenTabletDriver daemon (and our pen-dynamics plugin) target `net8.0`.
- **Git** — OpenTabletDriver is a submodule, so clone recursively.
- **Windows** for the full experience (VMulti / Windows Ink are Windows-only). The app builds and runs on
  macOS/Linux too, with the Windows-only surface hidden; see [design/macos/](design/macos/).

No separate OpenTabletDriver install is needed — it's built from the submodule and the app auto-starts it.

## Clone

```bash
git clone --recursive https://github.com/TheSevenPens/OpenTabletArtist.git
cd OpenTabletArtist
```

Already cloned without `--recursive`? Initialize the submodule: `git submodule update --init --recursive`.

## Build & run

```bash
dotnet build OpenTabletArtist.slnx   # builds the app AND the OTD daemon from the submodule
dotnet run --project OpenTabletArtist
```

> **Build the solution (`.slnx`), not just `OpenTabletArtist/`.** The daemon
> (`OpenTabletDriver.Daemon.exe`) is a separate project built from the submodule; if you build only the
> app it won't exist and the app will sit at **"Not connected"**.

On launch the app auto-starts the daemon if it isn't already running, then connects.

### Prefer the build script

`scripts/build.ps1` builds the solution and first clears the usual blockers — it stops a running
app/daemon that would lock the build outputs, initializes the OTD submodule if it's missing, and confirms
the daemon exe was produced:

```powershell
./scripts/build.ps1                       # Debug build of the solution
./scripts/build.ps1 -Test                 # also run the xUnit suite
./scripts/build.ps1 -Configuration Release
```

## Tests

```bash
dotnet test OpenTabletArtist.slnx
```

## Troubleshooting

**App sits at "Not connected" / the daemon page says `OpenTabletDriver.Daemon.exe` wasn't found.** You
built only the app project (or only ran the tests), which does **not** produce the daemon exe — it's a
standalone project the app launches as a separate process. Build the whole solution
(`dotnet build OpenTabletArtist.slnx`).

**Build fails with "file is locked by OpenTabletArtist".** The running app or daemon holds the build
outputs (the app exe or the OTD DLLs) open. Use `scripts/build.ps1`, which stops those processes before
building so this can't happen. To fix it by hand, close the running app **and** stop the daemon (Task
Manager → `OpenTabletDriver.Daemon.exe`, or the tray's Quit + Stop) before rebuilding. If a previous
instance hasn't fully exited it may still hold the `.exe` (a cleaner-shutdown item is tracked in
[FUTURES.md](FUTURES.md)).

See [ARCHITECTURE.md](ARCHITECTURE.md) for the codebase layout, the daemon communication model, and how
releases are cut.
