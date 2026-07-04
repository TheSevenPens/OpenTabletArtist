# Install OpenTabletArtist on Windows

OpenTabletArtist (OTA) is a Windows app for artists who want pressure sensitivity and tilt
from their drawing tablet. It bundles [OpenTabletDriver](https://opentabletdriver.net/) (OTD)
and wraps it in a friendlier interface, so most of the manual setup that a raw OTD install
requires is handled for you — usually one click, one UAC prompt.

> This is the OpenTabletArtist install guide. For the underlying driver, OTA uses OpenTabletDriver;
> you do **not** install OTD separately — it ships inside OpenTabletArtist.

## Before you start

- **Windows 10 or 11, 64-bit (x64).** 32-bit Windows and Windows on ARM are not supported.
- **A supported tablet.** Check the list at [opentabletdriver.net/Tablets](https://opentabletdriver.net/Tablets).
  If your tablet is marked **Zadig WinUSB**, it needs extra steps that this guide does **not** cover.
- **An internet connection during setup.** The VMulti driver, the Windows Ink plugin, and the driver
  cleanup tool are downloaded the first time you install them from inside the app.

You do **not** need to install the .NET runtime or OpenTabletDriver — the download is self-contained
and includes everything.

## Step 1 — Download and run

1. Go to the [latest release](https://github.com/TheSevenPens/OpenTabletArtist/releases/latest).
2. Under **Assets**, download `OpenTabletArtist-<version>-win-x64.zip`.
3. Right-click the zip → **Extract All**. Put the folder somewhere permanent (for example
   `C:\OpenTabletArtist`) — not inside your Downloads folder.
4. Open the folder and run **`OpenTabletArtist.exe`**.

> **Do not run OpenTabletArtist as Administrator.** Running elevated interferes with Windows Ink
> and per-app switching. Just double-click it normally.

On launch, OTA starts its bundled driver automatically and connects to it. The **Home** page shows a
**Daemon running** status once it's connected. If it says *Not connected*, click **Start**, then the
refresh icon.

## Step 2 — Let OTA detect your tablet

Plug in your tablet. OTA detects it automatically and it appears under **Tablets** in the sidebar
(with a status dot). Moving the pen should now move the mouse pointer.

- Don't worry yet about *which* monitor the pointer is on, or that pressure isn't working — both are
  set up in the steps below.
- If nothing is detected, wait a few seconds (it polls every ~3 s) or click the refresh icon. If it
  still doesn't appear, a conflicting manufacturer driver is the most likely cause — see Step 3.

## Step 3 — Remove conflicting tablet drivers

Manufacturer drivers (Wacom, Huion, XP-Pen, Gaomon, Veikk, …) interfere with OpenTabletDriver and
**must** be removed.

- OTA detects them automatically: a **Conflicting tablet driver** warning appears on **Home**, and the
  details are on the **Driver Cleanup** page (under **Advanced**).
- On the **Driver Cleanup** page, click **Install** to fetch the OTD team's *TabletDriverCleanup* tool
  (no admin needed to install it), then **Run** it (one UAC prompt; a terminal window shows its
  progress). It removes leftover driver bits that a normal uninstall leaves behind.
- You can also uninstall manufacturer drivers yourself from Windows **Settings → Apps** and Device
  Manager first; the cleanup tool is for the leftovers.

Restart if the cleanup tool asks you to, then reopen OpenTabletArtist.

## Step 4 — Install the VMulti driver

Pressure and tilt on Windows require the **VMulti** virtual driver.

1. Go to the **VMulti Driver** page (under **Advanced**). If it isn't installed, **Home** also flags it.
2. Click **Install**. Approve the single UAC prompt.
3. When it offers to **restart Windows**, do it (recommended). VMulti isn't fully active until you
   restart.

After the restart, reopen OpenTabletArtist. The VMulti Driver page should show **Installed**.

## Step 5 — Install the Windows Ink plugin

Windows Ink is how pressure and tilt reach your drawing apps on Windows.

1. Go to the **Windows Ink Plugin** page (under **Advanced**). **Home** flags it too if it's missing.
2. Click **Install**. OTA downloads and installs the plugin for you.

## Step 6 — Turn on Windows Ink for your tablet

Installing the plugin doesn't switch your tablet to it — you enable it per tablet:

1. Click your tablet under **Tablets** in the sidebar.
2. On the **Screen Mapping** tab, the output mode is an **Absolute / Relative** toggle. Keep
   **Absolute** (it carries pressure and tilt). If a warning says the tablet isn't on a Windows Ink
   mode, click its **Fix** button to switch it.

> Absolute mode is what you want for drawing — it maps the tablet to a fixed area of the screen and
> carries pressure and tilt. Relative mode behaves like a mouse and has no pressure.

## Step 7 — Map the tablet to a display

By default the tablet isn't mapped to a specific monitor. Set it once:

1. On the tablet's **Screen Mapping** tab, the diagram shows your monitors along the top and the
   tablet's active area below.
2. **Click the monitor** you want to draw on, then click **Apply mapping**.

OTA maps the tablet to that whole display **aspect-locked**, so a circle on the tablet draws a circle
on screen with no stretching. A live pen dot tracks over the tablet area so you can confirm it.

> Unlike raw OpenTabletDriver, there's no separate **Apply**/**Save** step to remember — OTA applies
> your changes to the driver automatically. (Named **Profiles** are a separate feature for saving and
> switching whole configurations.)

## Step 8 — Configure your drawing app

Each drawing app has its own tablet/stylus setting. Turn on **Windows Ink** in your app's tablet
settings. Instructions vary per app; Krita is a good, free app to start with.

## Step 9 — Test it

Open the **Test Drawing** page and draw with the pen. You should see the stroke respond to pressure
(and tilt/twist if your pen supports them). The page also shows live readouts and, when Pen Dynamics
is on, exactly what's affecting the stroke.

At this point you're set up: the pointer tracks on one display with no distortion, and pressure and
tilt work in your drawing app.

## Optional customization

All of these live under the tablet's tabs or the sidebar — configure them any time:

- **Pen Switches** — the pen tip, eraser, and barrel buttons. They ship on *Adaptive Binding*
  (recommended); a **Fix** button restores it if needed.
- **ExpressKeys** — map the tablet's hardware buttons to keys, mouse buttons, or scroll.
- **Dynamics** — a pressure-curve editor plus position/pressure smoothing, applied to every app.
- **Hotkeys** — global shortcuts to switch profiles or cycle the tablet's mapped monitor.
- **Profiles / Per-App Profiles** — save named configurations and switch them by hotkey or
  automatically per foreground application.
- **Theme** — appearance (Light / Dark / Sakura / Custom).

## Keeping OpenTabletArtist running

OpenTabletArtist must be running for your tablet to work — including hotkeys and per-app switching.

- **Closing the window minimizes it to the system tray**; the app keeps running. Reopen it from the
  tray icon. Use the tray's **Quit** to actually exit.
- There is currently **no built-in "start at Windows startup" option.** To launch it automatically,
  create a shortcut to `OpenTabletArtist.exe`, press <kbd>Win</kbd>+<kbd>R</kbd>, type
  `shell:startup`, and drop the shortcut into that folder.

## Where your settings live

- The driver's settings and configurations live in `%LocalAppData%\OpenTabletDriver`.
- OpenTabletArtist's own settings (theme, per-app maps, hotkeys) live in
  `%LocalAppData%\OpenTabletArtist`.

## Uninstalling

1. In OpenTabletArtist, open the **VMulti Driver** page and click **Uninstall** (one UAC prompt; it
   also cleans up leftover VMulti device nodes). Restart if prompted.
2. Quit OpenTabletArtist from the tray.
3. Delete the OpenTabletArtist folder you extracted in Step 1.
4. Optionally delete `%LocalAppData%\OpenTabletDriver` and `%LocalAppData%\OpenTabletArtist` to remove
   settings.
