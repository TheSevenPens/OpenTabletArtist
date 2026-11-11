"""Capture a window's client area as a crisp, fixed-size PNG, whatever monitor or scale it is on.

The idea: lay the window out at a fixed size in device-independent pixels (the "layout", default
1280x720), so every page looks the same on every monitor. Capture its client area as real screen pixels,
which is 1280*scale x 720*scale (2880x1620 at 225%), then resample to the target size (default 1920x1080).
Cropping to the client area means a taskbar, other windows and the window frame never appear.

Usage:
  python window_shot.py size  --title OpenTabletArtist                # lay the window out at 1280x720 DIP
  python window_shot.py shot  --title OpenTabletArtist --out page.png  # capture, resample, save
  python window_shot.py info  --title OpenTabletArtist                # print scale, client size, monitor

DPI: this process declares per-monitor-v2 awareness first, so every coordinate below is a physical pixel.
Do not call this from a host that has already fixed its DPI awareness differently without checking `info`.
"""
import argparse
import ctypes
import sys
from ctypes import wintypes

from PIL import Image, ImageDraw, ImageFilter, ImageGrab

user32 = ctypes.WinDLL("user32", use_last_error=True)
# Must happen before any window call, or coordinates are virtualised.
user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))  # PER_MONITOR_AWARE_V2


class RECT(ctypes.Structure):
    _fields_ = [("left", wintypes.LONG), ("top", wintypes.LONG), ("right", wintypes.LONG), ("bottom", wintypes.LONG)]


class POINT(ctypes.Structure):
    _fields_ = [("x", wintypes.LONG), ("y", wintypes.LONG)]


class MONITORINFO(ctypes.Structure):
    _fields_ = [("cbSize", wintypes.DWORD), ("rcMonitor", RECT), ("rcWork", RECT), ("dwFlags", wintypes.DWORD)]


user32.FindWindowW.restype = wintypes.HWND
user32.GetDpiForWindow.restype = wintypes.UINT
user32.MonitorFromWindow.restype = wintypes.HANDLE
SWP_NOZORDER, SWP_NOACTIVATE = 0x0004, 0x0010


def find(title):
    h = user32.FindWindowW(None, title)
    if not h:
        sys.exit(f"No window titled '{title}'.")
    return h


def client_size(h):
    r = RECT()
    user32.GetClientRect(h, ctypes.byref(r))
    return r.right, r.bottom


def client_origin(h):
    p = POINT(0, 0)
    user32.ClientToScreen(h, ctypes.byref(p))
    return p.x, p.y


def top_inset(h):
    """Rows at the top of the client area that DWM paints with the window border. For this window the
    border is drawn *inside* the client at the top but outside it on the other three sides, so the
    thickness is read from the left side: client origin minus the visible frame's left edge."""
    r = RECT()
    ctypes.windll.dwmapi.DwmGetWindowAttribute(h, 9, ctypes.byref(r), ctypes.sizeof(r))  # EXTENDED_FRAME_BOUNDS
    return max(0, client_origin(h)[0] - r.left)


def work_area(h):
    mi = MONITORINFO()
    mi.cbSize = ctypes.sizeof(MONITORINFO)
    user32.GetMonitorInfoW(user32.MonitorFromWindow(h, 2), ctypes.byref(mi))  # nearest
    return mi.rcWork


def set_client_size(h, cw, ch, margin=40):
    """Resize so the client area is exactly cw x ch pixels, and put it inside the monitor's work area
    (which excludes the taskbar). Iterates, because the frame's thickness is not known up front."""
    user32.ShowWindow(h, 9)  # SW_RESTORE: un-minimise / un-maximise
    wa = work_area(h)
    r = RECT()
    user32.GetWindowRect(h, ctypes.byref(r))
    w, hgt = r.right - r.left, r.bottom - r.top
    for _ in range(6):
        user32.SetWindowPos(h, None, wa.left + margin, wa.top + margin, w, hgt, SWP_NOZORDER)
        cur_w, cur_h = client_size(h)
        if (cur_w, cur_h) == (cw, ch):
            break
        w += cw - cur_w
        hgt += ch - cur_h
    return client_size(h)


