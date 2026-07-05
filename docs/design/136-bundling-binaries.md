# Decision — bundled-first setup binaries (#136, updated #364)

> Status: **decided — bundle VMulti + Windows Ink in release artifacts; download as fallback.**
> Supersedes the earlier "do not bundle" decision recorded here before #364 shipped.

## Strategy

Release builds ship offline-capable copies of:

| Component | Release path | Runtime fallback |
|-----------|--------------|------------------|
| **Windows Ink plugin** (Kuuube/VoidPlugins, GPL-3.0) | `BundledPlugins/WindowsInk/` | Daemon downloads from Plugin-Repository |
| **VMulti driver package** (djpnewton/vmulti-bin + WDK tools) | `Bundled/VMulti.Driver.zip` | `VMultiInstaller` downloads from GitHub |
| **Dynamics plugin** (first-party) | `BundledPlugins/OpenTabletArtistDynamics/` | Built in CI |

The app prefers the bundled copy when present; network download is the fallback when bundling failed or the user runs from a dev build without bundles.

## Compliance (#386)

We are now a **redistributor** of third-party binaries. Release artifacts must include:

- `docs/THIRD_PARTY_NOTICES.md` — attribution, licenses, and source offers
- GPL-3.0 Windows Ink plugin: ship LICENSE + written offer / source link per plugin terms
- VMulti: MIT driver core + Microsoft WDK tooling (document upstream origins)

## Release pipeline

- The Windows Ink bundle step in `.github/workflows/release.yml` **must succeed** — releases cannot silently ship without the offline WinInk bundle.
- VMulti bundle step already fails the workflow on error.

## Rationale for the change

- Offline install was a top user pain (#364).
- Engineering cost of bundling is low (CI already builds Dynamics the same way).
- Compliance is manageable with explicit NOTICE artifacts — preferable to fragile first-run downloads.

## What would revert this

- A legal review concluding WDK redistribution is unacceptable **and** an alternative install path exists that does not bundle WDK tools.
- A maintained system-package story (winget/choco) that makes offline bundling redundant.
