# Advanced page

*(Part of the [User Manual](USERMANUAL.md).)*

**Advanced** hosts OpenTabletDriver's own pages, as a pivot row of tabs: **Daemon** (connection status, version, and start/restart controls), **Console** (the daemon log), **Drivers** (Windows Ink Plugin + VMulti — Windows-only), **Configs** (custom tablet compatibility), **Diagnostics**, and **Plugins**.

## Daemon

The full daemon status and controls (this moved off Home, which now shows the daemon only when there's a problem). The information is laid out as consistent *property: value* lists across two cards:

- **Daemon Connection** — whether OTA is **Connected**, and (when connected) the time it connected and how long it's been up. A **Refresh** checks the status.
- **Daemon Process** — whether the daemon is **Running**, its **Source** (either "Bundled (ships with OTA)" or "External (not started by OTA)"), its **Version**, whether that version matches the build OTA ships (**Build match**), and its **Uptime**. This card carries **Start** when disconnected and **Restart** / **Stop** when running. The **Start / Stop / Restart** actions show an inline progress bar with live phase text (Stopping… → Starting… → Connecting…) while they run, and report a clear error if the daemon doesn't come online within 30 seconds.

The **Source** row tells you which daemon the app is actually connected to:

- **Bundled (ships with OTA)** — connected to this project's build under `external/OpenTabletDriver/OpenTabletDriver.Daemon/bin/`.
- **External (not started by OTA)** — connected to a daemon OTA didn't start, e.g. an officially-installed OpenTabletDriver you already had running. This is a **supported** setup: OTA connects to whichever daemon is running and only starts its own bundled copy when none is. It's also presented in its own **External Daemon** card (right column) showing the daemon's path + version, with a **Use bundled daemon instead** button (Restart) if you didn't intend it.

Ownership is detected by resolving the process on the other end of the named pipe (`GetNamedPipeServerProcessId`) and comparing its exe path to the project's daemon build. The right column also shows a **Bundled Daemon** card (embedded OTD version + path). A development build additionally shows an **OTD UX** card whose **Launch OTD UI** button opens the original OpenTabletDriver interface — for comparison, or for settings OTA doesn't surface; it's hidden in the released app, which ships the daemon but not that interface.

## Console

The live OpenTabletDriver daemon log, streamed with per-level coloring and a **minimum-level** filter. **Copy** is a dropdown — copy the visible log as **text**, a **Markdown** table, or an **HTML** table. **Clear** empties the view.

## Drivers  *(Windows-only)*

The **Drivers** tab stacks two cards: the **Windows Ink Plugin** on top and the **VMulti Driver** below.

### Windows Ink Plugin

Manages the third-party Windows Ink output-mode plugin (from Kuuube's VoiDPlugins), which delivers pen pressure and tilt to your apps. Shows:

- **Install status** — the plugin version folded into the text (e.g. "v0.5.2 installed"), or "Not installed."
- **Output mode** — whether the tablet actually uses a Windows Ink mode ("Plugin active" / "Not configured").
- **Supported driver vs OTD** — the plugin's declared supported driver version alongside the running OTD version. A warning indicator appears if the installed plugin doesn't declare support for the current OTD version (per OTD's own compatibility rule).
- **Buttons** — **Install** (when not installed); **Check for Update** (when installed) which queries the official OTD Plugin-Repository — if a newer plugin version is found the button becomes **Install Update (vX)**, otherwise it reports "Up to date"; **Uninstall**; and a **Refresh** icon (top-right) that re-reads the installed plugin and re-checks the repository in one step. Install/update/uninstall are driven through the daemon's plugin RPC; the card updates its status as soon as each operation completes.

### VMulti Driver

VMulti is the virtual pen device the Windows Ink plugin injects pressure and tilt through. Detection runs via both Setup API and HID enumeration. Has **Install** / **Uninstall** wizards, **Refresh** to re-check, and **Browse** to open the driver folder. Both **Install** and **Uninstall** run in-app (one UAC prompt each, no flashing cmd window) and offer to **restart** Windows afterward. Install creates the VMulti device via `devcon`; Uninstall removes the driver and the active device *and* cleans up the leftover driverless `djpnewton\vmulti` nodes (Device Manager Code 28) that the stock removal left behind. Detection reflects a *working* driver, so any remaining driverless leftovers are reported as **Not installed**, not as installed.

## Configs

Manages OpenTabletDriver's tablet **configuration** files — the per-tablet JSON definitions that let the daemon recognise and drive a tablet. Two things live here (see `docs/design/tablet-configs.md` for the full model):

- **Your config folder** — lists the loose config JSONs in the daemon's actual configurations folder (queried from the running daemon, so it's the folder OTD really reads — on Windows the portable `userdata\Configurations` or `%LOCALAPPDATA%\OpenTabletDriver\Configurations`). Each row shows the tablet's friendly name (from the JSON `Name`, falling back to a manufacturer-folder + filename combo). Per-row **View** opens the formatted JSON read-only; **Delete** removes the file after a confirmation. The header has **Refresh** and **Open Folder**.
- **Add tablet support** — **Check for more configs** fetches OpenTabletDriver's approved tablet configs for the bundled driver version and lists any your install doesn't already have (useful for a newly-supported tablet). **Install** downloads one into your config folder; reconnect the tablet (or restart the daemon) to use it.

> A tablet driven by a config file that **replaces** one of OpenTabletDriver's built-in, vetted configs (same name) raises a gentle *Needs attention* recommendation on Home — deliberate overrides are fine, but it's worth knowing you're off the vetted default if the pen behaves oddly. Its **Review** button opens this page.

## Diagnostics

Live tablet input visualization. See [DIAGNOSTICS.md](../dev/DIAGNOSTICS.md) for details. When more than one tablet is connected, a **Show** selector picks which tablet's live reports to display (the daemon's debug stream carries all tablets at once); with a single tablet it's hidden.

## Plugins

A read-only list of the OpenTabletDriver plugins installed in the daemon's plugin folder. Each row shows the plugin's name, version (when available), and whether it's **Active** (referenced by an enabled output mode or filter on a tablet) or just **Installed**. The OpenTabletArtist – Pen Dynamics plugin appears here once it's installed. Use the refresh icon to rescan, or **Browse** to open the plugin folder in File Explorer. (This view is informational — it has no install or remove buttons.)

Note that a plugin can be installed here and still not run: when OTA is using the bundled daemon, it keeps only its own **filters** enabled and switches off any others. See [Tablet → filters](TABLET.md#filters).

For what OTA ships and installs on its own, whether you need anything else, and how to add a plugin by hand, see the **[Plugins guide](PLUGINS.md)**.
