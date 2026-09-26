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

## Seeing the Windows release states

A dev tree ships no bundled daemon, and one flag reads exactly that:

```csharp
public bool HasBundledDaemon() => File.Exists(DaemonExePaths.BundledPath(AppContext.BaseDirectory));
```

Every Windows-release binding on the **daemon page** hangs off it, so running from `bin/Debug` shows you
the branch a macOS build takes. The page renders, nothing looks broken, and none of the release states are
on screen — which makes a visual pass from a dev tree feel conclusive while establishing nothing (#901).

What stays dark without a bundled daemon:

| | dev tree | Windows release |
|---|---|---|
| THIS APP line | `built against OTD 0.6.7.0` | `bundles OTD 0.6.7.0` |
| provenance chip | `YOURS` only | `BUNDLED`, or `YOURS` on someone else's daemon |
| way back to the bundled copy | cannot appear | offered, or explained when a location is chosen |
| driver card | always shown | hidden until there is something to offer |

### Putting a bundled daemon in place

One file is all the flag reads, so copy any daemon exe next to the app under `Daemon/`:

```powershell
Copy-Item -Recurse <a daemon folder> OpenTabletArtist\bin\Debug\net10.0\Daemon
```

A release package's `Daemon/` folder is the faithful choice, since that is OpenTabletDriver's own released
binary; the submodule build works too if you only care about the UI states, because "is this ours?" is
decided by the path, not by the binary.

**Remove it afterwards.** Left in place, the dev tree goes on behaving like a release build, which is the
same trap in the other direction:

```powershell
Remove-Item -Recurse OpenTabletArtist\bin\Debug\net10.0\Daemon
```

### Keep the run out of your real settings

Set `OTA_APPDATA` (#879) so the run writes its settings and logs somewhere disposable:

```powershell
$env:OTA_APPDATA = "$env:TEMP\ota-scratch"
dotnet run --project OpenTabletArtist
```

It redirects this application's own files only. The daemon has its own data under its own variable, and
the single-instance identity is shared with every other OTA on the machine — so close other instances
first rather than assuming this separates them.

### Reaching the foreign-daemon states

`YOURS` needs a daemon the app did not start and does not manage. Start one from anywhere outside the
app's folder, then launch the app: a running daemon owns the pipe, so OTA connects to it whatever it is.

```powershell
Copy-Item -Recurse <a daemon folder> $env:TEMP\foreign-otd
Start-Process $env:TEMP\foreign-otd\OpenTabletDriver.Daemon.exe -WindowStyle Hidden
```

That is now the only route, and it is the route a user takes too. OTA launches nothing but the copy it
ships, so there is no chosen location to set and no `daemon.userPath` to write: the picker, the setting
and the offered way back to the bundled copy all went with #930. Starting a second copy of OTA's *own*
daemon (a dev tree beside a release, or Debug beside Release) reaches the other external case, the one
the driver card calls "another copy of the OTD daemon this app ships".

> **What none of this gives you** is a machine without the .NET 8 runtime the shipped daemon needs. That
> is #878, and it wants a clean VM or a Windows Sandbox session (#902) — not a dev box with a folder moved
> around.

## Tests

```bash
dotnet test OpenTabletArtist.slnx
```

Four suites run: `OtdInterop.Tests` (OTD integration), `OtdHealth.Tests` (pure diagnostics),
`OpenTabletArtist.Tests` (app logic and health presentation), and `OpenTabletArtist.UiTests`
(views on Avalonia's headless platform, no display needed). Both library suites run without the app
or Avalonia. See [the health library boundary](../design/health-library.md) for standalone commands.

### They run on Microsoft Testing Platform, not VSTest

xUnit v3 test projects are executables that host their own runner, and `global.json` names that runner:

```json
"test": { "runner": "Microsoft.Testing.Platform" }
```

Two things follow, both of which will bite before anyone reads this:

**Run them from this checkout.** `dotnet test` picks its runner from `global.json`, and `global.json` is
resolved from the **current directory**, not from the project path you pass. So an absolute path is not
enough: run from somewhere else and no platform runner is selected, nothing runs, and the command exits
**0**. `scripts/build.ps1` does this for you (it pushes to the repository root and pops back), but an
IDE task or a script of your own that shells out to `dotnet test` has to do the same.

That failure is silent, so it is worth knowing what it looks like: no output at all, and success.

**VSTest options are rejected, not ignored.** `--filter`, `--logger`, `--collect` and friends belong to
the runner that is no longer in the path. Passing one fails the command with `Unknown option` and exit 5
*before any test runs*, which is the behaviour we want — a filter that silently matched nothing and
exited 0 would look like a pass.

**Filtering uses the platform's own options**, after the usual arguments:

```bash
dotnet test tests/OtdInterop.Tests/OtdInterop.Tests.csproj --no-build --filter-class OtdInterop.Tests.BoundaryDependencyTests
dotnet test tests/OpenTabletArtist.Tests/OpenTabletArtist.Tests.csproj --no-build --filter-method OpenTabletArtist.Tests.RotationSelectionTests.OneSelection_IsOneApply
```

`--filter-namespace` selects a namespace. The negating forms are `--filter-not-class`,
`--filter-not-method` and `--filter-not-namespace` — **not** a trailing hyphen, which is xUnit's own
console spelling and is rejected here with `Unknown option`. Running the built test executable directly
is the other route, and that one does take xUnit's switches (`-class`, `-method`, `-class-`).

**Coverage is not collected.** `coverlet.collector` was a VSTest data collector that nothing ever ran —
no workflow passed `--collect` and there is no `.runsettings` — so it was removed rather than replaced.
If coverage is wanted, `Microsoft.Testing.Extensions.CodeCoverage` is the platform's equivalent and
should arrive with a workflow that consumes it.

**In an IDE**, test discovery needs the Testing Platform support switched on: Visual Studio's Test
Explorer has an MTP setting, and Rider has *Settings | Build, Execution, Deployment | Unit Testing |
Testing Platform*. Without it the panes may look empty even though the command line is green.

The remaining piece of [#756](https://github.com/TheSevenPens/OpenTabletArtist/issues/756) is xUnit 4,
which is blocked by `Avalonia.Headless.XUnit` — see the note in that project's `.csproj`.

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
