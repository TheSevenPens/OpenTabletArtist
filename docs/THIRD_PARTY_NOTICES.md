# Third-party notices

OpenTabletArtist release builds may include redistributed third-party components.
This file satisfies attribution and license-awareness obligations for bundled artifacts (#386).

## Bundled in release artifacts

### Windows Ink plugin (Kuuube / VoidPlugins)

- **License:** GPL-3.0
- **Source:** https://github.com/Kuuuube/VoidPlugins (Windows Ink plugin)
- **Shipped as:** `BundledPlugins/WindowsInk/` (DLLs + `metadata.json`)
- **GPL note:** You may obtain source code for the GPL-licensed plugin from the upstream repository above.

### VMulti virtual pen driver package

- **Driver files (`vmulti.sys`, `vmulti.inf`, etc.):** MIT — https://github.com/djpnewton/vmulti
- **Packaged zip:** https://github.com/X9VoiD/vmulti-bin (release v1.0)
- **WDK tools in package (`devcon.exe`, `DIFxCmd.exe`, …):** Microsoft Windows Driver Kit — used under upstream packaging; redistribution terms are upstream's responsibility.
- **Shipped as:** `Bundled/VMulti.Driver.zip`

### OpenTabletDriver daemon

- **License:** LGPL-3.0 (submodule / bundled executable)
- **Source:** https://github.com/OpenTabletDriver/OpenTabletDriver

### OpenTabletArtist Dynamics plugin

- **License:** Same as OpenTabletArtist (first-party)
- **Shipped as:** `BundledPlugins/OpenTabletArtistDynamics/`

## Download-only fallbacks (not redistributed by us)

When bundles are absent or fail, the app may download:

- Windows Ink plugin from the OpenTabletDriver Plugin-Repository
- VMulti package from X9VoiD/vmulti-bin on GitHub

Those downloads are fetched at runtime from upstream; OpenTabletArtist does not republish them in that path.
