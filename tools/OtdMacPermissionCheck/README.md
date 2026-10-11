# macOS permission check

Run `python3 tools/OtdMacPermissionCheck/otd-permcheck.py` with the daemon up to find out whether its
Input Monitoring grant is actually working. Nothing to build or install: Python 3 only.

On macOS, enumerating a HID device needs no permission while opening one needs Input Monitoring, so the
script asks the running daemon for both lists and compares them:

```
HID devices the daemon can see: 6, from tablet vendors: 2
  seen:   Wacom Intuos Pro L  (vid 0x056a pid 0x03f9)  Wacom
  seen:   Wacom Intuos Pro L  (vid 0x056a pid 0x03f9)  Wacom
Tablets the daemon opened: 1
  opened: Wacom PTK-870

VERDICT: the daemon opened a tablet, so Input Monitoring is in effect for this daemon.
```

A tablet seen but none opened is the missing-grant signature. It is the same inference the Home page
makes for its "OpenTabletDriver can't read your tablet" issue; this prints the evidence.

Exit codes: `0` a tablet was opened · `1` seen but not opened · `2` nothing from a known vendor on the bus
(`--all` lists every device) · `3` no daemon socket.

What it cannot tell you:

- **Accessibility.** There is no probe for it. Bind a key and press it.
- **Which app the grant is recorded under.** macOS credits a daemon's grant to the app that launched it,
  so System Settings lists OpenTabletArtist (or Terminal, or whatever started the daemon), not the daemon.
  An ad-hoc signed dev build also gets a new code hash each rebuild, so a grant can stop matching after
  `dotnet build`. Reading the record itself means `sudo sqlite3` on `/Library/Application
  Support/com.apple.TCC/TCC.db`, which needs Full Disk Access for the terminal; this script answers the
  question that matters without that.

Linux: the daemon's socket is in `/tmp`, and the script finds it there too, but Linux has no
Input Monitoring; a tablet seen but not opened there is a udev-rules problem.
