# 319 — Embed the Windows Ink plugin

**Status:** Decided — proceeding with **Option A** (vendor prebuilt binary); Option B tracked separately
**Issue:** #319
**Related:** #317 (setup/remediation model — embedding removes a major remediation case), [136-bundling-binaries.md](136-bundling-binaries.md)

## Decision (2026-07-02)

- **Approach: Option A — vendor a prebuilt WindowsInk binary**, deployed via the existing
  `BundledPlugins/` rail. Chosen for build simplicity now.
- **Version mismatch is handled out-of-band:** we have a direct line to the plugin's developer
  (Kuuube) and will coordinate a WindowsInk build matched to our bundled OTD (currently 0.6.7),
  rather than solving the ABI-match in our build. This is the mitigation for Option A's main risk.
- **Option B (submodule build from source) is deferred** and filed as its own investigation issue —
  worth revisiting if out-of-band coordination becomes a burden or we want the ABI match by construction.
- **OTA is now licensed GPL-3.0** (same as VoiDPlugins), which makes bundling the GPL binary
  unambiguous. See `LICENSE` and `NOTICE` at the repo root.
- **GPL §6 provenance (required for Option A):** whenever we ship the WindowsInk *binary*, we must
  record the exact upstream release/commit it was built from and keep the corresponding source
  available (a provenance note beside the bundled plugin; optionally a submodule pinned purely for
  provenance). Captured in `NOTICE`.

The rest of this document is the analysis that led here.

## Goal

On a clean machine, getting OpenTabletArtist (OTA) working today requires the user to **install the Windows Ink plugin** and then set the tablet's output mode to a Windows Ink mode. The install step is the sharp edge. We want the Windows Ink plugin to be **present automatically** so a first run "just works," and we want a sane story for **keeping it up to date**.

The plugin in question is the WindowsInk output-mode plugin from Kuuube's **[VoiDPlugins](https://github.com/Kuuuube/VoiDPlugins)** — the de-facto official Windows Ink plugin for OTD. We have a direct line to its developer, which materially changes the update/coordination calculus.

## How it works today

- The daemon owns plugin install: `DaemonClient.DownloadPluginAsync(PluginMetadata)` → the daemon's `DownloadPlugin` RPC fetches the release, verifies its SHA256, and extracts it into `<PluginDir>/Windows Ink/`.
- `WindowsInkPluginService` reads the **installed** `<PluginDir>/Windows Ink/metadata.json`, and downloads the **OTD Plugin-Repository** to find the newest release named `"Windows Ink"` that's compatible with our OTD version (`WinInkUpdateState.SelectNewestCompatible`).
- The Dashboard's Windows Ink card surfaces install status, version, "update available", and a version-mismatch warning, and drives the install/update via the daemon.

So the *mechanism* is automated — but it's **user-initiated** and depends on a **network fetch** at setup time. #319 is about removing that first-run friction.

## Key facts / constraints

