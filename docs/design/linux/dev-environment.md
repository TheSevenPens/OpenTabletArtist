# Linux dev environment + verification tooling

> Sibling of the [feasibility hub](../192-linux-feasibility.md). Everything needed to **build, run, and verify**
> OTA on Linux: toolchain bootstrap, device-permission setup, environment quirks, and verification checks.
> Grounded in a real run on Fedora 44 (kernel 7.1.4) with a Wacom Intuos Pro L.

## Toolchain

| Component | Version used | Notes |
|---|---|---|
| OTA app | `net10.0` | The Avalonia app. |
| OTD daemon (submodule) | `net8.0`, **v0.6.7** | Needs the **.NET 8 runtime** to run standalone. |
| .NET SDK | **10.0.x** | Builds both (`net10` SDK builds `net8` targets fine). |

### Bootstrap (Fedora)

```sh
# .NET 10 SDK + .NET 8 runtime (Fedora ships both in the standard repos)
sudo dnf install -y dotnet-sdk-10.0 dotnet-runtime-8.0
```

### Bootstrap (other distros)

If your distro doesn't package .NET 10, use the install script (user-local, no sudo):

```sh
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh && chmod +x /tmp/dotnet-install.sh
/tmp/dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"
/tmp/dotnet-install.sh --channel 8.0 --runtime dotnet --install-dir "$HOME/.dotnet"

export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
```

### Build

```sh
dotnet build OpenTabletArtist.slnx   # builds the app AND the OTD daemon (0 errors)
dotnet test OpenTabletArtist.slnx    # 614 pass, 0 fail
dotnet run --project OpenTabletArtist
```

## Device permissions (required for tablet detection)

The OTD daemon reads tablets via `/dev/hidraw*`. By default these are owned by `root` and inaccessible.
Two things are needed: **udev rules** so the OS grants access to the HID devices, and **kernel module
blacklisting** so the built-in `wacom`/`hid_uclogic` drivers don't grab the device first.

> A future OTA release will automate this via a one-click setup (tracked in [#589](https://github.com/TheSevenPens/OpenTabletArtist/issues/589)).

### 1. Install udev rules

OTD ships a rule generator. Pipe its output into the system rules directory:

```sh
bash external/OpenTabletDriver/generate-rules.sh \
  | sudo tee /etc/udev/rules.d/99-opentabletdriver.rules > /dev/null
sudo udevadm control --reload-rules && sudo udevadm trigger
```

The generated rules use `TAG+="uaccess"` to grant the active console user access to matching devices.

#### Fallback: if `uaccess` doesn't grant permissions

On some setups (e.g. Fedora 44) the `uaccess` tag applies but the ACL isn't set. Add a direct
`MODE`/`GROUP` rule as a fallback:

```sh
echo 'KERNEL=="hidraw*", SUBSYSTEM=="hidraw", MODE="0660", GROUP="input"' \
  | sudo tee /etc/udev/rules.d/98-hidraw-permissions.rules > /dev/null
sudo udevadm control --reload-rules && sudo udevadm trigger
sudo usermod -aG input $USER
```

**Log out and back in** for the group change to take effect.

### 2. Blacklist conflicting kernel modules

The built-in `wacom` and `hid_uclogic` drivers grab tablet devices before OTD can. Unload them and
blacklist them for future boots:

```sh
# Immediate unload
sudo rmmod wacom hid_uclogic 2>/dev/null

# Persist across reboots
sudo tee /etc/modprobe.d/99-opentabletdriver.conf > /dev/null <<'EOF'
blacklist wacom
blacklist hid_uclogic
EOF

# Regenerate initramfs so the blacklist takes effect at boot
# Fedora:
sudo dracut --regenerate-all --force
# Debian/Ubuntu:
# sudo update-initramfs -u
# Arch:
# sudo mkinitcpio -P
```

### 3. Replug the tablet

After both steps, **unplug and replug** the tablet so the new rules apply.

## Environment quirks (learned the hard way)

- **Both .NET 10 SDK and .NET 8 runtime are needed.** The OTA app is `net10.0` but the OTD daemon
  (submodule) is `net8.0`. If the .NET 8 runtime is missing, `dotnet run` fails with a framework
  resolution error for the daemon.
- **`/dev/null` and `/dev/urandom` can lose permissions** when broad udev rules fire. If `dotnet build`
  fails with `CryptographicException` or commands complain about `/dev/null`, fix with
  `sudo chmod 666 /dev/null /dev/urandom /dev/random`.
- **The daemon binds its pipe lazily.** The Unix domain socket
  (`CoreFxPipe_OpenTabletDriver.Daemon` under `/tmp`) appears only after the daemon finishes startup and
  tablet detection. `DaemonClient`'s connect loop already retries for this.
- **Avalonia Brush objects are UI-thread-affine.** Creating them off the UI thread crashes with
  `InvalidOperationException: The calling thread cannot access this object because a different thread owns
  it.` This affects any code that constructs UI objects from RPC callbacks.
- **`FileStream.Lock`/`Unlock` is unsupported on macOS.** The platform analyzer (`CA1416`) treats these
  as errors even when guarded by `OperatingSystem.IsLinux()`. Use `FileShare.None` for exclusive access
  instead.

## Verification checks

### 1. `tools/DaemonProbe` — headless daemon smoke test

```sh
dotnet run --project tools/DaemonProbe
```

Expected (daemon up, tablet connected):
```
[probe] GetTablets OK -> 1 tablet(s)
          - Wacom Intuos Pro L
[probe] GetSettings OK -> settings object returned  (Profiles: 1)
[probe] ROUND-TRIP SUCCEEDED.
```

### 2. Live GUI checks

Launch the app and confirm:

- **Boots + connects:** Home shows the detected tablet; no exceptions in the Console tab.
- **Gating:** Home "Needs attention" has no VMulti/Windows Ink nags; ADVANCED rail hides the Drivers tab.
- **Console tab:** log lines appear promptly (batched rendering), no lag during tablet detection.
- **Display settings:** the "Display settings" link opens GNOME/KDE display panel.

### 3. Terminal output

On a successful launch with a tablet, the terminal should show:

```
[Detect:Info]   Searching for tablets...
[Detect:Info]   Detected tablet 'Wacom Intuos Pro L'
[Settings:Debug] Using OpenTabletDriver application data directory: ~/.config/OpenTabletDriver
[IPC:Debug]     Connected to a client.
```

If you see `DeviceUnauthorizedAccessException`, the udev rules or kernel module blacklisting isn't in
place — see the device permissions section above.

## Reproducing the end-to-end proof

1. Bootstrap the toolchain (above).
2. Set up device permissions (udev rules + kernel module blacklist).
3. Build the solution (`dotnet build OpenTabletArtist.slnx`) — 0 errors.
4. Run tests (`dotnet test OpenTabletArtist.slnx`) — 614 pass.
5. Plug in a tablet; `dotnet run --project tools/DaemonProbe` → round-trip succeeds.
6. Launch the app (`dotnet run --project OpenTabletArtist`); confirm live GUI checks above.
