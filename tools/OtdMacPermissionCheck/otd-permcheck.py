#!/usr/bin/env python3
"""Is OpenTabletDriver's Input Monitoring grant actually working?

Asks the running daemon what it can SEE (GetDevices: every HID endpoint it can enumerate, which needs
no permission on macOS) versus what it could OPEN (GetTablets: the tablets it detected, which needs
Input Monitoring). A supported tablet in the first list and nothing in the second is the signature of
a missing grant. The Home page's "OpenTabletDriver can't read your tablet" check makes the same
inference; this is the same question from a terminal, with the evidence printed.

Talks to the daemon over its socket with header-delimited JSON-RPC, the way StreamJsonRpc frames it,
so it needs nothing but Python 3. No dependencies, nothing installed, nothing written.

    python3 tools/OtdMacPermissionCheck/otd-permcheck.py

Exit codes: 0 a tablet was opened  ·  1 seen but not opened (grant missing)  ·  2 nothing on the bus
            ·  3 no daemon socket found.
"""
import glob
import json
import os
import socket
import sys

# USB vendor ids of tablets OTD supports. Extend if yours is missing; the device list is printed
# unfiltered on request (--all) so an unknown vendor is still visible.
VENDORS = {
    0x056A: "Wacom",
    0x256C: "Huion / Gaomon",
    0x28BD: "XP-Pen",
    0x0B57: "Ugee / XP-Pen",
    0x2D1F: "Xencelabs",
    0x5543: "UC-Logic",
    0x08CA: "Artisul",
    0x0416: "VEIKK",
}


def socket_path():
    candidates = []
    for base in (os.environ.get("TMPDIR"), "/tmp"):
        if base:
            candidates += glob.glob(os.path.join(base, "CoreFxPipe_OpenTabletDriver.Daemon"))
    return candidates[0] if candidates else None


def rpc(path, method):
    s = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
    s.settimeout(10)
    s.connect(path)
    body = json.dumps({"jsonrpc": "2.0", "id": 1, "method": method, "params": []}).encode()
    s.sendall(b"Content-Length: %d\r\n\r\n" % len(body) + body)
    buf = b""
    while True:
        # The daemon also pushes notifications (log lines) on the same connection; skip until our reply.
        while b"\r\n\r\n" not in buf:
            buf += s.recv(65536)
        header, rest = buf.split(b"\r\n\r\n", 1)
        length = int(next(l for l in header.decode().split("\r\n")
                          if l.lower().startswith("content-length")).split(":")[1])
        while len(rest) < length:
            rest += s.recv(65536)
        message = json.loads(rest[:length])
        buf = rest[length:]
        if message.get("id") == 1:
            if "error" in message:
                sys.exit(f"{method} failed: {message['error']}")
            return message["result"]


def main():
    show_all = "--all" in sys.argv
    path = socket_path()
    if path is None:
        print("No daemon socket found. Is OpenTabletDriver running?")
        return 3

    devices = rpc(path, "GetDevices")
    tablets = rpc(path, "GetTablets")

    seen = [d for d in devices if d.get("VendorID") in VENDORS]
    print(f"HID devices the daemon can see: {len(devices)}, from tablet vendors: {len(seen)}")
    for d in (devices if show_all else seen):
        name = d.get("ProductName") or d.get("FriendlyName") or "?"
        vendor = VENDORS.get(d.get("VendorID"), "")
        print(f"  seen:   {name}  (vid {d.get('VendorID', 0):#06x} pid {d.get('ProductID', 0):#06x})  {vendor}")
    print(f"Tablets the daemon opened: {len(tablets)}")
    for t in tablets:
        print(f"  opened: {t['Properties']['Name']}")

    print()
    if tablets:
        print("VERDICT: the daemon opened a tablet, so Input Monitoring is in effect for this daemon.")
        return 0
    if seen:
        print("VERDICT: the daemon can see a tablet but could not open it. Input Monitoring is NOT granted")
        print("         to it. System Settings > Privacy & Security > Input Monitoring; the entry is the app")
        print("         that launched the daemon (OpenTabletArtist, or a terminal), not the daemon itself.")
        return 1
    print("VERDICT: no tablet from a known vendor on the bus. Plug one in, or run with --all to list")
    print("         every device the daemon sees.")
    return 2


if __name__ == "__main__":
    sys.exit(main())
