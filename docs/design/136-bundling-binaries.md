# Decision — bundle vs. download the VMulti + Windows Ink binaries (#136)

> Status: **decided — keep downloading at runtime; do not bundle (for now).** This note records the
> assessment so the question doesn't have to be re-investigated.

## Question

Today the app **downloads** the VMulti driver package and installs the Windows Ink plugin at runtime
(the daemon fetches it). #136 asked whether to **bundle** these in our release instead — for offline
install, a pinned/vetted version, and no dependency on GitHub staying reachable. We already bundle
the OTD daemon and our own Dynamics plugin this way, so the mechanism exists.

## Decision

**Keep the current download-at-runtime behavior.** We do **not** bundle VMulti or Windows Ink. The
deciding factor is licensing/redistribution, not engineering effort or size (~2.2 MB total):
downloading means we never redistribute third-party binaries; bundling would make us a redistributor
of GPL‑3.0 code and Microsoft WDK tools.

## What bundling would involve (inventory)

### VMulti — `X9VoiD/vmulti-bin` v1.0 (1.9 MB zip; pinned in `VMultiInstaller.DownloadUrl`)

Our in-app install/uninstall would need everything except the two `.bat`s (we generate our own scripts):

| File | Size | Origin / license |
|---|---|---|
| `vmulti.sys`, `vmulti.inf`, `pentablethid.cat`, `WinTab32.dll`, `hidkmdf.sys` | ~180 KB total | djpnewton/vmulti — **MIT** (redistributable) |
| `devcon.exe` (91 KB), `DIFxCmd.exe` (26 KB), `DIFxAPI.dll` (534 KB), `WdfCoInstaller01009.dll` (1.7 MB) | ~2.35 MB | **Microsoft WDK** — redistribution rights are unclear |

The MIT driver core is tiny and clearly redistributable; **the bulk and the licensing risk are the
Microsoft WDK tools**, which the current install depends on (`devcon` to create/remove the device,
`DIFxCmd` to (un)install the package).

### Windows Ink — `Kuuuube/VoidPlugins` 0.5.2, `WindowsInk.zip` (297 KB) — **GPL‑3.0**

Would be copy-installed like our Dynamics plugin (`WindowsInk.dll`, `VMulti.dll`, `VoiD.dll` — all
GPL‑3.0 — plus `Newtonsoft.Json.dll` (MIT) and a generated `metadata.json`).

## Why not bundle (rationale)

- **GPL‑3.0 (Windows Ink):** redistributing is allowed as *aggregation*, but adds compliance
  obligations (ship the plugin's LICENSE and a written offer / source link). Downloading avoids this.
- **Microsoft WDK tools (VMulti):** bundling `devcon`/`DIFx*`/`WdfCoInstaller` makes us redistribute
  Microsoft tooling whose redistribution terms are murky. Downloading the upstream package sidesteps it.
- **Size/benefit:** the convenience win (offline, pinned) doesn't outweigh taking on redistributor
  obligations while the download path works.

## What would change this decision

- **Windows Ink:** if offline install becomes important, it's the cleaner case — bundle the plugin
  with full GPL‑3.0 compliance (its LICENSE + source offer in the release). Lowest-risk first step.
- **VMulti:** bundle only after dropping the Microsoft-tool dependency — i.e. a **native SetupAPI**
  install/uninstall (`SetupDiCreateDeviceInfo` + `DIF_REGISTERDEVICE` / `DIF_REMOVE`, `pnputil`),
  which lets us ship just the MIT driver core (~180 KB) and removes `devcon`/`DIFx` entirely. That's
  the bigger option noted in #111/#112.
- **Reliability:** if the upstream GitHub releases prove flaky, prefer hardening the download
  (retry / clearer errors) before bundling.

## Related

- #110 / #111 / #112 — in-app VMulti install/uninstall (the native-SetupAPI path above would build on these).
- #113 — VMulti driver source confirmation (X9VoiD/vmulti-bin, `djpnewton\vmulti`).
