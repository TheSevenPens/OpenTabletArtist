# macOS permissions: what OpenTabletDriver needs, who gets credited, and how to tell

The daemon reads a USB tablet and synthesizes pointer and keyboard input. On macOS both of those are
gated by TCC (Transparency, Consent and Control), the system that backs the Privacy & Security pane.
This note collects what we know about it, including what was learned the hard way on 2026-10-11 while
investigating why a working setup looked broken.

## The two grants

| Grant | TCC service | Who needs it | What it gates |
|---|---|---|---|
| **Input Monitoring** | `kTCCServiceListenEvent` | the daemon | Opening a HID device to read reports. Without it the daemon can *enumerate* the tablet but cannot *open* it, so detection finds nothing. |
| **Accessibility** | `kTCCServiceAccessibility` | the daemon | Posting synthesized input. OTD's macOS output posts every pointer and key event through `CGEventPost` (`OpenTabletDriver.Native/OSX/OSX.cs`, `MacOSVirtualMouse`, `MacOSVirtualKeyboard`). Key bindings in particular need it. |

What needs **no** grant, and why that matters below:

- **Enumerating HID devices.** `IOHIDManager` lists vendor and product ids freely. This is what makes
  the missing-grant signature detectable.
- **Display geometry.** `CGDisplayBounds` and friends are unrestricted.
- **Everything OpenTabletArtist itself does.** The app never opens a HID device: pen input for Scribble,
  Test and calibration arrives over the daemon's RPC debug stream. Global hotkeys are a no-op off Windows
  (`GlobalHotkeyService`). The calibration overlay's only native call sets a window level through
  `objc_msgSend`. OTA holds no TCC grant of its own.

## Who the grant is recorded under

This is the part that makes the Privacy & Security list misleading.

**TCC credits a grant to the *responsible process*, which is the app that launched the daemon**, not the
daemon. So the row you see and toggle is:

| The daemon was started by | The row in System Settings says |
|---|---|
| OpenTabletArtist (the normal case: OTA auto-starts it) | OpenTabletArtist |
| a terminal, `dotnet run`, or a script | Terminal (or iTerm, or whatever hosts the shell) |
| an AI assistant's shell tool | that assistant's app |
| OTD's own `OpenTabletDriver.app` | OpenTabletDriver |

Consequences:

- A daemon that works when OTA is launched from a terminal can stop seeing the tablet when OTA is launched
  from Finder, because the grant belonged to the terminal.
- Granting "OpenTabletArtist" in the Input Monitoring pane is correct even though OTA does no HID reading.
  The permission is the daemon's; the name on it is the launcher's.

## Why a rebuild loses the grant

TCC ties a grant to a **code identity**. For a signed bundle that is the signing identity plus bundle id,
which is stable across builds. For an unbundled, ad-hoc signed binary — every `dotnet build` output — it is
the **cdhash**, which changes on every build. So:

- A fresh dev build of the daemon or of OTA is a new identity. The old row may still be listed, but it no
  longer matches, and the daemon silently fails to open the tablet. Toggle the row off and on, or remove and
  re-add it, and it matches again until the next build.
- This is the catch-22 described in [HANDOFF.md](HANDOFF.md) and the reason a **stable signing identity**
  is the key deliverable of the packaging phase: OTD.app keeps its grant across updates for exactly this
  reason.

## How to tell whether the grant is working

Ordered from most to least useful. The first two answer the question that matters — *is the daemon reading
the tablet right now* — without touching the permission database.

### 1. Ask the daemon: `tools/OtdMacPermissionCheck`

```sh
python3 tools/OtdMacPermissionCheck/otd-permcheck.py
```

Compares what the daemon can **see** (`GetDevices`) with what it could **open** (`GetTablets`). A supported
tablet seen but none opened means Input Monitoring is not in effect for this daemon. Python 3 only; it
speaks the daemon's JSON-RPC over the Unix socket directly. See `tools/OtdMacPermissionCheck/README.md`.

### 2. The Home page

