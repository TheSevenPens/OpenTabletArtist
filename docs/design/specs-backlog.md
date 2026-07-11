# Specifications to write — complex behaviours + edge cases

> A backlog of OTA behaviours that are **multi-subsystem, edge-case-heavy, and currently under-specified** —
> the places where "fit and finish" actually means "pin down the intended behaviour first." Each entry is a
> *spec to write*, not a bug: the problem, why it's hard (what interacts), the edge cases to nail down, and the
> open questions. Grounded in the code as it stands (class names are pointers, not gospel).
>
> Ordered roughly by leverage. The first three are the tangle the user called out; they're deeply intertwined
> and probably want to be written together (or as one spec with parts).

---

## 1. Display mapping ↔ tablet area ↔ calibration — as one coupled system

**The problem.** Three settings are stored and edited semi-independently but are physically one chain: which
**monitor** the tablet maps to (`DisplayMappingApplier`), the tablet's **active area** (`AbsoluteModeSettings`),
and the **calibration** matrix (`CalibrationProfile`). Change any one and the others may silently become wrong.
There is no single spec that says how they relate, in what order they're derived, and what re-derives what.

**Why it's hard — what interacts.** `MappedCenter`/`CurrentlyMapped` place the area in OTD's *min-shifted
virtual-desktop* space; the calibration is fit *against a specific area + display* and carries a
`MappingFingerprint`; `ClassifyMapping` grades the result **Clean / Custom / OffScreen / None**. Editing the
area changes the fingerprint → calibration is now stale (`CalibrationProfile.IsStale`). Re-mapping to another
monitor changes both.

**Edge cases to pin down.**
- Aspect-ratio lock on vs off when the area or display changes.
- Area larger than the monitor (`OffScreen`) → the "mapped area partly off-screen" nag we saw live: what's the
  intended auto-fix vs. user choice?
