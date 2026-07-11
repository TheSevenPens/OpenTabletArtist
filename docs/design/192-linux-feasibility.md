# Linux support for OpenTabletArtist (#192)

> **Status (2026-07-10): feasibility ASSESSED from the codebase + OTD's Linux support + the macOS port —
> pending live verification on a Linux box.** Unlike the macOS investigation ([#140](140-macos-feasibility.md)),
> which was proven end-to-end on real hardware, this is a paper assessment: the app already **builds and tests
> on a `ubuntu-latest` CI lane**, and the macOS port forced the whole cross-platform architecture, so Linux is a
> **materially smaller lift** than macOS was. What's left is Linux-specific verification + a few Linux-only seams.

> Builds on [#140](140-macos-feasibility.md) (macOS) and [#73](https://github.com/TheSevenPens/OpenTabletArtist/issues/73)
> (cross-platform verification). Read the [macOS feasibility hub](140-macos-feasibility.md) and
> [windows-specific-surface.md](macos/windows-specific-surface.md) first — this doc only covers what's
> *different* on Linux.

## The one-paragraph story

The macOS port (Phases 0–5, merged) converted OTA from "Windows-only" into a **runtime-guarded, one-binary
cross-platform app**: every OS integration is `OperatingSystem.IsWindows()`-guarded (so *non-Windows* — Linux
included — already degrades gracefully), the OS concerns sit behind seams (`IDisplayEnumerator`,
`IDaemonLifecycleService`, `PlatformShell`, …), the Windows-only surface is gated off, and there's a
`ubuntu-latest` CI lane that builds + runs the full test suite. So on Linux, OTA should already **compile, boot,
gate its UI, and connect to a daemon** — because the gating checks `!IsWindows()`, not `IsMacOS()`. The Linux
work is (1) **verify** that shared core live on X11 and Wayland, (2) fill the **Linux-specific seams** (device
permissions via udev, tray, overlay coverage), and (3) **package** (`.deb`/`.rpm`/AppImage/Flatpak/tarball).

## What the macOS port already bought us for Linux (the head start)

| Capability | Why it already works on Linux |
|---|---|
| **Compiles, no Windows blocker** | All P/Invoke is `IsWindows()`-guarded or seamed; `ubuntu-latest` CI builds it today. |
| **Full test suite** | Runs on the `ubuntu-latest` lane (OS-portable test paths landed in Phase 0.5). |
| **Windows-only surface hidden** | `AdvancedViewModel.RailTabAppliesToOs(isWindows)` + `HealthInputs.IsWindows` gate on *non-Windows*, so VMulti / Windows Ink / Driver-cleanup / Startup + their health nags are hidden on Linux exactly as on macOS. |
| **Display enumeration** | `DisplayEnumerator` dispatches non-Windows → `AvaloniaScreensDisplayEnumerator` (Avalonia `Screens`), which is cross-platform. Needs X11/Wayland verification (below). |
| **Daemon transport + lifecycle** | Named-pipe → Unix-domain-socket connects as-is; `DaemonExePaths` uses the extension-less exe name (non-Windows); the daemon version/source fallback (Phase 4) is process-list based, not Win32. |
| **Seam safety** | Every seam no-ops/degrades off-Windows (Phase 5). `PlatformShell.RevealInFileManager` already maps non-macOS → **`xdg-open`**, and `OpenDisplaySettings` no-ops on Linux. |
| **Avalonia backend** | `Program.cs` uses `.UsePlatformDetect()` → Avalonia auto-selects **X11 or Wayland**. |

**Net:** the portable core (connect, profiles, area mapping, dynamics, calibration, test) should light up on
Linux with little-to-no new app code — the same way it did on macOS once the seams existed.

## The Linux-specific surface (what's genuinely different)

### 1. Device permissions — udev (the Linux analog of macOS TCC)
The macOS blocker was TCC (Input Monitoring / Accessibility) tied to a signed binary's identity. **Linux's
equivalent is udev + group membership**, and it's arguably *simpler*:
- The daemon needs read/write to the tablet's **`/dev/hidraw*`** and to **`/dev/uinput`** (to emit cursor
  output), plus membership in the relevant group (`input`/`plugdev`, distro-dependent).
- OTD already solves this: **`generate-rules.sh`** emits udev rules from the tablet config database (one rule
  per supported tablet) so the device nodes are accessible without root. OTA can reuse OTD's rules.
- **Why it's easier than macOS:** a udev rule is a one-time system install keyed to vendor/product IDs — it
  does **not** re-break on every rebuild the way a macOS TCC grant does (which is tied to the binary's cdhash).
  So the "re-grant on every new build" pain that dominated the macOS port **does not exist on Linux**.

### 2. X11 vs Wayland — the one area of real complexity (Linux's "macOS-specific" thing)
This is the cross-cutting theme. **X11** exposes the classic APIs; **Wayland** deliberately restricts them for
security, so several OS-integration seams are unavailable or compositor-specific there:

| Seam | X11 | Wayland |
|---|---|---|
| Display geometry (`Screens`) | ✅ via Avalonia | ⚠️ limited multi-monitor geometry historically; verify |
| Calibration overlay full-screen coverage | ✅ borderless/fullscreen | ⚠️ compositor-controlled positioning; verify |
| Global hotkeys | ✅ `XGrabKey` | ❌ no standard API (compositor-specific) |
| Per-app profile switching (active window) | ✅ `_NET_ACTIVE_WINDOW` | ❌ no standard API |
| Tray / menu-bar item | ⚠️ DE-dependent (below) | ⚠️ DE-dependent |

Recommendation: **target X11 first** (verify the whole core there), then treat Wayland gaps as *graceful
"unavailable on Wayland" states* — exactly the Phase 5 pattern already in place. Global hotkeys / per-app
switching are already guarded no-ops off-Windows, so Wayland simply keeps them unavailable (with a clear state)
rather than crashing.

### 3. Calibration overlay coverage
`CalibrationOverlayWindow.PlaceOnDisplay` today: Windows → `WindowState.FullScreen`; **non-Windows → size to the
display bounds + `CoverFullDisplayOnMac()` (macOS-only ObjC)**. On Linux the mac path is a no-op, so it falls to
a borderless window sized to the display — which may or may not fully cover under a given WM/compositor
(panels, struts, fullscreen semantics). **This is the one place likely to need a small Linux-specific branch**
(e.g. `WindowState.FullScreen` on X11, or `_NET_WM_STATE_FULLSCREEN`). The **calibration report stays the
coordinate-space oracle** here, same as macOS.

### 4. Tray / status icon
Avalonia `TrayIcon` on Linux uses **StatusNotifierItem / AppIndicator (D-Bus)** — which is **DE-dependent**:
works on KDE and GNOME-with-AppIndicator-extension, but **vanilla GNOME hides legacy trays**. Needs a
"tray may be unavailable on your desktop" fallback (the app already keeps running when the window closes).

### 5. Packaging
OTD ships Linux via its `eng/bash/` types: **`Debian` (.deb), `RedHat` (.rpm), `BinaryTarBall`, `Generic`,
`Simple`** (runtimes `linux-x64` / `linux-musl-x64`), plus **udev rules** from `generate-rules.sh`, and a
`OpenTabletDriver.UX.Gtk` GUI head (OTA doesn't need a separate head — Avalonia is the GUI). For OTA the
realistic targets are **AppImage** and/or **Flatpak** (single-file / sandboxed, popular on Linux) and a plain
**tarball**; `.deb`/`.rpm` optional. **Flatpak caveat:** its sandbox complicates raw device (`/dev/hidraw`,
`uinput`) access — the daemon may need to run outside the sandbox or via portals; worth an early spike.

## A phased plan sketch (leaner than macOS — most of the core is shared)

Mirrors the macOS phases, but the shared cross-platform work is already merged, so Linux is mostly
**verification + Linux-only seams**. Same discipline: runtime guards, Windows-safe, verify-first on real
hardware (an X11 desktop first, then a Wayland session).

- **L0 — Boot + connect (X11).** Build on Linux (already green in CI); launch the GUI on an X11 desktop;
  confirm it connects to an OTD daemon, lists the tablet, gates the Windows-only surface, zero exceptions.
- **L1 — Device permissions.** Install OTD's udev rules (`generate-rules.sh`) + group membership; confirm the
  daemon reads the tablet and emits output (uinput). Document the one-time setup.
- **L2 — Display + calibration.** Verify `AvaloniaScreensDisplayEnumerator` multi-monitor geometry on X11;
  add the Linux overlay-coverage branch if the borderless window under-covers; run a real pen calibration
  (the report is the oracle).
- **L3 — Seams on Linux.** Tray (DE-dependent fallback), run-at-startup (**XDG autostart** `.desktop`),
  single-instance (file-lock / D-Bus). Global hotkeys + per-app switching: X11 backends optional, Wayland =
  graceful "unavailable".
- **L4 — Wayland pass.** Re-run L0–L2 under Wayland; confirm the guarded seams degrade cleanly; note gaps.
- **L5 — Packaging.** AppImage/Flatpak/tarball + udev-rule install; a Linux CI package lane. (Signing isn't a
  Gatekeeper-style gate on Linux, so there's no Phase-7-style paid blocker — the macOS split doesn't apply.)

## Open questions (to resolve, several shared with macOS Phase 6)

- **X11-first, Wayland-graceful — accept that as the V1 target?** (Full Wayland parity for hotkeys/per-app is a
  compositor problem, not ours.)
- **Daemon: same questions as macOS Phase 6** — one daemon or two; build-from-submodule vs consume OTD's Linux
  release; bundle vs rely on a distro-installed OTD; and OTA's plugin (`OpenTabletArtist.Dynamics`) only
  installs into the app-owned daemon, so an external distro OTD daemon lacks calibration/dynamics. **Note:** on
  Linux many users already `apt/dnf install opentabletdriver` — coexistence matters *more* here than on macOS.
- **Shared settings dir** — same `~/.config` (XDG) OTD path clobber risk as the macOS `~/Library` one.
- **Packaging format** — AppImage vs Flatpak vs distro packages; the Flatpak device-access spike.
- **Which DEs/compositors to officially support** — X11 (GNOME/KDE/XFCE) as tier 1; Wayland as best-effort.

## Verdict

**Feasibility is high and the cost is low — Linux is the cheap sequel to the macOS port.** The architecture is
already cross-platform, the app builds + tests on Linux CI today, the Windows-only surface is already gated off,
and the hardest macOS problem (permission re-grants tied to a signed binary) **doesn't exist on Linux** (udev is
a one-time, identity-independent install). The real work is **live verification on X11/Wayland**, a small
**overlay-coverage branch**, the **udev/tray** Linux seams, and **packaging** — no foundational risk, no paid
external gate. Recommend an X11-first investigation on a real Linux box + tablet to convert this paper
assessment into a proven one, exactly as #140 did for macOS.
