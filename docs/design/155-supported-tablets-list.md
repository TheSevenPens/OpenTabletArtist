# Investigation — show a list of OTD-compatible tablets (#155)

> Status: **investigated; implementation deferred.** Feasible and well-scoped — recorded here so it
> can be picked up later without re-investigating.

## Question

Can the app show a known list of tablets compatible with OpenTabletDriver?

## Finding: yes, feasible

OTD's "supported tablets" are its **tablet configuration files** — **~339 configs across 26
manufacturers** (Wacom, Huion, XP‑Pen, Gaomon, XenceLabs, VEIKK, Parblo, UGEE, …). Each is JSON with
a friendly `Name` plus `Specifications` (digitizer size, pen max pressure, pen/aux button counts) and
`DigitizerIdentifiers` (USB VID/PID).

### Where the list lives (important)

- The built-in configs are **embedded resources** in `OpenTabletDriver.Configurations.dll`
  (`<EmbeddedResource Include=".\Configurations\*\*.json">`) — **not** loose files on disk, and **not**
  exposed by any daemon RPC. `IDriverDaemon` only reports *detected* tablets (`GetTablets` /
  `DetectTablets`), not the full supported set.
- `AppInfo.ConfigurationDirectory` (on disk) holds only **user-added / override** configs — that's
  what the existing **Custom Tablet Configs** page already lists. It is *not* the full catalog.

So #155 is a different thing from Custom Tablet Configs: that page shows the user's *installed* config
files; #155 is the full *catalog of what OTD supports*.

## Recommended implementation (when picked up)

- **Data source:** read the embedded `.json` resources from `OpenTabletDriver.Configurations.dll`
  (add a `ProjectReference`/assembly reference and enumerate `GetManifestResourceNames()` filtered to
  `.json`, or reuse OTD.Desktop's configuration provider if it exposes the parsed set). Manufacturer =
  the folder segment in the resource path; name + specs = the JSON. This is **version-matched to our
  bundled daemon, works offline, and needs no daemon round-trip.**
- **UX:** a searchable "Supported Tablets" list — filter by manufacturer, show name + key specs
  (size, pressure levels, buttons), and highlight the user's currently-detected tablet if present.
  Placement TBD: a new sidebar page, or a second section on the Custom Tablet Configs page.
- **Parsing:** we already parse a config's `Name` in `CustomTabletConfigsView`; the same shape applies.

## Effort / risk

Mostly UI; data access is straightforward and read-only. Low risk. The only real decision is
placement (new page vs. folded into Custom Tablet Configs).

## Decision

**Deferred** — not implementing now. Kept open as a scoped feature with this note as the spec.

## Related

- Custom Tablet Configs page (installed/override configs) — sibling, not a duplicate.
- [otd_windows_configurations](../../) — installed OTD reads on-disk configs from the `Configurations`
  folder; the built-in catalog is embedded in the assembly.