1. **License: GPL-3.0.** VoiDPlugins is GPL-3.0. This is the single most important constraint (see [Licensing](#licensing)).
2. **It's one plugin among several.** The repo also ships PrecisionControl, VMultiMode, TouchEmu, Reconstructor, etc. We only want **WindowsInk** (plus whatever it depends on at runtime).
3. **Version coupling.** VoiD plugins are built against a specific OTD version; our bundled daemon is **OTD 0.6.7** (from the `external/OpenTabletDriver` submodule). A plugin binary built against a different OTD ABI can fail to load. This is the crux of the "which artifact" decision.
4. **We already have the deploy rail.** Our own `OpenTabletArtist.Dynamics` plugin ships in `BundledPlugins/<folder>/` next to the app; `PressurePluginInstaller` copies it into the daemon's plugin dir when stale and restarts the daemon. `release.yml` builds it and copies it into `publish/.../BundledPlugins/`. The WindowsInk plugin can ride the **exact same rail**.
5. **The daemon expects a folder** `<PluginDir>/Windows Ink/` containing the plugin DLL(s) + `metadata.json` (our compat checks read that metadata).
6. **OTA currently has no `LICENSE` file** — a pre-existing hygiene gap that this work forces us to resolve (see [Licensing](#licensing)).

## Options

### A. Vendor a prebuilt binary
Commit VoiD's built `WindowsInk` DLL(s) + `metadata.json` into the repo (e.g. under `BundledPlugins/`), deploy like Dynamics.

- ➕ Simplest build; no extra toolchain.
- ➖ **Version-match risk:** their release binaries are built against *their* chosen OTD version, which may not equal our 0.6.7 → load failures.
- ➖ **GPL redistribution:** shipping the binary obliges us to provide the *corresponding source* for exactly that binary. Doable but a standing obligation on every bump.

### B. Git submodule, build from source in CI  ⟵ recommended
Add VoiDPlugins as a submodule (mirroring `external/OpenTabletDriver`), build **just the WindowsInk plugin against our pinned OTD**, and deploy the output via the `BundledPlugins` rail.

- ➕ **Solves version coupling for free:** built against *our* OTD 0.6.7, so ABI always matches the bundled daemon.
- ➕ **GPL source-availability is intrinsic:** the source is right there as a pinned submodule.
- ➕ Matches an existing, understood pattern (`external/OpenTabletDriver` is already a submodule).
- ➖ Heavier build; we must track their build script (a shell script today) and any native/runtime deps.
- ➖ Their repo builds *all* plugins; we either build the whole thing and copy WindowsInk, or add a targeted build. The dev contact helps here.

### C. Prefetch/runtime fetch
Keep the daemon-download mechanism but trigger it automatically on first run (silent), or pre-download during install.

- ➕ Independent updates without an OTA rebuild.
- ➖ Still a **network dependency** at setup — doesn't meet the "just works offline / bundled" goal; only removes the button click.

## Licensing

This must be settled **before** any bundling ships.

- **GPL-3.0 + aggregation.** The WindowsInk plugin is a *separate program* — a `.dll` loaded by the **OTD daemon** (a separate process from OTA), communicating through OTD's plugin interface. OTA merely places the file on disk. This is a strong **"mere aggregation"** posture (distributing independent works on the same medium), not a derivative work of OTA. Bundling it should be fine on that basis — but:
- **Binary redistribution obligation.** If we ship the *binary* (Option A), GPL-3.0 requires we make the *corresponding source* for that exact binary available. Option B sidesteps this because the source is the submodule.
- **OTA has no license today.** We should add an explicit `LICENSE`. GPL aggregation doesn't force OTA itself to be GPL, but we should be deliberate about OTA's own license and make sure it's compatible with *shipping alongside* a GPL work (permissive licenses like MIT/Apache-2.0 are fine to aggregate with GPL).
- **We have the dev's ear.** The cleanest outcome: get **explicit written blessing** from Kuuube to redistribute/build-and-bundle WindowsInk with OTA, plus agreement on attribution and how we surface the plugin's own license in-app/in-repo. This removes ambiguity regardless of the option chosen.

> Recommendation: treat licensing as a **gate**. Confirm approach + attribution with Kuuube, add an OTA `LICENSE`, and include VoiD's `LICENSE` + attribution alongside the bundled plugin.

## Update handling

- **Bootstrap vs. override — keep both.** Bundle a **known-good, OTD-0.6.7-matched** WindowsInk for zero-config first run, *and* keep the existing "check the plugin repo for a newer compatible release + update via the daemon" path as a **user-driven override**. First run needs no network; power users still get newer plugin builds without waiting for an OTA release.
- Under Option B, the routine update is: **bump the submodule to a new tag → rebuild OTA → release.** The issue already accepts that a plugin update may require an app rebuild; the dev contact lets us coordinate a bump when a meaningful WindowsInk change lands.
- The bundled `metadata.json` keeps our version/compat UI (`ReadInstalled`, `WinInkUpdateState`) meaningful, so "you're on the bundled 0.5.2, a newer 0.5.x is available" still renders correctly.

## Recommendation vs. decision

The original analysis recommended **Option B** (submodule build) because it makes the ABI match our
bundled daemon *by construction* and gives GPL source-availability for free.

**We chose Option A instead** (see [Decision](#decision-2026-07-02)): the direct relationship with the
plugin's developer lets us coordinate a WindowsInk build matched to our OTD version out-of-band, which
neutralizes Option A's main risk (ABI mismatch) without taking on B's heavier build. B remains a good
future move and is tracked as its own investigation issue. Adopting GPL-3.0 for OTA removes the licensing
friction that would otherwise have pushed us toward B.

## Open questions (some for Kuuube)

1. **Blessing + attribution:** OK to build-and-bundle WindowsInk with OTA under GPL-3.0? Preferred attribution + how should we surface the plugin's license in-app?
2. **Targeted build:** Can WindowsInk be built in isolation, or must we build the whole plugin set and cherry-pick? Any shared/common project it depends on?
3. **Runtime dependencies:** Does WindowsInk need anything beyond its managed DLL at runtime (native libs, a driver, elevation)? If so, those must be bundled/handled too.
4. **OTD-version policy:** Which OTD versions does the current WindowsInk support? Does building against 0.6.7 "just work," or are there source tweaks?
5. **Update cadence:** How often does WindowsInk change in a user-visible way, and how do we want to signal "an update to the bundled plugin is available"?

## Implementation sketch (once licensing clears, Option B)

1. Add `external/VoiDPlugins` (or `plugins/vendor/VoiDPlugins`) as a git submodule pinned to a tag.
2. Add a build step (in `release.yml`, next to the Dynamics bundling) that builds WindowsInk against our OTD and copies its output + `metadata.json` into `publish/.../BundledPlugins/Windows Ink/`.
3. Generalize `PressurePluginInstaller` (or add a sibling) to also deploy the `Windows Ink` bundle into the daemon's plugin dir when stale — reusing the copy-if-stale + restart-daemon logic.
4. On connect, ensure the bundled WindowsInk is present (like `EnsurePressurePluginAsync`), so a clean machine has it without any user action.
5. Keep the Dashboard update-check as the override path; add "bundled" vs "updated" provenance to the WinInk card.
6. Add OTA `LICENSE`; include VoiD's `LICENSE` + attribution beside the bundled plugin.
7. Dev-build story: fall back to the plugin's local submodule build output (mirroring how Dynamics resolves its dev-build path).
