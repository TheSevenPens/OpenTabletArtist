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
