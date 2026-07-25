# User Manual

This manual documents the interface in depth. For the full first-time setup walkthrough see the
[Windows install guide](INSTALL.md); to build from source see [BUILDING.md](../dev/BUILDING.md).

Each top-level page has its own guide — jump straight to one under [Per-page guides](#per-page-guides) below.

## Quick Start

OpenTabletArtist ships a **self-contained Windows build** — you don't need to install .NET or
OpenTabletDriver separately. You need **Windows 11 (64-bit)** and a
[supported tablet](https://opentabletdriver.net/Tablets).

1. **Download.** Grab `OpenTabletArtist-<version>-win-x64.zip` from the
   [latest release](https://github.com/TheSevenPens/OpenTabletArtist/releases/latest) (under **Assets**).
2. **Extract.** Right-click the zip → **Extract All**, into a permanent folder (for example
   `C:\OpenTabletArtist`) — not inside your Downloads folder.
3. **Run.** Open the folder and double-click **`OpenTabletArtist.exe`**. *Don't* run it as Administrator.
   It auto-starts the bundled OpenTabletDriver daemon and connects — **Home** shows **Daemon running**.
4. **Attach your tablet.** Plug it in; OTA detects it within a few seconds and moving the pen moves the
   pointer. (On a multi-monitor setup it auto-maps to your primary display; change it on the tablet's
   **mapping** tab.)
5. **Test it — Scribble.** Open the **Scribble** page and draw. You should see the stroke respond to
   **pressure** (and tilt/twist if your pen supports them), with live readouts — confirming the pen works.

### If the pen isn't working, or you want pressure & tilt in your apps

A little one-time Windows setup is needed, and OTA walks you through each item from **Home → Needs
attention** with a one-click **Fix**:

- **Remove manufacturer tablet drivers** (Wacom, Huion, XP-Pen, …) — they conflict with OpenTabletDriver.
  OTA flags them and offers the OTD team's cleanup tool on the **Driver Cleanup** page.
- **Install the VMulti driver** — required on Windows for pressure and tilt. OTA flags it and installs it
  in one click (a Windows restart finishes it).
- **Turn on Windows Ink** in your drawing app's tablet/stylus settings (Krita is a good free app to start
  with).

The [install guide](INSTALL.md) has the complete step-by-step version of all of this.

## Using the Interface

A **top navigation bar** runs across the top of the window with six pages — **Home**, **Tablet**, **Pen**, **Scribble**, **Settings**, and **Advanced** — and the active page is underlined in the accent colour. Most pages have their own **pivot row** of tabs just beneath the bar.

- **Tablet** and **Pen** carry the selected tablet's settings. A tablet **switcher** dropdown at the top-right picks which tablet you're editing (shown when more than one is connected) — the **Tablet**, **Pen**, and **Scribble** switchers are linked, so they always show the same tablet. The **Tablet** page's tabs are **about · mapping · calibration · buttons · wheels**; the **Pen** page's tabs are **movement · inputs · pressure**.
- **Settings** holds OpenTabletArtist's own preferences: **Presets**, **Hotkeys**, **Appearance** (theme), **System** (Startup + Shortcut + Driver Cleanup, Windows-only), and **Developer** *(debugging tools)*. (**Per-App Presets** is hidden while the feature is disabled.)
- **Advanced** hosts OpenTabletDriver's pages: **Daemon** (connection status, version, and start/restart controls), **Console** (the daemon log), **Drivers** (Windows Ink Plugin + VMulti — Windows-only), **Configs** (custom tablet compatibility), **Diagnostics**, and **Plugins**.

Every paired or connected tablet also appears on **Home** under *Your tablets*, each with a **Settings** button (opens the Tablet page for it) and a **Forget** button.

### Per-page guides

Each top-level page is documented in its own guide:

- **[Home](HOME.md)** — the health-check landing page, *Your tablets*, supported-tablets list, and the About/Help cards.
- **[Tablet page](TABLET.md)** — about · mapping · calibration · buttons · wheels (plus the hidden hover/filters/json tabs).
- **[Pen page](PEN.md)** — movement · inputs · pressure (the pressure curve + smoothing).
- **[Scribble page](SCRIBBLE.md)** — the paint canvas for confirming the pen, with live readouts.
- **[Settings page](SETTINGS.md)** — Presets · Hotkeys · Appearance · System (Startup/Shortcut/Driver Cleanup) · Developer.
- **[Advanced page](ADVANCED.md)** — Daemon · Console · Drivers · Configs · Diagnostics · Plugins.

## Navigation

Click a page in the top navigation bar to switch pages; the active page is underlined in the accent colour. Pages with sub-sections (Tablet, Pen, Settings, Advanced) show a pivot row of tabs beneath the bar.

## System tray & background mode

The app is **single-instance**: launching it again while it's already running (including when it's minimized to the tray) doesn't open a second window or tray icon — it just brings the existing window to the front.

The app runs with a **system tray icon**. **Closing the window minimizes it to the tray** rather than exiting — the app keeps running so its daemon controls stay one click away (the first time you close, a one-time hint explains this). From the tray you can:

- **Click the icon** — reopen the window.
- **Show OpenTabletArtist** — reopen the window.
- **Pen dynamics status** — a read-only line revealing whether the bundled Pen Dynamics filter is affecting the active tablet's pen: *off*, *on (behaves linear)*, or *Affecting your pen: Pressure curve, Pressure smoothing, Position smoothing* (only the parts actually in effect). Mirrors the Scribble page's indicator so the effect is never a mystery with the window closed. Shown only when a tablet is connected.
- **Open Tablet Settings…** — reopens the window and shows the active tablet's settings. Shown when a tablet is connected. (The tray also offers a focused **Pen Dynamics** editor.)
- **Switch Display** — a submenu listing your monitors; pick one to map the active tablet to that whole display (aspect-locked, the same mapping as clicking a display on the Tablet page's **mapping** tab). The currently-mapped display is check-marked. Shown only when the active tablet is in an Absolute output mode (otherwise there's no display area to set).
- **Active Tablet** — when more than one tablet is connected, a submenu to choose which tablet the tray actions (and the Scribble / Diagnostics pages) act on. With a single tablet it's hidden and that tablet is used automatically.
- **Start / Stop / Restart Daemon** — control the daemon directly (Start appears when it's stopped; Stop/Restart when it's running). The tray tooltip shows the current daemon status.
- **Quit** — fully exit the app (the OTD daemon, a separate process, keeps running).

## Stopping the daemon from outside this app

The OTD daemon is a separate process and keeps running after our app's window closes. Quick options for stopping it:

- **Use the OTD UX**: Click **OTD UX** on the **Daemon** tab (Advanced → Daemon) to launch `OpenTabletDriver.UX.Wpf.exe`, which has its own system tray icon with quit/show controls.
- **Use Task Manager**: `Ctrl+Shift+Esc`, find `OpenTabletDriver.Daemon.exe` in the Processes tab, right-click → End task.

The app's own tray icon (above) can also Stop/Restart the daemon directly.

## Troubleshooting

Common issues and fixes — a "Not connected to daemon" card, or a tablet that isn't detected. **See [Troubleshooting](TROUBLESHOOTING.md).**
