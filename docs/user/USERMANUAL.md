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

The app is made of **pages**, a page may be divided into **tabs**, and a tab may divide once more into **subtabs**:

- The **page menu** runs across the top of the window with six pages — **Home**, **Tablet**, **Pen**, **Scribble**, **Settings**, and **Advanced**.
- The **tab menu** sits just beneath it, in larger type, listing that page's tabs. Pages with nothing to divide (**Home**, **Scribble**) have no tab menu.
- **Subtabs** run down the left-hand side, as a vertical list rather than a menu across the top. Only one tab has them: **Settings → Developer**.

In both menus the current item is marked in the accent colour and a heavier weight — there's no underline. A selected subtab takes an accent bar down its left edge instead.

- **Tablet** and **Pen** carry the selected tablet's settings.
- A tablet **switcher** dropdown sits at the right of the page menu, alongside a **Refresh**. It appears on the **Tablet** and **Pen** pages and picks which tablet you're editing.
- **Settings** holds OpenTabletArtist's own preferences: **Presets**, **Hotkeys**, **Appearance** (theme), **System** (Startup + Shortcut + Driver Cleanup, Windows-only), and **Developer** *(debugging tools)*. 
- **Advanced** — advanced OTD-related pages (daemon controls, drivers, diagnostics, plugins). You'll rarely need these, though a few Home **Fix** buttons and the daemon controls live here.

Every paired or connected tablet also appears on **Home** under *Your tablets*. **Double-click a row** to open that tablet's settings, and use its **trash** icon to forget a remembered tablet (or reset a connected one to defaults).

### Per-page guides

- **[Home](HOME.md)** — the health-check landing page: *Needs attention*, *Your tablets*, and *About* with its links out.
- **[Tablet page](TABLET.md)** — about · mapping · calibration · buttons · wheels (plus the developer-only filters/json tabs).
- **[Pen page](PEN.md)** — basics · inputs · pressure (the pressure curve + smoothing).
- **[Scribble page](SCRIBBLE.md)** — the paint canvas for confirming the pen, with live readouts.
- **[Settings page](SETTINGS.md)** — Presets · Hotkeys · Appearance · System (Startup/Shortcut/Driver Cleanup) · Developer.
- **[Advanced page](ADVANCED.md)** — Daemon · Console · Drivers · Configs · Diagnostics · Plugins.
- **[Plugins](PLUGINS.md)** — what ships with OTA, what the Pen Dynamics plugin does, and whether you need any others.
- **[Getting help](HELP.md)** — where to ask when something isn't working, and what to include.


## System tray & background mode

**Closing the window minimizes it to the tray** rather than exiting — the app keeps running so its daemon controls stay one click away (the first time you close, a one-time hint explains this). From the tray you can:

- **Show OpenTabletArtist** — reopen the OTA window.
- **Switch Display** — Quickly reassign the current tablet to a different display
- **Start / Stop / Restart Daemon** — control the daemon directly (Start appears when it's stopped; Stop/Restart when it's running). The tray tooltip shows the current daemon status.
- **Quit** — fully exit the app (the OTD daemon, a separate process, keeps running).
- **Quit and stop the daemon** — exit the app *and* stop the OTD daemon (shown only while a daemon is running). Use this when you want nothing left running afterward.

## Stopping the daemon from outside this app

The OTD daemon is a separate process and keeps running after our app's window closes. Quick options for stopping it:

- **The app's own tray icon** (above) can also Stop/Restart the daemon directly.
- **Use a standalone OpenTabletDriver install**: if you have the official OpenTabletDriver installed alongside OTA, its own interface has a system tray icon with quit/show controls.
- **Use Task Manager**: `Ctrl+Shift+Esc`, find `OpenTabletDriver.Daemon.exe` in the Processes tab, right-click → End task.

## Troubleshooting

**See [Troubleshooting](TROUBLESHOOTING.md).**

## Getting help

If Troubleshooting doesn't cover it, **see [Getting help](HELP.md)** — where to ask, and what to include so the first reply is a useful one.
