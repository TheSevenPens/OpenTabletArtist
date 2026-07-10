# macOS dev environment + verification tooling

> Sibling of the [feasibility hub](../140-macos-feasibility.md). Everything needed to **build, run, and verify**
> OTA on macOS: toolchain bootstrap, the environment quirks that bit us, and the tools/checks used to prove
> each capability. Grounded in a real run on Apple-Silicon macOS 26 (Darwin 25.5) with a Wacom Movink 13.

## Toolchain

| Component | Version used | Notes |
|---|---|---|
| OTA app | `net10.0` | The Avalonia app. |
| OTD daemon (submodule) | `net8.0`, **v0.6.7** | Needs the **.NET 8 runtime** to run standalone (or bundle self-contained). |
| .NET SDK | **10.0.x** | Builds both (`net10` SDK builds `net8` targets fine). |

### Bootstrap

```sh
# .NET 10 SDK (builds OTA + the daemon). User-local, no sudo, coexists with other installs.
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh && chmod +x /tmp/dotnet-install.sh
/tmp/dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"

# .NET 8 *runtime* — only needed to run a submodule-built daemon standalone (a shipped daemon bundles its own).
/tmp/dotnet-install.sh --channel 8.0 --runtime dotnet --install-dir "$HOME/.dotnet"

export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
```

### Build

```sh
# The daemon (net8, arm64)
dotnet build external/OpenTabletDriver/OpenTabletDriver.Daemon/OpenTabletDriver.Daemon.csproj -c Release   # 0/0

# The app (net10)
dotnet build OpenTabletArtist/OpenTabletArtist.csproj -c Release                                           # 0/0

# Tests
dotnet test tests/OpenTabletArtist.Tests/OpenTabletArtist.Tests.csproj -c Release                          # 564 pass
```

## Environment quirks (learned the hard way)

- **The `dotnet` muxer can break when mixing runtime installs.** On macOS 26, installing the net8 runtime over
  the net10 SDK left `~/.dotnet/dotnet` in a state that exited 137 (SIGKILL) on every invocation. Restoring a
  clean host binary fixed it. **The reliable workaround throughout was to launch apps by their native
  apphost** (e.g. `OpenTabletArtist/bin/Release/net10.0/OpenTabletArtist`) with `DOTNET_ROOT` set, rather than
  `dotnet <dll>`.
- **The submodule daemon is `net8`** — running it standalone needs the .NET 8 runtime and inherits
  `DOTNET_ROOT`. For a shipped macOS release, **publish the daemon self-contained** so it doesn't depend on a
  system install.
- **The daemon binds its pipe lazily.** `CoreFxPipe_OpenTabletDriver.Daemon` (a Unix domain socket under
  `$TMPDIR`) appears only *after* the daemon finishes startup/tablet-detection (several seconds), and .NET
  unlinks the socket path after bind. **Poll by connecting, not by `test -S`.** `DaemonClient`'s connect-loop
  already retries for exactly this.
- **`screencapture -D <n>` display indices don't match OTA's display numbering** — useful when scripting
  screenshots for verification.

## Verification tooling

The port was proven with four tools/checks. Keep using them per phase.

### 1. `tools/DaemonProbe` — headless daemon smoke test

A committed, standalone tool (not in the solution) that mirrors `DaemonClient`'s exact transport
(`NamedPipeClientStream("OpenTabletDriver.Daemon")` → `JsonRpc` → `InvokeAsync`) and round-trips
`GetTablets` + `GetSettings`. The fastest way to confirm the daemon connection on any OS.

```sh
dotnet run --project tools/DaemonProbe            # against whatever daemon is listening
dotnet run --project tools/DaemonProbe -- OpenTabletDriver.Daemon 8000   # custom pipe / timeout
```

Expected (daemon up, tablet connected):

```
[probe] GetTablets OK -> 1 tablet(s)
          - Wacom Movink 13 (DTH-135)
[probe] GetSettings OK -> settings object returned  (Profiles: 2)
[probe] ROUND-TRIP SUCCEEDED.
```

Exit codes: `0` OK · `2` couldn't connect · `3` connected but an RPC call failed. See
`tools/DaemonProbe/README.md`.

### 2. Avalonia screens harness (throwaway)

A minimal Avalonia app that boots the real macOS backend and prints `Screens.All` — proves the display data
the `AvaloniaScreensDisplayEnumerator` reads. On the test rig it reported:

```
ScreenCount = 2
  #1  ASUS PA329CV   1920x1080 @(0,0)     primary
  #2  Wacom DTH135    960x540  @(0,1080)  secondary
```

Note the **friendly names** (better than the old "names may be blank" assumption) and that geometry is in
**logical points** (a 4K panel reports 1920×1080) — the same space OTD's macOS output uses.

### 3. Live GUI checks

Launch the app and drive it (screenshots + macOS accessibility). What to confirm per phase:

- **Boots + connects:** Home shows the detected tablet with correct specs; **zero exceptions** in the log.
- **Gating:** Home "Needs attention" is empty (no VMulti/Ink nags); ADVANCED rail hides the Windows-only tabs.
- **Output mode:** the tablet reads **Absolute**; the Calibration tab shows the density picker + START.
- **Daemon card:** "Daemon running v0.6.7 · Bundled daemon"; Restart launches the bundled daemon.
- **Tray:** a menu-bar item appears.

> Driving Avalonia buttons via macOS System Events is finicky (nested content) — radio/tab controls respond to
> `click at`, but plain buttons often need a real click. For calibration specifically, a **pen tap** is the
> reliable trigger.

### 4. The calibration report as a coordinate oracle

The Calibration tab produces a per-point report (target / measured / delta, plus RMS). **This is the fastest
way to diagnose overlay coverage / coordinate bugs on macOS.** The delta pattern is diagnostic:

- **uniform constant offset** → the overlay isn't at the display origin (menu-bar constraint).
- **offset that grows toward one edge** → the drawable area is inset/scaled.
- **≈2× scaled deltas** → a points-vs-pixels mismatch.

On the rig, a broken run showed a systematic ~30 px vertical offset (worst at the top) — the exact fingerprint
of AppKit constraining the window below the menu bar. After the overlay coverage fix, the deltas were small
and centred and the pen tracked the nib. A one-off geometry probe (window `Position`/`Bounds`/`Surface.Bounds`
vs. `screen.Bounds`) confirmed the window then sat at the display origin with full coverage.

The fix is `CalibrationOverlayWindow.CoverFullDisplayOnMac()` (macOS plan Phase 3) — it raises the `NSWindow`
above `kCGMainMenuWindowLevel` and sets its frame to the full `NSScreen` frame. So this report is also the
**acceptance gate** for that fix: after a calibration, a uniform vertical offset means the overlay is *not*
covering the full display and the ObjC frame/level call didn't take.

## Reproducing the end-to-end proof

1. Bootstrap the toolchain (above).
2. Build the daemon + app (0/0).
3. Ensure a daemon is running (OTD.app, or launch the submodule build's apphost with `DOTNET_ROOT` set).
4. `dotnet run --project tools/DaemonProbe` → round-trip succeeds.
5. Launch the app apphost; confirm the live GUI checks above.
6. Run a calibration with the pen; confirm small centred deltas + nib tracking.
