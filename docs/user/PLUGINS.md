# Plugins

*(Part of the [User Manual](USERMANUAL.md).)*

Plugins are an **OpenTabletDriver** feature, not an OpenTabletArtist one: they're small add-ons that load inside the driver's background process and change how your pen is handled. OpenTabletArtist ships the ones you need and installs them for you, so for most people this page is background reading rather than something to act on.

**Advanced → Plugins** lists what's currently installed — see [Advanced page](ADVANCED.md#plugins).

## What OpenTabletArtist installs for you

Two plugins are included with the app and installed automatically. You don't need to download or configure either one.

### OpenTabletArtist – Pen Dynamics

OTA's own plugin, and the one behind most of the pen features. It's copied into the driver's plugin folder the first time the app connects, and refreshed whenever you update OTA. A first-time install loads immediately; an update restarts the driver, because a plugin already in memory can't be swapped in place.

### Windows Ink

The third-party Windows Ink output-mode plugin from Kuuube's [VoiDPlugins](https://github.com/Kuuuube/VoiDPlugins) — the de-facto standard for OpenTabletDriver on Windows. It's what actually delivers **pressure and tilt** to drawing apps. OTA installs it for you: first trying the newest compatible release from OpenTabletDriver's official Plugin-Repository, then falling back to the copy bundled inside OTA if you're offline. You can check, update, or remove it from **Advanced → [Plugins](ADVANCED.md#windows-ink-plugin)**.

Both are installed only when OTA started the driver itself. If you're connected to an **External (not started by OTA)** daemon, OTA leaves plugins alone.

## What's inside the Pen Dynamics plugin

One file, three filters — all running inside the driver's pipeline, which is why they apply to **every** app you draw in rather than one at a time:

- **Pen Dynamics** — the pressure curve (softness, input/output minimum and maximum, an optional dead zone below the minimum) plus EMA **pressure smoothing** and **position smoothing**, with a choice of smoothing before or after the curve. Edited from **Pen → pressure**.
- **Calibration** — applies the pen-display calibration you record on **Tablet → calibration**, correcting the offset between where the pen is and where the pointer lands. It runs before Pen Dynamics.
- **Hover Limit** — drops pen reports past a set hover height, so a lifted pen stops dragging the pointer. **Currently inactive**: it's switched off pending further validation, and its tab is hidden. It's present in the file but does nothing.

You never install, enable, or configure these directly — OTA writes their settings for you as you use the interface.

## Do you need any other plugins?

**Almost certainly not.** The usual reasons people reach for a third-party OpenTabletDriver plugin are already handled:

| If you want… | You already have it |
|---|---|
| A pressure curve, pressure smoothing, or position smoothing | **Pen Dynamics** — **Slimy Scylla is not needed** |
| Pressure and tilt in your drawing app | **Windows Ink**, installed automatically |
| Better accuracy on a pen display | Built-in **calibration** |
| Support for a tablet that isn't recognized | That's a tablet *config*, not a plugin — see **Advanced → [Configs](ADVANCED.md#configs)** |

There's also a limit worth knowing before you install anything: while OTA is running its own driver, it keeps **only its own filters enabled** and switches off any others. So a third-party plugin that provides a *filter* will be installed but inert. Output modes, tools, and bindings aren't affected. See [Tablet → filters](TABLET.md#filters).

## If you really do need one

First check that the plugin is built for the driver OTA ships. A plugin compiled against a different version can fail to load:

- **.NET 8** (`net8.0`) — the framework the bundled driver runs on.
- **OpenTabletDriver 0.6.7** — the version OTA bundles. Plugins compile against OpenTabletDriver's own plugin library, so a build made for a different release may not load. Plugin releases normally state the driver version they support; look for 0.6.7.

Then install it by hand:

1. **Advanced → Plugins → Browse** — opens the driver's plugin folder (usually `%LOCALAPPDATA%\OpenTabletDriver\Plugins`).
2. Create a folder named after the plugin and put its `.dll` file(s) inside, along with `metadata.json` if the download includes one.
3. **Advanced → Daemon → Restart** — the driver only picks up plugins as it starts.
4. Back on **Advanced → Plugins**, hit refresh. The plugin should now be listed, showing **Installed**; it becomes **Active** once something actually references it.

To remove one, delete its folder and restart the driver again.

*(If you also have a full OpenTabletDriver installation with its own interface, its plugin manager works too. The folder method above works either way.)*
