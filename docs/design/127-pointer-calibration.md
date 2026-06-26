# Design — Pointer calibration for pen displays (#127)

> Status: **proposal, pre-implementation.** Seeking review before writing code.

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
| Virtual-desktop px | OS pixels | calibration targets / what the user sees |

Normalization (per Kuuuube): `u = raw / max * 2 - 1`, inverse `raw = (u + 1) / 2 * max`.

## Transform model

Use a **2×3 affine** in normalized-tablet space: `u' = A·u` where
`A = [[a, b, tx], [c, d, ty]]` (6 DOF: scale, rotation, shear, translation).

- 4 captured point-pairs → **over-determined** (8 equations, 6 unknowns) → solve by **least squares**
  (normal equations; closed form, no external math lib). Robust to a slightly-imprecise tap.
- Affine covers the real-world cases (offset + non-uniform scale + small rotation; shear absorbs
  minor skew). **Perspective/homography** (keystone) is deliberately **out of scope** for v1 — it's
  rarely needed for flat panels and adds nonlinear solving; noted as a future extension.
- Degenerate input (collinear/duplicate taps) → detected via the normal-matrix determinant →
  reject with a "couldn't compute, try again" message rather than apply garbage.

The pure math (`build affine from point pairs`, `apply`) lives in **`Domain/`** and is unit-tested
against known cases (identity, pure offset, pure scale, rotation, a noisy 4-point fit).

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

**Recommended (A): a new `CalibrationFilter` in the existing `OtdWindowsHelper.Dynamics` plugin
assembly**, `PipelinePosition.PreTransform`, exposing the 6 affine values as `[Property]`s
(`A11,A12,A21,A22,Tx,Ty`) plus an `Enabled`-style presence. The installer already copies the whole
assembly (`PressurePluginInstaller`), so a second `[PluginName]` filter ("OTD Windows Helper -
Calibration") ships with **no new install/build path**. A `CalibrationProfile` service reads/writes
the store on the profile, mirroring `PressureCurveProfile` exactly.

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

1. **Entry point:** a **Calibrate** button on the Screen-Mapping tab (next to Display Settings),
   enabled only in an **Absolute** output mode (Relative has no absolute position to correct — show
   the same kind of note the Test tab uses). Also reachable later from a guided-setup flow (#60).
2. **Overlay:** a borderless, top-most window covering the **display the tablet is mapped to**
   (from `AbsoluteModeSettings.Display` / `DisplayEnumerator`). Dimmed background, instructions, a
   **Cancel** (Esc) at all times.
3. **Capture:** show one target at a time (TL → TR → BR → BL), each a crosshair/ring. The user rests
   the nib on it and taps; we capture the raw position on pen-down (optionally average a few samples
   for stability), advance, and mark it done. A live dot shows the current raw→screen point so the
   user sees the uncorrected error.
4. **Result:** after 4 taps, compute `A`. Show a **preview/confirm** ("move the pen around — does the
   cursor track the nib?") with **Apply**, **Redo**, **Cancel**. Apply persists to the profile +
   pushes to the daemon (debounced apply path we already have).
5. **Reset:** a **Clear calibration** action (removes/identity-resets the filter store).

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

- `Domain` math: affine-from-points (identity, offset, scale, rotation, least-squares on noisy 4
  points), degenerate-input rejection, normalize/denormalize round-trip. (pure unit tests)
- `CalibrationProfile` read/write round-trip (mirrors `PressureCurveProfileTests`).
- Filter `Consume` applies the transform and passes through when MaxX/MaxY unknown (mirrors the
  Dynamics filter's defensive pass-through).
- VM: Calibrate enabled only in Absolute mode; capture state machine advances and resets correctly.
- The overlay window itself is thin (no heavy logic) — manual visual pass.

## Scope / phasing

- **v1 (this issue):** affine 4-tap calibration, filter + profile service, Screen-Mapping entry
  point + overlay, apply/redo/clear, Absolute-only, single mapped display.
- **Later:** homography/keystone; calibration from a guided setup (#60); auto-invalidate on area
  change; export/import calibration.

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
5. **Same plugin assembly** (`OtdWindowsHelper.Dynamics`) — zero new install path. Add a
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
