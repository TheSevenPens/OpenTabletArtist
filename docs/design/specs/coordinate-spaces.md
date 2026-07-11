# Spec: the coordinate-space model

> **Status: draft (2026-07-10).** The canonical model of every coordinate space a pen position passes through,
> the transform between each, and the invariants that keep them from being mixed. Mixing two of these spaces is
> the exact class of bug behind the macOS negative-origin calibration failure (#140/#517) and the ~1% HiDPI
> residual. Backlog item #2 in [specs-backlog.md](../specs-backlog.md).
>
> Where the code is *certain*, this is authoritative (formulas are quoted from the source). Where the code has
> an unresolved tension (points vs pixels vs scaling), it's flagged **⚠ OPEN** and must be settled by live
> measurement before this spec is "done."

## The pipeline (one pen sample, end to end)

```
 pen on glass
     │  daemon reads HID
     ▼
[1] RAW TABLET UNITS         (0..MaxX, 0..MaxY; ints)
     │  CalibrationFilter (PreTransform), if calibrated:
     │     n = ToNormalized(raw);  n' = M · n;  raw' = FromNormalized(n')
     ▼
[2] NORMALIZED TABLET (-1..1)   ← the calibration matrix lives HERE
     │  (back to raw' units)
     ▼
[1] RAW TABLET UNITS (corrected)
     │  AbsoluteOutputMode / AbsolutePositionMapper.CreateMatrix:
     │     raw→mm → centre on input area → rotate → scale to output area → +output centre
     ▼
[3] OTD VIRTUAL-DESKTOP        (0-based, MIN-SHIFTED; OS desktop unit)
     │  cursor placement (OS API)
     ▼
[4] RAW OS DISPLAY COORDS      (per-monitor rects; CAN BE NEGATIVE)
     │  ÷ Scaling  (HiDPI)   ⚠ see OPEN-1
     ▼
[5] AVALONIA LOGICAL POINTS    ← overlay windows, target rings live HERE
     │  × Scaling
     ▼
[6] PHYSICAL PIXELS
```

## The spaces

| # | Space | Definition | Unit / range | Where it's used (code) |
|---|---|---|---|---|
| 1 | **Raw tablet units** | Digitizer report coordinates. | `0..MaxX`, `0..MaxY` (ints; Movink ≈ 59552×33848) | `DeviceReport.Position`; `PenSample.RawX/RawY`; inputs to everything below. |
| 2 | **Normalized tablet** | `raw/max·2−1`. Tablet **centre = (0,0)**. | **−1..1** | `CalibrationMath.ToNormalized/FromNormalized`; **the calibration matrix `M` operates only here** (`CalibrationFilter`). |
| — | *Millimetres* | `raw · (Width_mm / MaxX)`. Physical size. | mm | Internal intermediate inside `AbsolutePositionMapper.CreateMatrix`. |
| — | *Tablet area* | Active sub-rectangle, **centre-positioned**. | mm (`MappingArea`) | `AbsoluteModeSettings.Tablet` → `input` in the mapper. |
| — | *Display/output area* | Target rectangle, **centre-positioned**, in space [3]. | space [3] units (`MappingArea`) | `AbsoluteModeSettings.Display` → `output` in the mapper. |
| 3 | **OTD virtual-desktop** | The daemon's output space: origin at the **min corner of all monitors** (0-based, min-shifted), so a monitor left-of-primary lands at a **positive** offset. | OS desktop unit (⚠ OPEN-1); can exceed one monitor | Output of `AbsolutePositionMapper.MapToDesktop`; `DisplayMappingApplier.MappedCenter/CurrentlyMapped`. |
| 4 | **Raw OS display coords** | The OS's native monitor rectangles, as the enumerator reports them. **May be negative** (monitor left of / above primary). | pixels on Windows / points on macOS (⚠ OPEN-1) | `DisplayInfo.X/Y/Width/Height` (from Avalonia `Screen.Bounds`). |
| 5 | **Avalonia logical points** | The UI coordinate space. | points (= [4] ÷ `Screen.Scaling`) | Overlay window `Position`/`Bounds`; calibration targets + live dot. |
| 6 | **Physical pixels** | Actual pixels. | pixels (= [5] × `Scaling`) | Backing store; not used directly by app logic. |

## The transforms (quoted from source)

**Raw ↔ normalized (space 1 ↔ 2)** — `CalibrationMath`:
```
ToNormalized(raw)   = (raw.X/maxX·2 − 1,  raw.Y/maxY·2 − 1)     // → −1..1
FromNormalized(n)   = ((n.X+1)/2·maxX,    (n.Y+1)/2·maxY)
```

**Calibration correction (applied in space 2, PreTransform)** — `CalibrationFilter.Consume`:
```
n  = ToNormalized(report.Position)
n' = Vector2.Transform(n, M)            // M is the fitted affine, in normalized space
report.Position = FromNormalized(n')    // hands corrected RAW units to the output stage
```
So calibration runs **before** the absolute transform and never leaves normalized space. `M`'s translation
terms (`M31/M32`) are therefore in **normalized** units — a common place to mis-scale.

**Raw → virtual-desktop (space 1 → 3)** — `AbsolutePositionMapper.CreateMatrix` (mirrors OTD's
`AbsoluteOutputMode` byte-for-byte):
```
res  = Scale(digitizer.Width/MaxX, digitizer.Height/MaxY)   // raw → mm
res *= Translate(-input.CenterX, -input.CenterY)            // centre on tablet area
res *= Rotate(-input.Rotation)
res *= Scale(output.Width/input.Width, output.Height/input.Height)  // mm → output units
res *= Translate(output.CenterX, output.CenterY)            // into virtual-desktop (space 3)
```
`MapFromDesktop` is its inverse (used by the solver + calibration targets).

**Raw OS coords → virtual-desktop (space 4 → 3)** — the **min-shift**, `DisplayMappingApplier.MappedCenter`:
```
minX = min(d.X for all displays);  minY = min(d.Y for all displays)
mappedCentre = (display.X − minX + display.Width/2,  display.Y − minY + display.Height/2)
```
So the stored **Display area is a *centre* in min-shifted space**, and `CurrentlyMapped` matches a monitor by
comparing the stored centre to `MappedCenter(d)`. `ApplyToProfile` writes `Display.{X,Y}` = this centre,
`Display.{Width,Height}` = the raw monitor size.

**OS coords ↔ logical points (space 4 ↔ 5)** — `÷ Screen.Scaling` / `× Screen.Scaling` (overlay
`PlaceOnDisplay`). ⚠ see OPEN-1.

## Invariants (the rules that prevent the bugs)

1. **One space per function boundary.** A function's inputs and outputs are all in a single named space; the
   space is stated in the doc-comment. (Regression source: `CalibrationViewModel` mixed a space-4 origin
   (`_ctx.Display.X`) with a space-3 point (`MapToDesktop`) → the negative-origin capture failure, #517.)
2. **The calibration matrix is space-2 only.** Never apply `M` to raw or desktop coordinates.
3. **Anything in virtual-desktop (3) is min-shifted; anything from `DisplayInfo` (4) is raw.** To go between
   them, always apply `−min` / `+min` — never assume the desktop origin is (0,0). Use
   `MappedCenter`/`OutputOrigin`, not `Display.X`, for space-3 math.
4. **The daemon's output unit must equal `DisplayInfo`'s unit on each OS.** If space 3 and space 4 disagree by
   the backing scale, the pen drifts proportionally from centre — the ~1% residual. **⚠ OPEN-1.**

## Worked example — the negative-origin layout (why #517 happened)

Displays: primary `#1 (0,0,1920,1080)`, target `#2 (0,1080,960,540)`, and `#3 (−1920,0,1920,1080)` →
`minX = −1920`.

- `MappedCenter(#2)` = `(0 − (−1920) + 480, 1080 − 0 + 270)` = **(2400, 1350)** → stored `Display.{X,Y}`.
- So `Display` area origin in space 3 = `(2400 − 480, 1350 − 270)` = **(1920, 1080)**.
- `MapToDesktop(pen at centre of #2)` ≈ **2400** (space 3).
- The bug: normalizing the live dot with `Display.X` (space 4 = **0**) gave `(2400 − 0)/960 = 2.5` — off-screen
  → `Near()` never matched. The fix normalizes with the space-3 origin (1920): `(2400 − 1920)/960 = 0.5`. ✓

## ⚠ OPEN items to resolve (before this spec is "done")

- **OPEN-1 — points vs pixels vs scaling (the HiDPI residual).** `DisplayInfo` carries **no scale field**, and
  the two readers disagree: the **enumerator** uses `Screen.Bounds` *directly* for `DisplayInfo`, while the
  **overlay** uses `Bounds / Scaling`. On the macOS test rig the enumerator reported **points** (960×540 for a
  1920-px panel) and things aligned — but that means either Avalonia's macOS `Bounds` is already points (so the
  overlay's `÷Scaling` would double-count) or the rig reported `Scaling = 1`. This is unverified and is the most
  likely root of the ~1% residual. **Resolve by measuring, per OS, the actual units of `Screen.Bounds`,
  `Screen.Scaling`, `DisplayInfo`, and the daemon's output space, and define the one canonical unit for space
  3/4 — then make the enumerator and overlay agree.**
- **OPEN-2 — mixed-DPI monitors.** One scale factor per screen; a mapping that spans monitors of different
  scale has no single valid `÷Scaling`. Define behaviour (map is per-monitor anyway).
- **OPEN-3 — rotation + non-integer scaling** interactions with the above.
- **OPEN-4 — should `DisplayInfo` carry `Scaling`** so space-3/4/5 conversions are explicit rather than
  re-derived ad hoc at each call site? (Likely yes — it would let invariant #4 be checked in one place.)

## Verification hooks

The **calibration report is the coordinate-space oracle**: a uniform offset ⇒ overlay not at the space-3
origin; deltas that grow to one edge ⇒ inset/scaled drawable; ~2× deltas ⇒ a points-vs-pixels mix (OPEN-1). The
`tools/` probes that measured raw→cursor (§ macOS `dev-environment.md`) are the way to settle OPEN-1 live.
