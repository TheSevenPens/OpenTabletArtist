# Pen page

*(Part of the [User Manual](USERMANUAL.md).)*

The **Pen** page's tabs are **movement**, **inputs**, and **pressure** — each its own section below. It shares the [Tablet page](TABLET.md)'s header (linked switcher + Refresh).

## movement

The tablet's **output mode**. Pick a movement mode — **Normal (Absolute)** maps the pen 1:1 to the screen (recommended for drawing) or **Mouse-like (Relative)** moves the cursor like a mouse (often for games) — and below both is a **Don't use Windows Ink** toggle *(Windows only)*. Windows Ink carries pressure & tilt, but Windows treats the pen like touch, so in some apps dragging the pen scrolls the page instead of selecting. Turning Windows Ink off switches to OpenTabletDriver's plain output, so the pen acts like a mouse — dragging selects text and objects — at the cost of pressure and tilt. The toggle is independent of the movement mode, so all four combinations are possible. While Windows Ink is off, Home shows an **informational** note (not a warning) that pressure/tilt are disabled. *(A tablet that lands on a non-Windows-Ink mode **without** this being an intentional choice still gets a warning + **Fix** to switch it back.)* Below the modes is a **Position smoothing** slider (moved here from the pressure tab, since it steadies the pen's on-screen position) — 0 = off to 1 = max, perceptually scaled; it applies while the pen is down and resets on lift.

## inputs

The pen's switches in three columns: on the **left**, the **tip** and **eraser** cards plus a **Pen input** card; in the **middle**, a diagram of the pen standing tip-down with its side buttons highlighted and **numbered** for the ones this pen has; on the **right**, a card per **barrel button** (numbered 3·2·1 top-to-bottom, to line up with the diagram). Each tip/eraser/button is a status card (#495): a green **"Adaptive Binding — recommended"** badge when it's on Adaptive Binding, or an amber "Not the recommended setting" + a **Use Adaptive** button when it's drifted onto anything else. Adaptive is the only supported choice — under Windows Ink (which OTA's output modes use) the other binding types don't work — so there's nothing else to pick. The **Pen input** card holds three opt-out toggles: **Disable pen tip** (#493 — clears the tip binding so tapping does nothing; your previous tip binding is stashed and restored when you turn it back off), **Disable pressure sensitivity** (#494 — a flat on/off contact that drops pressure entirely, so the pressure curve is then inert), and **Disable tilt** (stops any tilt being reported to your apps). A pen with no barrel buttons just shows the left column beside a plain pen.

## pressure

Shapes how hard you have to press — the pressure curve plus pressure smoothing (moved here from the Tablet page; this tab was formerly called *dynamics*). It's enforced by the bundled *OpenTabletArtist – Pen Dynamics* filter, so it affects **every** app (Krita, Clip Studio Paint, Photoshop, …), not just one. There's **no on/off switch** — it simply does nothing until you shape the curve or raise the smoothing slider (a linear curve with zero smoothing is a true no-op), and the filter stays enabled from then on. Edits are debounced and applied to the daemon automatically. The tab has:

- **Live pressure bar** — at the top, a dot for the raw incoming pressure and, when the curve or smoothing are shaping it, a second dot for the processed value (curve + smoothing, so it lags exactly as your apps receive it); both values read out to four decimals.
- **Pressure curve** — edited directly on the chart by dragging three nodes: the pink **min** and cyan **max** nodes set where pressure starts and saturates, and the amber **bend** node in the middle sets the response's **softness** — drag it **up** for a lighter, concave touch or **down** for a firmer, convex one (it replaces the old Softness slider). Beside the chart, a column of **preset thumbnails** (Soft · Linear · Hard) previews each shape as a mini graph — click one for a quick starting point (Linear resets the curve). While you draw, a green dot tracks your **live pen pressure** along the curve.
- **Pressure smoothing (jitter reduction)** — evens out pressure jitter (0 = off to 1 = max; perceptually scaled, like Slimy Scylla, so the slider feels even across its range). It runs after the curve, applies while the pen is down, and resets each time it lifts so strokes start crisp with no carry-over.

> **Disable pressure sensitivity** and **Disable tilt** live on the **inputs** tab; **Position smoothing** lives on the **movement** tab.
