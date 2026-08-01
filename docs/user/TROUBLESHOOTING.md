# Troubleshooting

## "Not connected to daemon" on Home

1. Click **Fix** on the "Not connected to daemon" card — it starts the daemon (built from the submodule) and connects, morphing into a "Connecting to daemon…" state while it works.
2. If that doesn't resolve it, click **Open daemon page** (Advanced → Daemon) for the full controls — **Start**, **Restart**, and a **Refresh** to re-check the connection.
3. The daemon auto-starts on app launch — if it didn't, check whether another OTD instance is already running.

If the daemon page reports that **OpenTabletDriver.Daemon.exe wasn't found**, the daemon exe is missing from your install. The app checks for it before every connection attempt and says so plainly instead of silently timing out. Re-extract the release zip completely — the **`Daemon/`** subfolder must sit next to `OpenTabletArtist.exe`. *(Building from source? Build the whole solution so the daemon is produced — see [BUILDING.md](../dev/BUILDING.md).)*

## "No Tablet Detected" even though my tablet is plugged in

1. Make sure the daemon is running (Advanced → Daemon).
2. Wait a few seconds — the app reconciles with the daemon every 30 seconds (and immediately on connect).
3. Click the refresh icon to force an immediate check.

## Diagnostic log (for bug reports)

OpenTabletArtist keeps a small diagnostics log of its own at:

`%LOCALAPPDATA%\OpenTabletArtist\logs\app.log` *(rolled to `app.log.1` once it gets large)*

It records the background problems the app would otherwise handle quietly — daemon connection failures and retries, settings / preset / tablet-config read or save problems, plugin-repository lookups, and per-app profile switches that didn't apply. It only writes when something actually goes wrong, so a healthy run leaves little or nothing. If something isn't behaving and the reason isn't obvious, this file is the first place to look and the most useful thing to attach to a bug report.

*(This is separate from **Advanced → Console**, which streams the OpenTabletDriver **daemon's** log live.)*

## "Your settings couldn't be read" on Home

If your `settings.json` ever becomes corrupt or unreadable, OpenTabletArtist starts with defaults rather than crashing, and moves the unreadable file aside to a timestamped backup (`settings.json.corrupt-…`) next to it so nothing is lost. Home shows a "Your settings couldn't be read" card naming the backup. To recover, close the app and restore that backup file over `settings.json` in `%LOCALAPPDATA%\OpenTabletArtist\`. If the card instead says the file **couldn't be backed up**, copy `settings.json` somewhere safe before making changes — a later save may overwrite it.