def foreground(h):
    user32.SetForegroundWindow(h)
    user32.SetCursorPos(0, 0)  # park the pointer well away from the capture


def round_corners(img, radius):
    """Make the four corners transparent. The capture includes the pixels the OS leaves outside a
    Windows 11 window's rounded corners (the border colour, or whatever is behind), which look like specks
    when the image sits on a page. Drawn at 4x and reduced so the edge is anti-aliased."""
    k = 4
    big = Image.new("L", (img.width * k, img.height * k), 0)
    ImageDraw.Draw(big).rounded_rectangle((0, 0, big.width - 1, big.height - 1), radius=radius * k, fill=255)
    img = img.convert("RGBA")
    img.putalpha(big.resize(img.size, Image.LANCZOS))
    return img


def cmd_info(a):
    h = find(a.title)
    dpi = user32.GetDpiForWindow(h)
    wa = work_area(h)
    print(f"dpi={dpi} scale={dpi/96:.3f} client={client_size(h)} origin={client_origin(h)} "
          f"work_area=({wa.left},{wa.top},{wa.right},{wa.bottom})")


def cmd_size(a):
    h = find(a.title)
    scale = user32.GetDpiForWindow(h) / 96
    cw, ch = round(a.layout_w * scale), round(a.layout_h * scale)
    inset = top_inset(h)
    got = set_client_size(h, cw, ch + inset)
    foreground(h)
    print(f"scale {scale:.3f}: wanted client {cw}x{ch + inset} (content {cw}x{ch} + {inset}px border), got {got[0]}x{got[1]}")
    if got != (cw, ch + inset):
        sys.exit("Could not reach the target size; is the window fixed-size, or the work area too small?")


def cmd_shot(a):
    h = find(a.title)
    scale = user32.GetDpiForWindow(h) / 96
    inset = top_inset(h)
    cw, ch_full = client_size(h)
    ch = ch_full - inset
    want = (round(a.layout_w * scale), round(a.layout_h * scale))
    if (cw, ch) != want:
        sys.exit(f"Content is {cw}x{ch}, expected {want}. Run 'size' first.")
    foreground(h)
    x, y = client_origin(h)
    y += inset
    img = ImageGrab.grab(bbox=(x, y, x + cw, y + ch), all_screens=True).convert("RGB")
    if img.size != (a.width, a.height):
        img = img.resize((a.width, a.height), Image.LANCZOS)
    if a.sharpen > 0:
        img = img.filter(ImageFilter.UnsharpMask(radius=a.radius, percent=a.sharpen, threshold=a.threshold))
    if a.corner_radius > 0:
        img = round_corners(img, a.corner_radius)
    img.save(a.out, optimize=True)
    print(f"saved {a.out}: {img.size[0]}x{img.size[1]} from {cw}x{ch} (scale {scale:.3f}), sharpen={a.sharpen}")


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("command", choices=["info", "size", "shot"])
    p.add_argument("--title", default="OpenTabletArtist")
    p.add_argument("--layout-w", type=int, default=1280)
    p.add_argument("--layout-h", type=int, default=720)
    p.add_argument("--width", type=int, default=1920)
    p.add_argument("--height", type=int, default=1080)
    p.add_argument("--out", default="shot.png")
    p.add_argument("--sharpen", type=int, default=0, help="UnsharpMask percent; 0 = none")
    p.add_argument("--radius", type=float, default=0.6)
    p.add_argument("--corner-radius", type=float, default=12, help="output px; 0 keeps the OS corner pixels")
    p.add_argument("--threshold", type=int, default=2)
    a = p.parse_args()
    {"info": cmd_info, "size": cmd_size, "shot": cmd_shot}[a.command](a)


if __name__ == "__main__":
    main()
