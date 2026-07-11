# Spec: the coordinate-space model

> **Status: draft (2026-07-10).** The canonical model of every coordinate space a pen position passes through,
> the transform between each, and the invariants that keep them from being mixed. Mixing two of these spaces is
> the exact class of bug behind the macOS negative-origin calibration failure (#140/#517) and the ~1% HiDPI
> residual. Backlog item #2 in [specs-backlog.md](../specs-backlog.md).
>
> Where the code is *certain*, this is authoritative (formulas are quoted from the source). Where the code has
> an unresolved tension (points vs pixels vs scaling), it's flagged **⚠ OPEN** and must be settled by live
> measurement before this spec is "done."

> **Quick rule (the one to internalise).** OTD stored areas + `MapToDesktop` output = **space [3]**
> (min-shifted, top-left, +Y down). `DisplayInfo` = **space [4]** (raw OS coords, can be negative). The
> UI/overlay = **space [5]** (logical points). **Never subtract a [4] value from a [3] value without `−min`** —
> that single mix is #517. And the mapper output isn't the cursor coordinate a probe reads: a pointer shim sits
> after it (see *Grounding*).

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

## Origin & axis direction — where is (0,0), and which way is +Y?

The classic trap: pure-math convention is **bottom-left, +Y up**; screen/desktop convention is **top-left,
+Y down**. OTA's entire logical pipeline is **top-left, +Y down** — with exactly one exception, at the macOS
AppKit boundary.

| Space | Origin corner | +Y | Notes |
|---|---|---|---|
| [1] Raw tablet units | top-left | **down** | Digitizer convention; confirmed by the calibration report (top targets → small raw Y, bottom targets → large raw Y). |
| [2] Normalized −1..1 | **tablet centre** (0,0) | down | −1 = top edge, +1 = bottom edge. |
| *mm / tablet area / display area* | *(rectangle **centre**)* | down | `MappingArea.Position` is the *centre* of the area, expressed in its parent space. |
| [3] OTD virtual-desktop | top-left (min corner of all monitors) | down | The min-shift keeps a top-left origin. |
| [4] Raw OS display coords (`DisplayInfo`) | top-left | down | Avalonia normalises **both** Windows and macOS to top-left here. |
| [5] Avalonia logical points | top-left | down | |
| [6] Physical pixels | top-left | down | |
| **macOS AppKit** (`NSScreen.frame`, `NSWindow.setFrame:`) | **bottom-left** | **UP** | ⚠ The Cocoa flip. Lives **only** in `CoverFullDisplayOnMac`'s ObjC interop. |

**The macOS split, stated explicitly** (it's the confusing part): AppKit's *window/screen* APIs are
**bottom-left, +Y up**, but the *global event/cursor* coordinate the daemon actually warps the pointer into
(`CGWarpMouseCursorPosition`) and that our probes read (`CGEventGetLocation`) is the **flipped, top-left,
+Y down** space — same as everything else. So only the overlay's `NSScreen`/`NSWindow` interop is bottom-left;
the cursor pipeline is top-left throughout. Mixing the two Y-conventions at that ObjC boundary is a latent bug
class — it would misposition the overlay on a multi-display layout with unequal heights (a single full-screen
frame happens to hide it today).

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
5. **+Y is *down* in every space except the macOS AppKit interop (`NSScreen`/`NSWindow`), which is +Y *up*.**
   Flip Y when crossing that boundary; never hand an AppKit rect a Y value from an Avalonia/desktop rect (or
   vice-versa) without the flip. The flip stays contained in `CoverFullDisplayOnMac`.
6. **Calibration targets + the live dot normalize against the *same* origin as `MapToDesktop`'s output**
   (space [3]) — the one-sentence form of #517: a specialisation of #1/#3, but this is *the* failure mode.
   Regression test: `Capture_Succeeds_WhenMappedDisplaySitsAtShiftedOrigin` (encodes the worked example below).

## Common mistakes (each has bitten this codebase)

Grouped by *moving* between spaces, *talking* about them, and *coding* them. Every one maps to an invariant.

### Moving between spaces
- **Assuming the desktop origin is (0,0).** Subtracting a raw `DisplayInfo.X` (space 4) from a `MapToDesktop`
  point (space 3, min-shifted). *Symptom:* perfect on single / primary-at-origin layouts, wildly off the moment
  a monitor sits at a negative coordinate (live dot lands at X≈2.5). **This is #517.** → invariant #3: use the
  min-shifted origin (`MappedCenter`/`OutputOrigin`), never `Display.X`, for space-3 math.
- **Mixing points and pixels.** Comparing a logical-point value to a physical-pixel one (or forgetting
  `÷Scaling`). *Symptom:* a clean **≈2× / ≈1.6×** offset that grows with distance from centre — the HiDPI
  residual. → invariant #4 + OPEN-1: carry the unit with the value; don't re-derive scale per call.
- **Flipping — or not flipping — Y.** Treating an AppKit rect (`NSScreen`, bottom-left +Y up) as top-left
  +Y down, or vice-versa. *Symptom:* vertical position mirrored about the display centre; hidden on a single
  full-screen frame, visible on multi-display unequal heights. → invariant #5: flip only at the
  `CoverFullDisplayOnMac` boundary.
- **Treating an area's `Position` as a corner.** `MappingArea`/`Area.Position` is the **centre**, not top-left.
  *Symptom:* everything offset by half the area's width/height. → corner = `Position − size/2`.
- **Writing a `DisplayInfo` rect straight into the Output area without `MappedCenter`.** `ApplyToProfile` does
  the min-shift + centre correctly; an *ad-hoc* write that copies `DisplayInfo.{X,Y}` (space 4, raw) into
  `AbsoluteModeSettings.Display` (space 3, min-shifted centre) reintroduces #517. → always route through
  `MappedCenter`/`ApplyToProfile`.
- **Assuming normalized is 0..1.** It's **−1..1** (centre origin). *Symptom:* the calibration matrix's
  translation is 2× off, or a value lands in the wrong quadrant. (I made this exact slip earlier this session.)
  → `ToNormalized = raw/max·2 − 1`.

### Talking about them
- **"Screen / display / desktop coordinates" — unqualified.** Which of raw-OS (negative-capable), min-shifted
  virtual-desktop, logical points, or pixels? On macOS "desktop pixels" is itself ambiguous (points vs backing
  pixels). → name the numbered space ([3] vs [4] vs [5]).
- **"Normalized" without the range,** or **"the origin" without the corner + axis.** The thing the *Origin &
  axis direction* section exists to fix. → always state the range (−1..1 vs 0..1) and the corner + Y-direction.
- **Conflating "the display" with "the mapped area."** The output area may be a whole monitor (`Clean`), a
  sub-region (`Custom`), or spill off-screen (`OffScreen`). "Maps to display 2" hides which. → say which
  `DisplayMappingValidity`.

### Coding them
- **Not naming the space in the function's contract.** A `Vector2` in/out with no doc of its space → the next
  caller guesses. *This is how #517 got written.* → invariant #1: state the space in the doc-comment; ideally a
  distinct type per space so the compiler rejects a mix.
- **Deriving scale / origin ad hoc at each call site.** `DisplayInfo` has no `Scaling`, so the enumerator and
  the overlay each compute it — and **disagree** (OPEN-1). → compute once, carry it; consider putting `Scaling`
  on `DisplayInfo` (OPEN-4).
- **Testing only the happy path** (primary at (0,0), 1× scale, one monitor). The min-shift and HiDPI bugs are
  *invisible* there — #517's own reference tests used `minX=0`. → every coordinate test needs a negative-origin
  case, a HiDPI case, and a multi-monitor case.
- **Scaling a point as if it were a vector.** The affine's translation (`M31/M32`, the area centre) applies to
  *points*, not to deltas / sizes / directions. *Symptom:* offsets that appear or vanish when you (wrongly)
  transform a size or a difference. → transform points with the full affine; transform vectors with the linear
  part only.
- **Assuming `Displays.First()` is the primary monitor.** OTA's enumerator sorts primary-first, but OTD's
  macOS pointer offset uses `Displays.First()` in raw **CG enumeration order**, which isn't necessarily the
  primary. → don't assume ordinal 0 = primary; check `IsPrimary`.

## Worked example — the negative-origin layout (why #517 happened)

Displays: primary `#1 (0,0,1920,1080)`, target `#2 (0,1080,960,540)`, and `#3 (−1920,0,1920,1080)` →
`minX = −1920`.

- `MappedCenter(#2)` = `(0 − (−1920) + 480, 1080 − 0 + 270)` = **(2400, 1350)** → stored `Display.{X,Y}`.
- So `Display` area origin in space 3 = `(2400 − 480, 1350 − 270)` = **(1920, 1080)**.
- `MapToDesktop(pen at centre of #2)` ≈ **2400** (space 3).
- The bug: normalizing the live dot with `Display.X` (space 4 = **0**) gave `(2400 − 0)/960 = 2.5` — off-screen
  → `Near()` never matched. The fix normalizes with the space-3 origin (1920): `(2400 − 1920)/960 = 0.5`. ✓

## Grounding: what OTD + the tablet actually use (read from OTD's source)

Resolves the two *upstream* unknowns by reading the daemon (our submodule), not by inference.

### The tablet (HID report) → space [1]
`TabletReport(byte[])`, the default parser:
- `X = ushort @ byte 2`, `Y = ushort @ byte 4`, `Pressure = ushort @ byte 6` — little-endian **16-bit unsigned**,
  buttons in byte 1. So raw coordinates are **unsigned integers, one per axis, range `0..MaxX` / `0..MaxY`**,
  with MaxX/MaxY from the tablet's config (`DigitizerSpecifications`), ≤ 65535.
- **Origin top-left, +Y down** — the HID digitizer convention; OTD reads the wire value directly, **no flip**
  (matches our calibration data: top targets → small raw Y).
- **Per-tablet variation:** this is the *default* byte layout. Other tablets use other parsers
  (`TiltTabletReportParser`, per-tablet parsers via `ReportParserProvider`) — byte offsets, tilt/eraser/
  proximity, and report ID vary by device; the config's `MaxX/MaxY/Width/Height` define the range + physical
  size. (This is why our macOS stream showed `IntuosV3ExtendedReport`, not the base layout.)

### OTD's output space → space [3]
`AbsoluteOutputMode.CalculateTransformation` is **byte-for-byte what `AbsolutePositionMapper` mirrors**
(verified this session). Its output/config space is the **virtual screen** (`IVirtualScreen`):
- Built from the OS display list, **anchored at the min corner** of all monitors. `MacOSDisplay` shifts every
  display to **0-based** (`origin − offset`, virtual-screen `Position = (0,0)`); `WindowsDisplay` keeps each
  monitor at its **raw** position and sets virtual-screen `Position` to the min corner. → This *is* OTA's
  space-3 "min-shifted virtual-desktop," and it's why `MappedCenter` subtracts `min`.
- **Units = the OS native desktop unit: pixels on Windows** (`EnumDisplaySettings` `dmPels…`), **points on
  macOS** (`CGDisplayBounds`).
- **Origin top-left, +Y down on both** — Windows virtual desktop is top-left; macOS uses **`CGDisplayBounds`**
  (CoreGraphics *display* space, **top-left Y-down**), **not** `NSScreen.frame` (Cocoa, bottom-left Y-up). **So
  the daemon's output is top-left Y-down on macOS too** — confirming invariant #5: only the *overlay's*
  `NSScreen`/`NSWindow` interop is bottom-left; the cursor pipeline is top-left throughout.
- The area `Position` is the **centre** of the area (`Area.Position`), matching OTA's `MappingArea`.
- **The mapper output is *not* the raw cursor coordinate — a per-platform pointer shim sits after it.**
  `MapToDesktop`/`AbsoluteOutputMode` produce space [3]; then the OS pointer layer converts it:
  `MacOSAbsolutePointer` subtracts `primary.Position` (the *first* display in the min-shifted list) →
  `CGEventSetLocation` in CG-global (primary-relative, top-left, points); `WindowsAbsolutePointer` scales by
  `VirtualScreen.size / 65535` (the `SendInput` 0..65535 absolute range). **Consequence:** a probe reading
  `CGEventGetLocation` sees **space [3] − primary.Position**, not the mapper output directly — factor this into
  the OPEN-1 measurement.

### What this settles vs. leaves open
- **Settles:** the raw HID convention (top-left, +Y down, uint16, config-ranged); that OTD's output space = OTA's
  min-shifted space-3; that OTD's macOS output is top-left Y-down (`CGDisplayBounds`), so invariant #5 holds.
- **Narrows OPEN-1:** the *daemon* side is now known — **points on macOS, pixels on Windows**. The remaining
  unknown is only the *OTA* side: whether Avalonia's `Screen.Bounds` matches that unit and how `Scaling`
  reconciles.
- **Nuance to watch:** OTD **0-based-shifts displays on macOS but keeps raw (min-anchored) on Windows.** OTA's
  `min()` shift accommodates both, but a *Windows* layout with a monitor at a negative coordinate is the same
  negative-origin hazard #517 fixed on macOS — worth a Windows regression check.

## ⚠ OPEN items to resolve (before this spec is "done")

- **OPEN-1 — points vs pixels vs scaling (the HiDPI residual).** `DisplayInfo` carries **no scale field**, and
  the two readers disagree: the **enumerator** uses `Screen.Bounds` *directly* for `DisplayInfo`, while the
  **overlay** uses `Bounds / Scaling`. On the macOS test rig the enumerator reported **points** (960×540 for a
  1920-px panel) and things aligned — but that means either Avalonia's macOS `Bounds` is already points (so the
  overlay's `÷Scaling` would double-count) or the rig reported `Scaling = 1`. This is unverified and is the most
  likely root of the ~1% residual. **Resolve by measuring, per OS, the actual units of `Screen.Bounds`,
  `Screen.Scaling`, `DisplayInfo`, and the daemon's output space, and define the one canonical unit for space
  3/4 — then make the enumerator and overlay agree.** (Narrowed by *Grounding* above: the daemon side is now
  known — **points on macOS, pixels on Windows** — so only the OTA/Avalonia `Bounds`-vs-`Scaling` half is open.)
- **OPEN-2 — mixed-DPI monitors.** One scale factor per screen; a mapping that spans monitors of different
  scale has no single valid `÷Scaling`. Define behaviour (map is per-monitor anyway).
- **OPEN-3 — rotation + non-integer scaling** interactions with the above.
- **OPEN-4 — should `DisplayInfo` carry `Scaling`** so space-3/4/5 conversions are explicit rather than
  re-derived ad hoc at each call site? (Likely yes — it would let invariant #4 be checked in one place.)

## Verification hooks

The **calibration report is the coordinate-space oracle**: a uniform offset ⇒ overlay not at the space-3
origin; deltas that grow to one edge ⇒ inset/scaled drawable; ~2× deltas ⇒ a points-vs-pixels mix (OPEN-1). The
`tools/` probes that measured raw→cursor (§ macOS `dev-environment.md`) are the way to settle OPEN-1 live.

**Closing OPEN-1 (one measurement row).** For a single held physical point, log: `Screen.Bounds`,
`Screen.Scaling`, the `DisplayInfo` as enumerated, `MapToDesktop`'s output at that point, and the probe's
`CGEventGetLocation` — remembering the cursor read is **space [3] − `primary.Position`** (the pointer shim
above), not the mapper output. If `Screen.Bounds` already equals the daemon's *points*, the overlay's
`÷ Scaling` double-counts (that's the residual); if `Bounds` is *pixels*, the enumerator — which copies `Bounds`
into `DisplayInfo` **un-divided** — is the one carrying the wrong unit. One row decides which.
