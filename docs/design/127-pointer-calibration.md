# Design — Pointer calibration for pen displays (#127)

> Status: **approved & implemented**, then **extended past the original v1**. (Design reviewed before
> coding; decisions and implementation requirements below were folded in from that review.)
>
> **What shipped vs. this design.** This doc records the approved v1 — a 6-DOF **affine** 4-tap
> calibration. The implementation has since advanced beyond it and is now the source of truth:
> - **Corners (4 point)** fit a **perspective homography** (`CalibrationMath.SolveHomography`, #195),
>   which also corrects keystone/parallax; the affine is kept only to *read* legacy stores.
> - **Fine grid (9 point / 25 point)** fit a **per-node bilinear offset field**
>   (`CalibrationSolver.SolveGrid` / `CalibrationGrid`, #196) for localized distortion.
> - The correction runs on its own **Calibration tab** (three cards) with an On/Off toggle, **Undo last
>   point** (#458), a persisted **calibration report** of the recorded taps (#460), and a stale hint
>   when the mapping fingerprint changes (#147).
>
> The "Transform model", "UX flow", and "Scope / phasing" sections below have been updated to match;
> the rest is preserved as the original rationale. See also
> [ARCHITECTURE.md](../ARCHITECTURE.md) → *Pointer calibration*.

## Problem

On pen *displays* (Wacom Cintiq, Huion Kamvas, XP-Pen Artist, …) the point the user sees under
the nib and the point the OS thinks the pen is at can be offset/scaled/rotated slightly — parallax,
panel/digitizer mismatch, bezel geometry. Every OEM driver fixes this with an interactive
**4-corner calibration**: the user taps a dot near each corner, and the driver derives a correction.

OpenTabletDriver has no built-in interactive calibrator. Kuuuube's
[Tablet_Calibration](https://github.com/Kuuuube/Tablet_Calibration) plugin can apply a correction,
but only via **manual, iterative** sliders (X/Y offset + per-edge stretch) — no guided dot-tap flow.

**Goal:** an in-app interactive calibration — show 4 targets, capture where the pen actually is at
each, compute a correction transform, apply + persist it to the active tablet's profile.

## Background: how OTD maps the pen

Absolute mode maps **raw tablet units → screen pixels** with an affine matrix
(`AbsolutePositionMapper.CreateMatrix`, mirroring OTD): `rawUnits→mm → center on tablet area →
rotate → scale to display → translate into the virtual desktop`. Calibration must correct the
*physical* misalignment on top of whatever area mapping the user has chosen.

A pipeline filter at `PipelinePosition.PreTransform` sees `report.Position` in **raw tablet units**,
*before* that absolute transform. So a correction applied there composes cleanly: we nudge the raw
position so the existing absolute transform then lands it on the intended pixel.

## Coordinate spaces

| Space | Range | Where |
|---|---|---|
| Raw tablet units | `0..MaxX`, `0..MaxY` | `report.Position` at PreTransform |
| Normalized tablet | `-1..1` | calibration math (resolution-independent, matches Kuuuube/OTD) |
| Virtual-desktop px | OS pixels | calibration targets during the solve / what the user sees |
| Display-relative px | `0..Width`, `0..Height` | the persisted report's screen coordinates (#461) |

Normalization (per Kuuuube): `u = raw / max * 2 - 1`, inverse `raw = (u + 1) / 2 * max`.

### What's stored, and in which space

When a calibration is saved (`CalibrationProfile.Write`, into the profile's filter store) the persisted
coordinate data is:

- **The transform** — `Homography` / `Grid` / affine `M11…M32` payloads — in **normalized tablet space
  (`-1..1`)**. This is what the filter actually applies (normalize the raw report → transform →
  denormalize). The raw tap is *not* recoverable from it.
- **The report** (`Domain/CalibrationReport`), per recorded tap:
  - **target** and **measured** (pixel-equivalent of the raw tap) — in **display-relative px**
    (`0..Width`, `0..Height`), *not* virtual-desktop px, so they read naturally against the one display
    that was calibrated instead of carrying its desktop offset (#461). The overlay solves in
    virtual-desktop px, then `CalibrationViewModel.BuildReport` subtracts the display origin before
    storing.
  - **raw** — the **raw tablet digitizer units** (`0..MaxX`, `0..MaxY`) of the *averaged* tap. The report
    is the only place the raw tap survives.
  - the **sample count** averaged for the tap (not a coordinate). Individual samples are intentionally
    dropped — only their per-tap mean is kept.
- **Mapping fingerprint** — a staleness token (rounded input/output areas + display number), not meant to
  be read as coordinates.

The report retains enough (averaged raw + targets) to re-derive per-point error or even re-fit a
different model **without re-tapping** — but only while the mapping is unchanged, since the solve inputs
(digitizer maxima, input/output areas, display origin) live on the profile, not in the report.

## Transform model

All models work in **normalized-tablet space** (each axis `-1..1`, matching OTD/Kuuuube) so the
correction is resolution-independent. The filter normalizes each report, applies the model, and
denormalizes. The pure math lives in **`Domain/`** (`CalibrationMath`, `CalibrationSolver`) and is
unit-tested against known cases (identity, pure offset/scale/rotation, noisy fits, degenerate
rejection, round-trips). It is **source-shared** into the plugin so the daemon and the app compute
identically.

A stored calibration carries a `Model` tag selecting which of three it uses:

- **Affine** — a **2×3 affine** `u' = A·u` (6 DOF: scale, rotation, shear, translation), fit by least
  squares (normal equations, closed form). This was the v1 model; it is now written **only** by the
  legacy `CalibrationProfile.Write(...Matrix3x2...)` overload and read back for pre-#195 stores. The
  overlay no longer produces it.
- **Homography** *(the 4-point / Corners default, #195)* — a **projective homography** (`Homography`,
  8 DOF), fit by least squares over the 4 corner correspondences (`CalibrationMath.SolveHomography`).
  Corrects keystone / perspective parallax on top of offset/scale/rotation/shear — the real win for
  pen displays whose panel and digitizer planes are slightly non-parallel.
- **Grid** *(9-point 3×3 / 25-point 5×5, #196)* — a regular grid of **per-node offsets**
  (`CalibrationGrid`) applied by **bilinear interpolation**, where each node's offset is
  `expected − measured` in normalized space (`CalibrationSolver.SolveGrid`). Corrects *localized*
  distortion a single global transform can't. Modeled on BetterCalibrator.

Common to all: 4+ captured point-pairs are **over-determined** and solved by least squares (robust to
a slightly-imprecise tap; no external math lib). Degenerate input (collinear/duplicate taps, a
collapsed grid, a non-invertible system) is detected — via the normal-matrix determinant, Gaussian
pivoting, or a zero-area grid extent — and the capture is **rejected** with a "couldn't compute, try
again" message rather than applying garbage.

## What gets captured, and the target/expected pairing

For each of 4 on-screen **targets** `Pᵢ` (virtual-desktop px):

1. Read the **raw tablet position** reported when the user taps `Pᵢ`, via the daemon debug stream
   (`DaemonPenInputSource` — same source the Test tab's Driver mode uses). Raw is used (not the OS
   pointer) so calibration is independent of the Windows-Ink output stage and any existing error.
2. Compute the **expected raw** for `Pᵢ`: `expectedRawᵢ = CreateMatrix⁻¹ · Pᵢ` using the profile's
   current `AbsoluteModeSettings` (tablet area + display area) and the tablet digitizer spec — the
   exact inputs the Test tab already assembles in `TestViewModel.BuildMapping`.
3. Fit `A` so that `normalize(measuredRawᵢ) → normalize(expectedRawᵢ)` for all i.

The filter then applies `A` to every report's normalized position. Result: tapping the spot the user
*sees* produces the pixel they *expect*.

> Because `expectedRaw` derives from the **current** area mapping, calibration is tied to that
> mapping (true of OEM calibration too). Changing the Screen-Mapping area should prompt a
> recalibrate. v1: document it + invalidate (offer "recalibrate") when the area changes.

**Targets** sit **inset from the corners (~10%)**, not in the literal corners — corners are hard to
reach on a display and digitizer linearity is worst at the very edge. Inset taps give a better fit.

## Where the transform runs — recommendation + alternative

**Recommended (A): a new `CalibrationFilter` in the existing `OpenTabletArtist.Dynamics` plugin
assembly**, `PipelinePosition.PreTransform`. The installer already copies the whole assembly
(`PressurePluginInstaller`), so a second `[PluginName]` filter ("OpenTabletArtist - Calibration")
ships with **no new install/build path**. A `CalibrationProfile` service reads/writes the store on the
profile, mirroring `PressureCurveProfile` exactly, and orders it *before* the dynamics filter.

As shipped, the filter exposes as `[Property]`s: the six affine components `M11,M12,M21,M22,M31,M32`
(`Matrix3x2` convention; identity by default, used for legacy/affine stores), a `Model` selector
(`""`/`Affine` → the M-matrix, `Homography`, `Grid`), the `Homography` and `Grid` **CSV payloads**
(parsed once on set), a `Mapping Fingerprint` (opaque staleness token), and a `Report` (the recorded
taps, #460). `Consume` branches on `Model`; presence of the store is the enable/disable (`store.Enable`).

- **Pro:** calibration is *orthogonal* to area mapping — the user's chosen area stays untouched and
  visible in the Screen-Mapping tab; the correction is a separate, toggleable layer (how Wacom does
  it). Full affine (incl. shear).
- **Con:** depends on our plugin being loaded (already required for Dynamics).

**Alternative (B): bake the correction into the profile's tablet input `Area`** (shift/resize/rotate
it), no filter needed — works on stock OTD. **Rejected for v1** because it *overwrites* the
user-set area (the Screen-Mapping UI and calibration would fight over the same fields), can't
represent shear, and muddles "what area am I mapping" with "what's my correction." Worth revisiting
only if avoiding the plugin dependency becomes important.

## UX flow

*(As shipped. The v1 design put the entry on the Screen-Mapping tab; it now lives on a dedicated
**Calibration** tab — see [USERMANUAL.md](../USERMANUAL.md) → Calibration.)*

1. **Entry point:** the tablet's **Calibration** tab (Absolute mode only — Relative has no absolute
   position to correct, so the tab explains that instead). Three cards, one per density — **4 point**,
   **9 point**, **25 point** (`CalibrationModeChoice` → `CalibrationOptions`) — each with a **Start**
   button and a note on when to use it. The Start button is additionally gated on the tablet being
   connected (#177) and a host that can open the overlay. A status card shows the current state with an
   **On/Off toggle** (disable the correction *without clearing it*, to compare with/without) and a
   **Clear calibration** button. The active calibration's card is marked **In use**.
2. **Overlay:** a borderless, top-most window covering the **display the tablet is mapped to**
   (`DisplayEnumerator`). Dimmed background, instructions, **Cancel** (Esc) at all times. Coordinates
   are exposed normalized to the display (0..1) so target/live-dot placement is DPI-independent.
3. **Capture:** show one target at a time — Corners run TL → TR → BR → BL inset ~10%; Grid runs a
   row-major lattice. We accept a tap only when the live dot is within a hit radius of the active
   target, average ≥4 down-samples for stability, then advance. A live dot shows the uncorrected
   raw→screen point so the user sees the current error. **Undo last point** (#458) pops the most recent
   tap and re-arms that target.
4. **Result:** once every target is captured, fit the model (Corners → homography; Grid → offset grid).
   On success, apply it **temporarily** for a **preview/confirm** ("move the pen around — does the
   cursor track the nib?") with **Apply**, **Redo**, **Undo last point**, **Cancel**. Apply keeps it
   (in this app the preview already persisted, so Apply just closes); Cancel restores whatever
   calibration existed when the overlay opened. A degenerate fit → a **Failed** state with **Redo**.
   The recorded taps are saved as a **calibration report** (#460) shown back on the Calibration tab
   (target px + measured raw + sample count per point, with **Copy**).
5. **Reset:** the **Clear calibration** action removes the filter store (true return to identity).

## Multi-monitor

Calibration is per-display and per-profile, against the one display the tablet is Absolute-mapped to.
The overlay opens on that display; targets are in that display's pixels. (Spanning all displays is
out of scope — consistent with the Screen-Mapping "one display at a time" decision in #117.)

## Failure / edge handling

- **Relative / non-mappable mode** → Calibrate disabled with an explanatory note.
- **No daemon / no tablet detected** → disabled.
- **Degenerate taps** (collinear, duplicate, wild outlier) → reject, ask to redo.
- **Cancel mid-flow** → no change to the profile; debug stream stopped (reference-counted, like the
  Test/Diagnostics pages).
- **Pen lifts between targets** → fine; we only capture on a deliberate tap on the active target.

## Testing

*(As shipped — see `tests/OpenTabletArtist.Tests/Calibration*Tests.cs`.)*

- `CalibrationMath` (`CalibrationMathTests`): affine-from-points (identity, offset, scale, rotation,
  least-squares on noisy points), **homography** solve + `Project`, degenerate-input rejection, the
  small linear solver, and normalize/denormalize round-trip.
- `CalibrationSolver` (`CalibrationSolverTests`): the measured→expected composition lands taps near
  their targets; **grid** offsets and CSV round-trips (`CalibrationModelsTests` covers `Homography` /
  `CalibrationGrid` / `CalibrationReport` parse round-trips).
- `CalibrationProfile` read/write round-trip across all three models + type-name guard, `IsStale`
  fingerprint logic, and filter-order (before dynamics) (`CalibrationProfileTests`).
- Filter `Consume` applies the selected model and passes through when MaxX/MaxY unknown (mirrors the
  Dynamics filter's defensive pass-through).
- VM (`CalibrationViewModelTests`): Calibrate enabled only in Absolute mode; the capture state machine
  advances, undoes, and resets correctly; capture bypasses an existing calibration.
- The overlay window itself is thin (no heavy logic) — manual visual pass.

## Scope / phasing

- **v1 (this issue):** affine 4-tap calibration, filter + profile service, entry point + overlay,
  apply/redo/clear, Absolute-only, single mapped display. *(Shipped.)*
- **Shipped since v1:**
  - **Homography** (perspective/keystone) as the 4-point default (#195).
  - **Grid** calibration — 9-point (3×3) and 25-point (5×5) per-node bilinear correction (#196).
  - **Undo last point** during capture (#458).
  - **On/Off toggle** to compare with/without, without clearing.
  - **Calibration report** — recorded taps persisted with the calibration and shown on the tab (#460),
    each with the **pixel-equivalent** of the raw tap and a **fit-quality** summary (RMS/max pointing
    error corrected, plus an outlier flag for a misfired tap) (#461). Post-correction residual is ~0 by
    construction for the homography/grid models, so the report shows the *pre*-correction parallax.
  - **Stale hint** via a mapping fingerprint when the area mapping changes (#147).
  - Moved to a dedicated **Calibration** tab (from the Screen-Mapping tab).
- **Later:** calibration from a guided setup (#60); auto-recapture prompt on area change (today it's a
  passive stale hint); export/import calibration.

## Resolved decisions (design review, #146)

1. **Filter (A), not area-bake.** Orthogonal correction layer; keeps the user's area mapping intact.
2. **Full affine (6 DOF) for v1.** 4 inset taps → least squares. The confirm screen may show
   human-readable deltas ("offset ~2 px, scale ~1.01") derived from the matrix, without limiting the
   model.
3. **Raw-stream capture.** OS-pointer capture would fold in Windows Ink + any existing calibration
   error (circular).
4. **Coupling acceptable for v1** (OEM drivers behave the same). Store a small **mapping fingerprint**
   (hash of input/output area + display id) next to the affine so the UI can flag "calibration may be
   stale" without forcing a blind recalibrate.
5. **Same plugin assembly** (`OpenTabletArtist.Dynamics`) — zero new install path. Add a
   `CalibrationProfile.FilterTypeName` constant and extend the plugin type-name guard test.

## Implementation requirements (from review)

- **Inverse mapping (new):** `AbsolutePositionMapper` has no desktop→raw path. Add `TryInvertMatrix`
  + `MapFromDesktop` in `Domain/` (unit-tested) to compute `expectedRawᵢ`.
- **Filter order:** Calibration **before** Dynamics (both `PreTransform`) — correct the raw position
  first so position smoothing never smears an uncorrected error. Order deterministically when writing
  the filter stores (`CalibrationProfile.Write` / `PressureCurveProfile.Write`).
- **Capture with existing calibration bypassed:** while tapping, capture **pre-calibration** raw (or
  temporarily disable the calibration filter), so we don't calibrate on top of an old correction and
  "Clear" truly returns to identity.
- **Preview before Apply:** after computing `A`, apply it temporarily (in-memory / short-lived daemon
  push) for the "move the pen around" confirm step; persist only on Apply.
- **Tap acceptance:** pen-down pressure threshold, optional averaging of N raw reports, and a hit
  radius (screen px) around the active target so stray taps don't advance.
- **Overlay placement:** top-most borderless window on the mapped display (`DisplayEnumerator` +
  Avalonia placement). Manual-test on a real display tablet — Windows may need the overlay on the
  same output as the pen display for pen input to land cleanly.

## Testing (incl. review additions)

- `Domain` math: affine-from-points (identity, offset, scale, rotation, least-squares on noisy 4
  points), degenerate-input rejection, normalize/denormalize round-trip.
- **Inverse round-trip:** `raw → MapToDesktop → MapFromDesktop ≈ raw` for non-degenerate mappings.
- **Composition:** calibration ∘ `AbsolutePositionMapper` lands measured taps near their targets
  (synthetic point pairs).
- `CalibrationProfile` read/write round-trip (mirrors `PressureCurveProfileTests`) + type-name guard.
- Filter `Consume` applies the transform and passes through when MaxX/MaxY unknown.
- VM: Calibrate enabled only in Absolute mode; capture state machine advances/resets correctly.
