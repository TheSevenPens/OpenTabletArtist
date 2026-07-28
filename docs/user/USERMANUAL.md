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

## Using the Interface

A **top navigation bar** runs across the top of the window with six pages — **Home**, **Tablet**, **Pen**, **Scribble**, **Settings**, and **Advanced** — and the active page is underlined in the accent colour. Most pages have their own **pivot row** of tabs just beneath the bar.

- **Tablet** and **Pen** carry the selected tablet's settings.
- A tablet **switcher** dropdown at the top-right picks which tablet you're editing (shown when more than one is connected)
- **Settings** holds OpenTabletArtist's own preferences: **Presets**, **Hotkeys**, **Appearance** (theme), **System** (Startup + Shortcut + Driver Cleanup, Windows-only), and **Developer** *(debugging tools)*. 
- **Advanced** - this is really advanced stuff related to OTD. You should never need to go here. 

Every paired or connected tablet also appears on **Home** under *Your tablets*, each with a **Settings** button (opens the Tablet page for it) and a **Forget** button.

### Per-page guides

- **[Home](HOME.md)** — the health-check landing page, *Your tablets*, supported-tablets list, and the About/Help cards.
- **[Tablet page](TABLET.md)** — about · mapping · calibration · buttons · wheels (plus the hidden hover/filters/json tabs).
- **[Pen page](PEN.md)** — movement · inputs · pressure (the pressure curve + smoothing).
- **[Scribble page](SCRIBBLE.md)** — the paint canvas for confirming the pen, with live readouts.
- **[Settings page](SETTINGS.md)** — Presets · Hotkeys · Appearance · System (Startup/Shortcut/Driver Cleanup) · Developer.
- **[Advanced page](ADVANCED.md)** — Daemon · Console · Drivers · Configs · Diagnostics · Plugins.


## System tray & background mode

**Closing the window minimizes it to the tray** rather than exiting — the app keeps running so its daemon controls stay one click away (the first time you close, a one-time hint explains this). From the tray you can:

- **Show OpenTabletArtist** — reopen the OTA window.
- **Switch Display** — Quckly reassign the current tablet to a different display
- **Start / Stop / Restart Daemon** — control the daemon directly (Start appears when it's stopped; Stop/Restart when it's running). The tray tooltip shows the current daemon status.
- **Quit** — fully exit the app (the OTD daemon, a separate process, keeps running).
- **Quit and stop the daemon** — exit the app *and* stop the OTD daemon (shown only while a daemon is running). Use this when you want nothing left running afterward.

## Stopping the daemon from outside this app

The OTD daemon is a separate process and keeps running after our app's window closes. Quick options for stopping it:

- **Use The app's own tray icon** (above) can also Stop/Restart the daemon directly.
- **Use the OTD UX**: Click **OTD UX** on the **Daemon** tab (Advanced → Daemon) to launch `OpenTabletDriver.UX.Wpf.exe`, which has its own system tray icon with quit/show controls.
- **Use Task Manager**: `Ctrl+Shift+Esc`, find `OpenTabletDriver.Daemon.exe` in the Processes tab, right-click → End task.

## Troubleshooting

**See [Troubleshooting](TROUBLESHOOTING.md).**
