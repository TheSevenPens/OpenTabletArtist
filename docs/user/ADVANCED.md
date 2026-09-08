# Advanced page

*(Part of the [User Manual](USERMANUAL.md).)*

**Advanced** hosts OpenTabletDriver's own controls, divided into tabs: **Daemon** (connection status, version, and start/restart controls), **Console** (the daemon log), **Configs** (custom tablet compatibility), **Diagnostics**, and **Plugins**. *(The **VMulti** driver moved to [Settings → Drivers](SETTINGS.md#drivers), beside driver cleanup.)*

## Daemon

The full daemon status and controls (this moved off Home, which now shows the daemon only when there's a problem).

The page leads with a **diagram of the connection**, read top to bottom: OpenTabletArtist, the pipe running down beside its own facts, then the OpenTabletDriver daemon process. That is the whole subject of this page — OTA does not contain the daemon, it attaches to a separate process — and the layout now says so before the facts do.

- **This app** — OTA's own version and the OpenTabletDriver version it **bundles**.
- **The wire** — running down between the two, labelled **connected** or **not connected**, with the time it connected (**Since**) and how long it has been up beside it. While a Start / Stop / Restart or the initial connect is in flight, the wire itself becomes a progress bar with live phase text (Stopping… → Starting… → Connecting…), and reports a clear error if the daemon doesn't come online within 30 seconds.
- **Daemon process** — the running daemon and its **Version**, with its **Source** (either "Bundled (ships with OTA)" or "External (not started by OTA)") and **Uptime** underneath. When no daemon is running this box is drawn empty and greyed and the wire breaks, which is what "not running" looks like.

Because the two versions now sit at either end of the same wire, a **Build match** line only appears when they *disagree* — agreement says itself.

The daemon box carries a **⋯** menu — the actions all act on that process — holding **Refresh status**, and **Start** when it isn't running or **Restart** / **Stop** when it is. It stays there when the box is greyed out, which is exactly when you need **Start**.

When the daemon is **External (not started by OTA)**, **Stop** and **Restart** ask first — naming the executable that's running, and what happens next. Stopping affects anything else using that daemon, and starting one again from OpenTabletArtist launches its own bundled daemon rather than the one you stopped; Restart does both in a single step. Stop always stops just the daemon OpenTabletArtist is connected to, never other OpenTabletDriver processes that happen to be running, and a systemd-managed daemon is stopped through `systemctl` rather than killed.

The **Source** row tells you which daemon the app is actually connected to:

- **Bundled (ships with OTA)** — connected to this project's build under `external/OpenTabletDriver/OpenTabletDriver.Daemon/bin/`.
- **External (not started by OTA)** — connected to a daemon OTA didn't start, e.g. an officially-installed OpenTabletDriver you already had running. This is a **supported** setup: OTA connects to whichever daemon is running and only starts its own bundled copy when none is. The diagram says **External** in the daemon box, and an **External Daemon** card below carries the explanation, the daemon's path, and a **Use bundled daemon instead** button (Restart) if you didn't intend it.

Ownership is detected by resolving the process on the other end of the named pipe (`GetNamedPipeServerProcessId`) and comparing its exe path to the project's daemon build. (The embedded OTD version and its folder used to sit in a **Bundled Daemon** card of their own; they are under the *this app* end of the diagram now, where they can be read against the running daemon's version opposite.) A development build additionally shows an **OTD UX** card whose **Launch OTD UI** button opens the original OpenTabletDriver interface — for comparison, or for settings OTA doesn't surface; it's hidden in the released app, which ships the daemon but not that interface.

## Console

The live OpenTabletDriver daemon log, streamed with per-level coloring and a **minimum-level** filter. **Copy** is a dropdown — copy the visible log as **text**, a **Markdown** table, or an **HTML** table. **Clear** empties the view.

## Configs

Manages OpenTabletDriver's tablet **configuration** files — the per-tablet JSON definitions that let the daemon recognise and drive a tablet. Two things live here (see `docs/design/tablet-configs.md` for the full model):

- **Your config folder** — lists the loose config JSONs in the daemon's actual configurations folder (queried from the running daemon, so it's the folder OTD really reads — on Windows the portable `userdata\Configurations` or `%LOCALAPPDATA%\OpenTabletDriver\Configurations`). Each row shows the tablet's friendly name (from the JSON `Name`, falling back to a manufacturer-folder + filename combo). Per-row **View** opens the formatted JSON read-only; **Delete** removes the file after a confirmation. The header's **⋯** menu holds **Reload list** and **Open configurations folder**.
- **Add tablet support** — **Check for more configs** fetches OpenTabletDriver's approved tablet configs for the bundled driver version and lists any your install doesn't already have (useful for a newly-supported tablet). **Install** downloads one into your config folder; reconnect the tablet (or restart the daemon) to use it.

> A tablet driven by a config file that **replaces** one of OpenTabletDriver's built-in, vetted configs (same name) raises a gentle *Needs attention* recommendation on Home — deliberate overrides are fine, but it's worth knowing you're off the vetted default if the pen behaves oddly. Its **Review** button opens this page.

## Diagnostics

Live tablet input visualization. See [DIAGNOSTICS.md](../dev/DIAGNOSTICS.md) for details. When more than one tablet is connected, a **Show** selector picks which tablet's live reports to display (the daemon's debug stream carries all tablets at once); with a single tablet it's hidden.

## Plugins

A read-only list of the OpenTabletDriver plugins installed in the daemon's plugin folder. Each row shows the plugin's name, version (when available), and whether it's **Active** (referenced by an enabled output mode or filter on a tablet) or just **Installed**. The OpenTabletArtist – Pen Dynamics plugin appears here once it's installed. The header's **⋯** menu holds **Rescan plugin folder** and **Open plugin folder**. (This view is informational — it has no install or remove buttons.)

Note that a plugin can be installed here and still not run: when OTA is using the bundled daemon, it keeps only its own **filters** enabled and switches off any others. See [Tablet → filters](TABLET.md#filters).

For what OTA ships and installs on its own, whether you need anything else, and how to add a plugin by hand, see the **[Plugins guide](PLUGINS.md)**.

### Windows Ink Plugin

Manages the third-party Windows Ink output-mode plugin (from Kuuube's VoiDPlugins), which delivers pen pressure and tilt to your apps. Shows:

- **Install status** — the plugin version folded into the text (e.g. "v0.5.2 installed"), or "Not installed."
- **Output mode** — whether the tablet actually uses a Windows Ink mode ("Plugin active" / "Not configured").
- **Supported driver vs OTD** — the plugin's declared supported driver version alongside the running OTD version. A warning indicator appears if the installed plugin doesn't declare support for the current OTD version (per OTD's own compatibility rule).
- **Buttons** — **Install** (when not installed); **Check for Update** (when installed) which queries the official OTD Plugin-Repository — if a newer plugin version is found the button becomes **Install Update (vX)**, otherwise it reports "Up to date"; **Uninstall**; and a **Refresh** icon (top-right) that re-reads the installed plugin and re-checks the repository in one step. Install/update/uninstall are driven through the daemon's plugin RPC; the card updates its status as soon as each operation completes.

*(This card used to live beside the VMulti driver on a tab called **Drivers**. It manages a plugin, so it sits beside the plugin list now; VMulti has since moved to [Settings → Drivers](SETTINGS.md#drivers).)*
