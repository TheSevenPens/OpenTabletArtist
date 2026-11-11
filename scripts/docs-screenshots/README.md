# Documentation screenshots

Produces the page screenshots for the user guide (the `OpenTabletArtistDocs` repo, published through
GitBook) as crisp 1920x1080 PNGs, from the running app.

```powershell
.\capture-all.ps1 -OutDir $env:TEMP\ota-shots -DocsRepo C:\Users\<you>\Documents\GitHub\OpenTabletArtistDocs
```

`-DocsRepo` is optional: with it, each capture also replaces the docs asset named in
`docs-image-map.json`, so the pages need no edit. `-Only home,pen-pressure` captures a few.

## What a screenshot is

A **1280x720 DIP layout, resampled to 1920x1080**. Both halves are decisions.

- **Layout.** Every page is laid out at 1280x720 device-independent pixels, whatever the monitor. That
  matches the density of the earlier shots (1100x720 DIP) and keeps text a readable size. It is *not*
  "a window of 1920x1080 pixels": on a 100% monitor that would be a 1920x1080 layout, with smaller text
  and wider cards, and the pages would look different. Pin the layout, not the pixel count.
- **Pixels.** The window's client area is captured as real screen pixels (2880x1620 at 225%, 1920x1080
  at 150%) and resampled to 1920x1080 with Lanczos. Compared at 2x zoom against a native 1080p capture the
  result is as sharp, so no sharpening by default (`--sharpen` on `window_shot.py` exists if wanted).
- **Taskbar.** Only the window's client area is captured, and the window is kept inside the monitor's work
  area, so the taskbar can never appear. A 4K monitor is the easy case: 1620 px of window in a 2052 px
  work area.
- **Edges.** The 2 px border Windows draws inside the top of this window's client area is cropped off, and
  the four rounded corners are made transparent (the PNGs are RGBA). `--corner-radius 0` keeps them opaque.

## Why not the in-app "Screenshot all pages"

Settings -> Dev -> Screenshots has a sweep that renders each page to a PNG. Its output is soft, and it drops
the window chrome. Use real screen pixels instead. (The older `scripts/capture-ota-window.ps1` is a plain
window grab with no DPI handling or sizing; this folder supersedes it for the docs.)

## Before you run it

1. **Run the released build from a neutral folder**, e.g. `C:\OpenTabletArtist`, not from `bin\Debug` and not
   from a path with your user name in it: the Daemon tab prints the daemon's location. The released build
   has the bundled daemon and no "Development build" banner.
2. **Close any other OpenTabletArtist and its daemon.** The daemon is single-instance, and a second copy
   would attach to the first. The script refuses to run with more than one OTA process.
3. **Select the tablet you want shown** in the app's tablet switcher, with that tablet plugged in. The Tablet
   and Pen pages remember it. Keep the same tablet across a set so the pages agree.
4. **Look at what the window will show.** The state is part of the screenshot:
   - Home's health card and the Health page show whatever is wrong on this machine. A real finding is
     fine; a stale one is not.
   - Settings -> System shows "Startup is enabled but points at an old install path" when a Run-at-startup
     entry points at another build. Repair it first (toggle Start OpenTabletArtist when Windows starts on,
     then off).
   - Settings -> Presets and Advanced -> Console show `C:\Users\<name>\AppData\...` paths.
5. **Nothing else open on that monitor.** The window's backdrop is translucent and picks up whatever is
   behind it; a maximized app behind it tints the capture. The Claude desktop app has done this.
6. **Pillow** for the Python in use: `python -m pip install pillow`. With mise present, the script resolves
   the real python binary rather than the shim.

## How it works, and what bit us

- `window_shot.py` does everything pixel-exact in its own process, which declares per-monitor-v2 DPI
  awareness before touching a window. The PowerShell host does not give consistent coordinates for that
  (its window and UI coordinates came back scaled by 1.5 against the screen), so it is used only for
  navigation, where the same skew applies to both the element and the cursor and cancels out.
- **Navigation clicks the real mouse.** UI Automation's `SelectionItemPattern.Select()` ticks a radio button
  but does not run the app's navigation; the page stays put and the screenshot is of the previous one.
- **A missing tab fails the run** with the list of names that are present, rather than capturing the wrong
  page under a new name. Tab names are matched exactly; the Health tab by prefix, since its name carries the
  finding count.
- The app starts **minimized** when launched from a script; `size` restores it first.
- `size` puts the window 40 px in from the top-left of the work area of the monitor it is on. If the window cannot fit there (a 1080p monitor with a visible taskbar is 1920x1032 of work area for a 1920x1082 window) it stops with an error instead of capturing a cropped page: use a bigger monitor, or hide the taskbar on that one.
- If you move to a **different monitor scale**, nothing changes in the recipe: the client size is
  `layout x scale`, and the downsample factor follows. At 150% on a 1080p monitor the capture is already
  1920x1080 and is not resampled at all.

## Pages and the docs repo

`docs-image-map.json` maps each capture to the file it replaces in `.gitbook/assets`. A new page needs its
own entry, a target in `capture-all.ps1`, and an image reference in the page. Several pages in the docs are
only a screenshot and a caption, so a retake alone brings them up to date; the prose pages
(`home.md`, `settings/presets-tab.md`, ...) need reading against the new shots.
