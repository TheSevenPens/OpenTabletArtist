# Install OpenTabletArtist on Windows

OpenTabletArtist (OTA) offers a quick, guided way to install and get started. Download and run OTA,
then follow its directions to install anything else you need.

## Before you start

OTA currently works with **Windows 11, 64-bit (x64)**. Windows 7, 32-bit Windows, and Windows on ARM
are not supported.

## Supported tablets

Check the [supported tablets list](https://opentabletdriver.net/Tablets).

## Step 1 — Download and run

Get the [latest release](https://github.com/TheSevenPens/OpenTabletArtist/releases/latest) and run
**OpenTabletArtist.exe**. **Do not run it as Administrator.**

## Step 2 — Follow OTA's guidance

Check **Needs attention** on the right side of the **Home** page. If anything appears there, follow
its directions. OTA will:

- Detect conflicting tablet drivers and guide you through removing them.
- Help you install the **VMulti** driver, which is required for pressure sensitivity and tilt.
- Provide a download link for the **.NET 8 runtime**, which OpenTabletDriver requires. You'll need
  to download and install it yourself.

## Step 3 — Configure your drawing apps

Make sure your drawing apps use **Windows Ink**, not **Wintab**.

## Stopping OTA or the daemon

To stop OTA or the daemon, right-click the OTA icon in the taskbar's system tray and use its menu.
