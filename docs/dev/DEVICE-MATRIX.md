# Manual device matrix

Automated tests cannot prove any of this. Every item here needs a real tablet, a real display
arrangement, or a real drawing application — the failures are in hardware behaviour and OS integration,
which is exactly where a unit test has nothing to say. Tracked as part of the redesign gates (#741).

Work through this before a release, and before merging anything that touches mapping, pressure,
output modes, or the daemon lifecycle. Record the result in the release PR: what you ran, on what, and
what happened.

## How to record a run

Copy the table into the PR and fill it in. "Not tested" is a legitimate and useful answer — an untested
row is information; a row silently assumed to work is not.

| Area | Tablet | OS | Result | Notes |
|---|---|---|---|---|
| | | | | |

## Pressure

- [ ] Pressure reaches the drawing app at all — a stroke varies in width or opacity with pen force.
- [ ] The full range is usable: a light stroke is faint, a hard stroke is full-strength, and neither end
      is clipped. A curve edit in Pen Dynamics changes the result in the direction you'd expect.
- [ ] Pen tip click still works as a click outside the canvas (menus, buttons).
- [ ] Tilt, on a tablet that reports it.
- [ ] Eraser end, on a pen that has one.

## Reconnect and sleep

The daemon and the app disagree about device state most easily here, and the symptom is a tablet that
looks connected in the UI and does nothing.

- [ ] Unplug and replug the tablet while the app is open — it disappears and comes back, and the pen works.
- [ ] Sleep and resume the machine with the tablet attached.
- [ ] Sleep and resume with the tablet unplugged, then plug it in.
- [ ] Restart the daemon from the Daemon page while a tablet is connected.
- [ ] Close the app to tray, unplug, replug, reopen.
- [ ] After each of the above: the active area and output mode are still what you set, not defaults.

## Monitor changes and scaling

- [ ] Change the primary display while the app is open.
- [ ] Unplug a second monitor the tablet was mapped to; the mapping lands somewhere sane and says so.
- [ ] Reattach it; the mapping can be restored.
- [ ] Change display scaling (100% / 150% / 200%) — the mapped area still matches where the pen lands.
- [ ] Rotate a display.
- [ ] A mixed-DPI arrangement, if you have one.
- [ ] An ultrawide, if you have one (#654).

## Representative art applications

Pressure and pen behaviour are negotiated per-application; passing in one proves little about another.
Cover at least one from each column.

| Windows Ink path | Direct / other |
|---|---|
| Clip Studio Paint | Krita (with and without Windows Ink) |
| Photoshop | Blender |
| Fresh Paint / Whiteboard (a quick smoke) | GIMP |

- [ ] A stroke with visible pressure variation in each app tested.
- [ ] Pen buttons do what they are bound to.
- [ ] No cursor offset between where the pen is and where the stroke lands.

## Platform

- [ ] Windows: the shipping platform, and the only one where VMulti and Windows Ink apply.
- [ ] macOS: the port's own matrix lives in `docs/design/macos/`. Platform support is claimed from
      end-to-end verification, not from a build that compiled.
- [ ] Linux: builds in CI; treat any support claim as unverified until a run is recorded here.