OTA makes the same inference. When no tablet is detected, `AppSession.DaemonSeesUnopenedTabletAsync` asks
the daemon for its device list and looks for a supported vendor and product id. A match raises the
`otd.permissionsMissing` health issue, severity Broken, shown on Home as **"OpenTabletDriver can't read your
tablet"** with an **Open Settings** button that deep-links to the Input Monitoring pane
(`x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent`, in `PlatformShell`).
"Nothing needs attention" means that check passed or was not needed.

Only Input Monitoring is covered. **There is no probe for Accessibility.** If the tablet moves the cursor
but a bound key does nothing, that is the one to check — by pressing the key.

### 3. The daemon log

`Found tablet 'Wacom PTK-870'` means the device opened. `No tablets were detected.` with a tablet plugged in
means it did not. The daemon's logs are under `~/Library/Application Support/OpenTabletDriver/Logs/`; when
OTA launches the daemon its stdout also goes to OTA's log.

### 4. System Settings

Privacy & Security → Input Monitoring, and → Accessibility. Read it knowing the two sections above: the row
is the launcher's, and a listed row is not necessarily a matching one after a rebuild.

### 5. The TCC database itself

Both grants live in the system-wide database, `/Library/Application Support/com.apple.TCC/TCC.db`. Reading it
needs `sudo` **and** a terminal that has Full Disk Access; without the latter even `sudo sqlite3` reports
`authorization denied`. If you grant Terminal Full Disk Access (Privacy & Security → Full Disk Access, then
quit and reopen Terminal):

```sh
sudo sqlite3 "/Library/Application Support/com.apple.TCC/TCC.db" \
  "SELECT service, client, client_type, auth_value, datetime(last_modified,'unixepoch','localtime')
   FROM access WHERE service IN ('kTCCServiceListenEvent','kTCCServiceAccessibility') ORDER BY service, client;"
```

- `auth_value`: 2 allowed, 0 denied, 1 undecided.
- `client_type`: 0 means `client` is a bundle id; 1 means it is an absolute path (how a bare binary appears).

Full Disk Access is a large grant for a small payoff — it lets anything run from that terminal read every
file on the machine — so this is the last resort, worth it only to see the Accessibility row or to find out
which app a grant was credited to. There is no public API to query another process's grant;
`IOHIDCheckAccess` and `AXIsProcessTrusted` answer only for the calling process. `tccutil` can only reset
(`tccutil reset ListenEvent <bundle id>`), not read.

## Troubleshooting flow

1. Tablet plugged in, OTA says no tablet. Run `otd-permcheck.py`.
2. **Seen but not opened:** Privacy & Security → Input Monitoring. Find the row for whatever launched the
   daemon (usually OpenTabletArtist). If it is already on, toggle it off and on — a rebuilt binary no longer
   matches the old row. Restart the daemon (Advanced → Daemon → Restart) so it reopens the device.
3. **Nothing seen:** not a permissions problem. Check the cable and the USB hub, and whether the tablet is in
   the OTD configuration set.
4. **Opened, cursor moves, key bindings do nothing:** Accessibility. Same pane, same launcher-row rule.
5. **Works from the terminal, not from Finder:** the grant belongs to the terminal. Grant OpenTabletArtist.

## Related things learned the same day

Not permissions, but found with the same tools and worth keeping next to them.

- **The daemon speaks header-delimited JSON-RPC over `$TMPDIR/CoreFxPipe_OpenTabletDriver.Daemon`**, and
  plain Python can talk to it: send `Content-Length: N\r\n\r\n{json}` and read frames back, skipping the
  `Message` notifications the daemon pushes on the same connection. That is how `otd-permcheck.py` works and
  how the live-settings diff that found #1008 was taken. `tools/DaemonProbe` does the same from .NET.
- **The daemon takes `--appdata <dir>`** (and `--config <dir>`). Point it at a scratch copy of
  `settings.json` to reproduce a settings-related state without touching the real one; OTA then connects to
  it as a foreign daemon. `OTA_APPDATA` does the same for OTA's own files.
- **The daemon rewrites loaded settings in memory and never writes them back** (revision stamp, button
  slots, wheel thresholds). That is what #1008 corrected on the OTA side; it is also why a file written by one
  daemon version reads as "changed" under the next.
