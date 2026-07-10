# Install OpenTabletArtist on Windows

OpenTabletArtist (OTA) is an alternative UX for OpenTabletDriver (OTD) on Windows app. 
It is designed for artists who want pressure sensitivity and tilt
from their drawing tablet. 

It bundles [OpenTabletDriver](https://opentabletdriver.net/) 
and wraps it in a friendlier, artist-optimized interface, so installing and configuring OTD is dramatically simpler.


## Before you start

- **Windows 11, 64-bit (x64).** 32-bit Windows and Windows on ARM are not supported.
- **A supported tablet.** Check the list at [opentabletdriver.net/Tablets](https://opentabletdriver.net/Tablets).
  If your tablet is marked **Zadig WinUSB**, it needs extra steps that this guide and OTA does **not** cover.

## Step 1 — Download and run

1. Go to the [latest release](https://github.com/TheSevenPens/OpenTabletArtist/releases/latest).
2. Under **Assets**, download `OpenTabletArtist-<version>-win-x64.zip`.
3. Right-click the zip → **Extract All**. Put the folder somewhere permanent (for example
   `C:\OpenTabletArtist`) — not inside your Downloads folder.
4. Open the folder and run **`OpenTabletArtist.exe`**.

> **Do not run OpenTabletArtist as Administrator.** Running elevated interferes with Windows Ink
> and per-app switching. Just double-click it normally — if you do launch it elevated, OTA flags a
> warning in Home's **Needs attention** list.

On launch, OTA starts the OTD daemon automatically and connects to it. The **Home** page shows a
**Daemon running** status once it's connected. If it says *Not connected*, click **Start**, then the
refresh icon.

## Step 2 — Remove conflicting tablet drivers

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

## Step 3 — Install the VMulti driver

Pressure and tilt on Windows require the **VMulti** virtual driver. 

1. If it isn't installed, the **Home** page will flag it.
2. Click **Install**. Approve the single UAC prompt.
3. When it offers to **restart Windows**, do it (recommended). VMulti isn't fully active until you
   restart.

After the restart, reopen OpenTabletArtist. The VMulti Driver page should show **Installed**.

## Step 4 — Let OTA detect your tablet

Plug in your tablet. OTA detects it automatically and it appears under **Tablets** in the sidebar
(with a status dot). Sometimes the detection happens very fast, but it can also take as long as about 7 seconds.

Once  detected, moving the pen should now move the mouse pointer. Don't worry if it moves the pointer on the wrong monitor.
We'll fix that in a moment.


## Step 5 — Map the tablet to a display

The first time OTA sees a tablet on a multi-monitor setup, it **auto-maps it to your primary display**
so the pointer doesn't span every monitor. If that's the display you want, you're done — otherwise pick
a different one:

1. Click your tablet under **Tablets**, then open the **Screen Mapping** tab. The diagram shows your
   monitors along the top and the tablet's active area below.
2. **Click the monitor** you want to draw on, then click **Apply mapping**.


## Step 6 — Test it with the built-in Scribble feature

Open the **Scribble** page and draw with the pen. You should see the stroke respond to pressure
(and tilt/twist if your pen supports them). The page also shows live readouts and, when Pen Dynamics
is on, exactly what's affecting the stroke.

You are almost done. 

## Step 6 — Configure your drawing app

Each drawing app has its own tablet/stylus setting. Turn on **Windows Ink** in your app's tablet
settings. Instructions vary per app; Krita is a good, free app to start with. 


## Optional OTA customization

All of these live under the tablet's tabs or the sidebar — configure them any time:

- **Pen Inputs / Pen Buttons** — the pen tip and eraser (Pen Inputs) and the pen's barrel buttons
  (Pen Buttons). They ship on *Adaptive Binding* (the only supported choice); a **Use Adaptive** button restores it if a switch has drifted onto something else.
- **Tablet Buttons** — map the tablet's hardware buttons (express keys) to keys, mouse buttons, or scroll.
- **Pen Dynamics** — a pressure-curve editor plus position/pressure smoothing, applied to every app.
- **Hotkeys** — global shortcuts to switch profiles or cycle the tablet's mapped monitor.
- **Profiles / Per-App Profiles** — save named configurations and switch them by hotkey or
  automatically per foreground application.
- **Theme** — appearance (System / Light / Dark / Sakura / Custom).

## Keeping OpenTabletArtist running

OpenTabletArtist must be running for your tablet to work — including hotkeys and per-app switching.

- **Closing the window minimizes it to the system tray**; the app keeps running. Reopen it from the
  tray icon. Use the tray's **Quit** to actually exit.
- To launch it automatically at sign-in, turn on **Start OpenTabletArtist when Windows starts** on the
  **Home** page. It starts minimized to the tray, so hotkeys and per-app switching are ready without
  opening it yourself.

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
