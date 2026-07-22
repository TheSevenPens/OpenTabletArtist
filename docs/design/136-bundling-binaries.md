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
- A **strip-symbols** step deletes every `.pdb` before zipping. This isn't cosmetic: the native `libSkiaSharp.pdb` (~80 MB) + `libHarfBuzzSharp.pdb` (~20 MB) shipped by the SkiaSharp/HarfBuzz NuGet packages are ~100 MB of dead weight, and `-p:DebugType=none` doesn't touch them (it's managed-symbols-only), so they must be removed as files.

## Size & packaging (see `docs/dev/ARCHITECTURE.md` → Releases)

The zip is dominated by **two self-contained .NET runtimes** (app net10 + bundled daemon net8) — the price of a zero-install download, and the reason our zip is much larger than OTD's *framework-dependent* Windows builds. After PDB stripping it's ~87 MB. **Single-file publishing** (`PublishSingleFile` + `EnableCompressionInSingleFile` + `IncludeNativeLibrariesForSelfExtract`) is the next lever: verified to collapse the ~250 loose root files to a single `OpenTabletArtist.exe` and shrink the payload (#585/#586). Kept out of the current release flow pending a check that the bundled-daemon auto-start and offline plugin installs still resolve from a packaged single-file build.

## Rationale for the change

- Offline install was a top user pain (#364).
- Engineering cost of bundling is low (CI already builds Dynamics the same way).
- Compliance is manageable with explicit NOTICE artifacts — preferable to fragile first-run downloads.

## What would revert this

- A legal review concluding WDK redistribution is unacceptable **and** an alternative install path exists that does not bundle WDK tools.
- A maintained system-package story (winget/choco) that makes offline bundling redundant.