- Sub-monitor `Custom` mappings vs whole-monitor `Clean` — which flows preserve which.
- When exactly is calibration auto-invalidated vs. kept with a "may be stale" hint (#147)?

**Open questions.** Is the area the source of truth and display/calibration derived, or are they peers? Should
"map to display X" be a first-class intent that re-derives area + re-prompts calibration, rather than three
edits?

---

## 2. The coordinate-space model (the foundational one)

**The problem.** OTA moves a point through **many coordinate spaces**, and mixing two of them is exactly the
class of bug that caused the macOS negative-origin calibration failures and the HiDPI confusion. There is no
canonical doc of *which space is used where* and *the transform between each*.

**The spaces (at least):** raw tablet units → digitizer-normalized (0..1) → millimetres → OTD *virtual-desktop*
(0-based, **min-shifted**, so a monitor left-of-primary lands at a positive offset) → **raw OS display coords**
(can be **negative**) → Avalonia **logical points** → **physical pixels** (HiDPI backing scale). `AbsolutePosition
Mapper`, `DisplayMappingApplier`, `CalibrationSolver`, and the overlay each live in specific ones.

**Why it's hard.** Negative-origin layouts, non-integer/HiDPI scaling, and points-vs-pixels differ per OS and
per display. The bugs are silent (things *almost* line up).

**Edge cases.** Negative desktop origin; mixed-DPI monitors; a display whose logical size ≠ pixel size;
rotation. **Deliverable:** one diagram + a table "value X is in space Y at boundary Z," and the invariant that
a given function's inputs/outputs are all in one named space.

---

## 3. "Environment" identity + remembered per-environment configuration

**The problem (the user's laptop scenario).** People move a laptop between desks/docks; monitors appear,
disappear, change resolution/scale, and get repositioned. OTA should **recognise a returning environment** and
restore the configuration it last used there — without a robust notion of "environment," it can't.

**Why it's hard.** What *is* an environment? A set of displays keyed how — by EDID/serial, connector, friendly
name, resolution, position? All of those change across dock/undock (a dock renames connectors; a projector
reuses a name; the same monitor comes back at a different resolution). Display hotplug fires mid-session
(`TabletsChanged`/display-changed), so this must be reactive, not just at startup.

**Edge cases.** Laptop-lid-closed (internal panel gone); same monitor at a new scale; two identical monitors;
a display removed *while it's the mapped one* (→ the tablet points at nothing); partial matches (2 of 3
displays return).

**Open questions.** Fingerprint definition + fuzzy-match tolerance; store N remembered environments or just
"last good"; auto-restore vs. prompt; interaction with per-app profiles and calibration (each environment may
need its own calibration). This likely wants a new persisted concept, not just a tweak.

---

## 4. Calibration lifecycle, validity, and the solver-model choice

**The problem.** Calibration has states (none / captured / applied / stale / failed), a `MappingFingerprint`
that decides staleness, three solver models (**affine / homography / grid**, with #486 currently *forcing*
affine even for 9/25-point capture), and a report that doubles as the accuracy oracle. The full lifecycle and
"when does a calibration stop being valid" isn't written down.

**Edge cases.** Resolution/scale change under a fixed mapping (calibration silently off — we saw ~1% residual);
display re-map; tablet re-detect; enable/disable without recapture; undo-last-point; the hold-to-average
timing. **Open question:** when should the app *offer to re-solve as grid/homography* vs. stay affine?

---

## 5. Profiles, multiple tablets, and per-app switching — precedence

**The problem.** Profiles are per-tablet; per-app switching (`ForegroundAppWatcher` → active app → profile)
swaps them live; multiple tablets can be present (we saw Movink + PTH-660, one "Not detected"). The precedence
and fallback rules aren't specified.

**Edge cases.** Active tablet disconnected (which profile shows / applies?); a per-app profile references a
tablet that's gone; two tablets, one active; app switches to one with no specific profile (→ default); a
profile's mapped display is absent. **Open questions:** what's the resolution order (app-specific → default →
none), and what does the UI show for a not-detected tablet's settings?

---

## 6. Daemon ownership + settings authority (who owns the truth)

**The problem.** OTA and a separately-installed OTD **share the same `settings.json`** and can clobber each
other; OTA classifies the daemon as **app-owned / external / unknown** and only installs its
`OpenTabletArtist.Dynamics` plugin into an app-owned daemon. Who is authoritative for settings, and when OTA
writes vs. the daemon, isn't specified.

**Edge cases.** External daemon → calibration/dynamics silently absent (no plugin); daemon restarted under OTA;
settings changed by the OTD UI while OTA is open; a version-skewed daemon; the plugin install/upgrade/uninstall
lifecycle. (Overlaps the macOS/Linux Phase-6 daemon questions — a shared spec.)

---

## 7. Health / "Needs attention" — the condition catalog + remediation

**The problem.** `Health.Evaluate` produces issues with severities (**Broken / Misconfigured /
Recommendation**) and remediation areas, each with a FIX action (winink, vmulti, driver.conflict, app.elevated,
daemon.foreign, mapped-area-off-screen, …). There's no single catalog of *every* condition, what triggers it,
its severity, its FIX, and how it clears.

**Edge cases.** Multiple simultaneous issues + ordering; an issue that can't auto-fix; a FIX that needs a
restart/permission; conditions that differ per-OS (the gated Windows-only ones); transient-vs-persistent. This
is prime UI fit-and-finish territory and wants an exhaustive table.

---

## 8. Output modes — semantics + availability

**The problem.** Absolute vs Relative, and (Windows) Windows-Ink vs OTD-native output, with Phase-0.3's
generalisation of detection. What each mode *means*, when it's available per OS, and the exact switching
behaviour (and what it does to area/calibration) isn't spec'd in one place.

**Edge cases.** Relative mode + calibration (does calibration even apply?); switching Absolute↔Relative and the
area/aspect implications; macOS/Linux native modes vs Windows Ink; the "Fix output mode" action vs. clicking a
mode.

---

## 9. Reconnection, hotplug, and live-refresh resilience

**The problem.** The app reacts to daemon connect/disconnect, tablet hotplug (`TabletsChanged`), and display
changes with generation-guarded reloads (`DataLoaded`, the "most recent load wins" logic). The intended
behaviour under rapid/overlapping events, and what UI state persists across a blip, isn't written down.

**Edge cases.** Daemon dies mid-calibration/mid-edit; tablet unplugged while its settings page is open; display
removed while the calibration overlay is up; a flurry of `TabletsChanged`; reconnect to a *different* daemon
(app-owned ↔ external). What's preserved, what's re-fetched, what's discarded?

---

## 10. Settings persistence — schema, versioning, migration, import/export

**The problem.** Settings/profiles/calibration are persisted (shared with OTD's format), with presets and
custom-config import/export. There's no spec for schema version, forward/backward compatibility, corrupt-file
recovery, defaults, or what a preset/export actually captures (does it include calibration? display mapping?).

**Edge cases.** Older/newer settings from another OTD/OTA; a preset made on a different display layout (→ ties
back to #1/#3); partial/corrupt settings; export→import across machines/OSes.

---

## How to use this backlog

- **Write #2 (coordinate spaces) first** — it's the shared vocabulary the others lean on, and it's the proven
  bug source.
- **#1 + #3 + #4 are one cluster** (mapping/area/calibration/environment) — spec them together; that's the
  user's laptop-roaming scenario end to end.
- **#7 (health catalog)** is the fastest UI-fit-and-finish win — it's mostly enumeration of existing behaviour.
- Each spec should end with a **truth table / state chart** and an **invariants** list; those are what catch the
  silent edge cases.
