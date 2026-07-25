# Scribble page

*(Part of the [User Manual](USERMANUAL.md).)*

A paint canvas for confirming the pen is working — draw with the pen and watch pressure, tilt, and twist live.

- **Tablet picker** — when more than one tablet is connected, a selector chooses which tablet this page (and the other single-tablet flows) acts on; hidden with a single tablet.

- **Dynamics indicators** — a row of chips spells out exactly what's altering the stroke — **Pressure curve** (the curve is bent, not linear), **Pressure smoothing**, and/or **Position smoothing** — so behavior changes are never a mystery. With everything at its default the row shows no chips (nothing is shaping the pen). (Edit the pressure curve + smoothing on the Pen page's **pressure** tab.)
- **Pointer-only warning** — *Pointer-only* Mode draws nothing, so active dynamics can't be seen. Picking it while dynamics is on shows a short warning — pick a pressure Mode to see them.
- **Input source** (toggle) — where both the position and the pressure/tilt come from:
  - **App** — the OS pointer (what a drawing app actually receives, via Windows Ink). The stroke renders under the pen.
  - **Driver** — the raw OTD daemon signal, before the Windows Ink output stage — so it works even when Windows Ink isn't delivering pointer events. The raw tablet position is mapped to the canvas through the active tablet's **Absolute** area mapping, so the stroke still lands under the pen. This needs an **Absolute output mode** (e.g. Windows Ink Absolute); in **Relative** mode there's no absolute position to map, so the canvas is disabled with a note.
- **Mode** — what to visualize: **Pressure Brush** (pressure → brush size), **Tilt Brush 1** (tilt azimuth → brush rotation), **Tilt Brush 2** (tilt altitude → brush size), **Barrel Rotation Brush** (twist → brush rotation), or **Crosshairs (No drawing)** (a crosshair, no drawing).
- **Readouts** — live values, with X/Y shown paired in one cell: **Canvas** (where the stroke lands), **Raw** (the source's raw coordinates — tablet units in Driver mode), pressure, **Tilt** X/Y, azimuth, altitude, twist, and hover.
- **Clearing** — the **Clear** button, or press **Delete** / **Backspace**.
