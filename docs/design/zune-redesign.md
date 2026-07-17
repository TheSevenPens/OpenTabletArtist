# Zune-style UX redesign (branch: `zune`)

Status: **draft / phase 0 planning.** A big-typography, panorama/pivot navigation model
inspired by the Zune UX (the precursor to Metro). Goal: take v1's UX "to the next level" —
more simplicity, less chrome, better use of horizontal space — **while keeping the Sakura skin
as the default** (Zune is a *layout* language, not a palette; the pink skin stays).

## Why this is tractable

The current codebase already leans Metro, so this is evolution, not a rewrite:

- **Type is already Zune-ish.** `TextBlock.PageTitle` = *Segoe UI Light, 28px* (Styles.axaml:458).
  Flat cards, no skeuomorphism, high-contrast text.
- **Palette is separated from layout.** Skins live in `Themes/Colors.axaml` (per-skin brush
  dictionaries) + runtime overrides in `ThemeViewModel.RefreshSkin`. Layout lives in
  `Themes/Styles.axaml` and the views, referencing `DynamicResource` brushes. The redesign
  touches **layout + type + motion + nav**, not the palette — so Sakura is untouched.
- **One shared sub-nav pattern.** `SettingsView`, `AdvancedView`, and the per-tablet detail
  page are all `ComplexHeader` + a vertical, data-driven `RadioButton` rail (`ItemsControl` over
  a `Tabs` list) + a `ContentControl` bound to `SelectedContent`. Replace that one pattern with a
  Zune **pivot** host and all three convert at once.

## Navigation inventory (today)

**Top-level (left sidebar):** `HOME · TABLET · SCRIBBLE · ABOUT · SETTINGS · ADVANCED`

- **TABLET** (single page + tablet-switcher dropdown), 12 sub-tabs:
  About · Pen Behavior · Pen Inputs · Pen Buttons · Pressure Dynamics · Position Dynamics ·
  Tilt Dynamics · Display Mapping · Active Area · Calibration · Tablet Buttons · Wheels
  (+ dev-only: Filters · JSON)
- **SETTINGS**, 8 tabs (+ gated): Presets · [Per-App Presets*] · Hotkeys · Startup · Shortcut ·
  Theme · Driver Cleanup · Dev Tools · [Developer*]   (* gated)
- **ADVANCED**, 7 tabs: Daemon · Windows Ink · Configs · Diagnostics · Console · Plugins ·
  VMulti Driver

~30 nav destinations total. Too many for a low-chrome design → **merge**.

## Target navigation model

**Desktop-Zune, not phone-panorama.** The Zune *desktop* software used a top horizontal wordmark
nav (`quickplay · collection · marketplace · social`) with **pivots** for sub-navigation — this
fits a resizable desktop window better than an infinite pan. Proposal:

- **Remove the left sidebar.** Top-level nav becomes a horizontal row of lowercase **wordmarks**;
  the active one is marked by **accent colour + weight** (no underline). Frees the full window width.
  *(An early mockup had a huge page title bleeding off the right edge — cut as redundant, since the
  active wordmark already names the section.)*
- **Sub-navigation = pivots** — big Segoe UI Light horizontal headers replacing the vertical rail;
  the active pivot uses the same accent+weight cue as the wordmarks (consistent at both levels).
- **Wide pages use columns.** Merged pages lay their (formerly separate) tabs out as horizontal
  panels, so the width earns its keep instead of a narrow column beside a rail.
- **Crisp square corners, flat fields.** Metro/Zune uses 90° corners everywhere (cards, buttons,
  toggles, sliders); only genuine status/data dots stay circular. Frosted glass + a soft shadow
  stay — that's the Sakura richness — but nothing is a rounded rectangle.

### Top-level nav (decided)

`home · tablet · scribble · settings · advanced · about` — lowercase wordmarks. **Presets** stays a
Settings pivot (#571); **Advanced** stays its own top-level section. **Scribble** (the pen-test
surface) is top-level — it was briefly dropped in an early mockup and restored.

### Proposed tablet merges (12 → 5 pivots)

| New pivot   | Absorbs                                          | Rationale |
|-------------|--------------------------------------------------|-----------|
| **about**   | About                                            | specs (unchanged) |
| **pen**     | Pen Behavior + Pen Inputs + Pen Buttons          | one pen diagram already unifies tip/eraser/buttons; output mode belongs with the pen |
| **dynamics**| Pressure + Position + Tilt Dynamics              | three panels across the width (curve · smoothing · position · tilt) |
| **mapping** | Display Mapping + Active Area + Calibration      | "where the tablet maps & how accurately" — a sub-pivot (display · area · calibrate) |
| **controls**| Tablet Buttons + Wheels                          | physical controls on the tablet body |

(Filters · JSON stay dev-gated, behind the pivot when Developer is on.)

### Proposed settings merges (8 → ~4)

`presets` (+ per-app) · `hotkeys` · `appearance` (theme) · `system` (startup + shortcut + driver
cleanup) · `developer` (dev tools + developer, gated)

### Proposed advanced merges (7 → ~5)

`daemon` (daemon + console log) · `drivers` (windows ink + vmulti) · `configs` · `diagnostics` ·
`plugins`

## Phase 0 — prep / cleanup (no visible change; pure enablement)

1. **Extract one pivot host.** Replace the repeated `ComplexHeader` + vertical rail + content-host
   in SettingsView / AdvancedView / tablet-detail with a single reusable data-driven control
   (title + pivot items + transitioning content). Implement the Zune pivot once.
2. **Data-drive the top-level nav.** MainWindow.axaml hard-codes HOME/TABLET/SETTINGS/ADVANCED as
   RadioButtons + one `ItemsControl` for leaves. Generalize to a single ordered nav-section list
   (label · page · visibility · selection) so the shell can swap sidebar → top-bar without
   touching each node. (Extends the existing `NavLeaves` idea to everything.)
3. **Define the Zune type ramp.** Name a small display scale (all Segoe UI Light for headings) and
   route PageTitle / TabTitle / SectionLabel through it; add a "bleed-off-edge" oversized-title
   control.
4. **Add a motion layer.** Wrap content hosts in `TransitioningContentControl` with a shared
   slide+fade page transition (Zune easing). Pick the primitives now.
5. **Make every subpage embeddable.** Subpages must render headerless and host-agnostic (no
   "I'm a top-level page" assumptions). Precedent already set when Presets/Per-App/Developer were
   folded into Settings tabs (#571/#572) — audit the rest.
6. **Composite-page scaffolding.** Merged pages (pen · dynamics · mapping) host several existing
   sub-VMs at once; verify no lifecycle/singleton assumptions block co-hosting (e.g. the
   Test page's Activate/Deactivate on navigation).
7. **Horizontal-panel primitives.** A wide-panels container (wrap/scroll) so merged pages use the
   width instead of a narrow column.

## Phase 0 status (done)

Committed on `zune`, each invisible/behaviour-preserving, build + 604 tests green:

- **0.1** — shared `Controls/TabbedPageView` backs SETTINGS + ADVANCED (the rail seam). ✔
- **0.2** — the whole top-level nav is one data-driven `NavSections` list. ✔
- **0.4** — named Zune type ramp (`TypeDisplaySize`…) + a shared `PageFade` crossfade primitive
  (applied to the tabbed-page content host). ✔
- **0.3 — deferred to Phase 2 (deliberately).** The per-tablet page can't fold into `TabbedPageView`:
  its 15 tabs are **inline XAML sections** switched by `RadioButton.IsChecked` ElementName bindings,
  not content VMs, and its code-behind ties the active tab to live pen-input sampling, deep-links,
  and the focused Pen-Dynamics dialog. Data-driving that rail forces ripping out ElementName
  switching and rerouting the live-input lifecycle — a large, high-regression rewrite with **zero
  visible payoff** until the pivot exists. So the tablet rail gets data-driven **together with the
  pivot in Phase 2**, where the risk is justified and tested against the real UI. (Settings/Advanced
  already share the host, so the pivot swap covers them regardless.)

## Phase 1–2 status (done)

Committed on `zune`, verified live (build + 601 tests green):

- **Phase 1** — the left sidebar is gone; the top-level nav is a horizontal **wordmark bar**
  (lowercase, accent+weight active), content full-width below. ✔
- **Phase 2a** — `TabbedPageView`'s rail is now a horizontal **pivot** (big lowercase Segoe UI Light);
  editing that one control converted SETTINGS + ADVANCED at once. ✔
- **Phase 2b** — `CompositeSectionViewModel`/`View` stacks existing sub-VMs, cutting pivot counts:
  SETTINGS 8→6 (System = Startup+Shortcut+Driver Cleanup; Theme→Appearance), ADVANCED 7→5
  (Daemon+Console; Drivers = Windows Ink+VMulti). Deep-links remapped. ✔
- **Polish** — top-level page switches crossfade (same `PageFade`); wordmark bar + pivots wrap on
  narrow widths and fit at the 800px minimum; dropped the duplicate brand eyebrow (the OS caption
  carries the app name); removed the now-dead `NavNode` style + `TabbedPageView.Title`. ✔
- **Phase 2c — the tablet page — done.** The **12→5 merges** landed first (four commits: mapping,
  dynamics, controls, pen) — the pivots are `about · pen · dynamics · mapping · controls`, each folding
  its old sections into one scroller with in-place headers. Then the **vertical rail was replaced with
  the same horizontal Zune pivot** the other pages use (PivotTab theme, accent+weight active cue, wraps
  on narrow widths); the grid went `[Auto,*]×[Auto,*]` → rows `[Auto,Auto,*]` so content spans full
  width. Pragmatic call on the deferred 0.3: kept the ElementName model rather than extracting all the
  inline sections into views — the pivot is still RadioButtons with the same x:Names, content still
  gates on `#XTab.IsChecked`, and the code-behind (live pen-input stream, deep-links, DynamicsOnly
  focused editor, screenshot sweep) reads those names unchanged. All verified live, 601 tests green.
  *Nice-to-have not done:* pivot switches swap instantly (no crossfade) — the shared `PageFade` would
  need a single content host, which the inline-section approach doesn't have.

## Phasing (PR sequence)

- **Phase 0** — refactor/enable. Invisible; keeps `master` behavior. **Done.**
- **Phase 1** — top-bar/wordmark nav, sidebar removed. **Done.**
- **Phase 2** — pivots + merges. **Done** — Settings/Advanced (2a/2b) and the tablet page (2c: merged 12→5 + rail→horizontal pivot).
- **Phase 3** — typography + motion polish. **Done.** Top-level page + pivot/section transitions all
  crossfade with the shared `PageFade`; the tablet pivot content also crossfades now (overlap +
  opacity, since it has no data-driven content host — `BoolToOpacityConverter`). Hero empty-states: the
  TABLET page's "no tablet" placeholder is a boxless big light-weight headline over the backdrop; home's
  inline empty state lightened to match. The type ramp (display/title/body sizes + `DisplayFontFamily`)
  is applied consistently across wordmarks, pivots, and section titles.
- **Phase 4** — Sakura-on-Zune tuning. **Done / inherent.** Petals render behind all content
  (`SakuraPetals` sits under the nav+content grid), and the boxless hero lets them drift behind the
  headline — the intended panorama. Contrast holds: the light-weight big text (wordmarks, pivots, hero)
  is top-anchored over the light-pink region; the strong pink→orange gradient is at the window bottom
  where content sits on frosted cards. Accent (sakura pink) is used consistently for active nav/pivots,
  primary buttons, and highlights. The rich Sakura default is unchanged (users love it) — the Zune
  layout was tuned to work *with* it, not restyle it.

## Decisions (resolved)

1. **Nav model:** desktop-Zune top wordmark bar + pivots. ✔
2. **Sidebar:** removed entirely; reclaim full width. ✔
3. **Presets:** stays under Settings (#571). ✔
4. **Advanced:** stays a top-level section. ✔
5. **Tablet merges:** about / pen / dynamics / mapping / controls. ✔
6. **Wordmarks:** lowercase. ✔
7. **Corners:** square (Metro); frosted glass + soft shadow kept. ✔
8. **No giant page title:** dropped; active wordmark names the section. ✔

## Feature → pivot inventory (completeness guard)

The merges regroup where things live; **nothing is removed**. This table maps every current control
to its new home, so nothing is lost when 12/8/7 tabs collapse into pivots. Modal flows (calibration
capture overlay, report dialog, supported-tablets dialog) are overlays — unaffected by nav — and
carry over as-is.

### tablet  (12 tabs → 5 pivots)

| Pivot | From (today) | Controls carried over |
|-------|--------------|-----------------------|
| **about** | About | full spec readout; Resources → View supported tablets |
| **pen** | Pen Behavior · Pen Inputs · Pen Buttons | output mode (Normal/Absolute · Mouse-like/Relative); **Don't use Windows Ink** toggle *(Win)*; tip + eraser Adaptive status cards + Use Adaptive; **Disable pen tip**; barrel-button Adaptive cards over the pen diagram (hidden when the pen has none) |
| **dynamics** | Pressure · Position · Tilt Dynamics | live-pressure bar; **Binary pressure**; pressure curve (min/max nodes, Softness, Presets Linear/Soft/Firm, Reset, live dot, Live in/out readout); **Cut below input minimum** *(dev-gated, #569)*; pressure smoothing; position smoothing; **Disable tilt**; per-tab Reset + status line |
| **mapping** | Display Mapping · Active Area · Calibration | display diagram (click-to-select, Apply mapping, Refresh, Display Settings, off-screen/custom flags); active-area **interactive diagram** (drag-move, drag-corner resize), **Rotation None/90/180/270**, Size slider, Maximize, mm/inches, usage stats; calibration 4/9/25-point cards + Start, active-cal status (Correction On/Off, Clear), **View report** dialog, full-screen capture overlay (Undo last / Redo all) |
| **controls** | Tablet Buttons · Wheels | aux-button cards, binding type (None/Keyboard/Mouse button/Mouse scroll), live-press highlight, **Buttons enabled** master, **Clear all**; wheel/dial bindings |
| *(dev)* filters · json | Filters · JSON | shown behind the pivot only when Developer is enabled |

Header (device switcher dropdown · connection status · Refresh · Forget) sits under the pivots.
**mapping is the densest merge** — built out in full in the mockup to prove the layout holds; if it
ever feels too tall it drops to a `display · area · calibrate` sub-pivot (fallback, not needed now).

### settings  (8 tabs → 5 pivots)

| Pivot | From (today) | Controls carried over |
|-------|--------------|-----------------------|
| **presets** | Presets (+ Per-App*) | Current settings (Save as preset, Browse); preset cards (Load/Update/Duplicate/Rename/Delete); *Per-App mappings + snapshot pickers + foreign-daemon guard, shown only when the feature flag is on* |
| **hotkeys** | Hotkeys | Cycle mapped monitor (assign/clear); per-preset hotkey assignment |
| **appearance** | Theme | theme selector (System/Light/Dark/Sakura/Custom); Falling petals + opacity; Colours & translucency (highlight/card/left-pane swatch pickers, card + left-pane opacity, Reset); Custom look (base colour, background image) |
| **system** | Startup · Shortcut · Driver Cleanup | Start-with-Windows *(Win)*; Start-menu shortcut *(Win)*; conflicting-driver scan/remove *(Win)* |
| **developer** *(gated)* | Dev Tools · Developer | Show-Developer-tab toggle; Developer keeps its own inner sub-nav (Warnings · Config errors · Tablet page extras · Screenshot · Window size · Calibration I/O) |

### advanced  (7 tabs → 5 pivots)

| Pivot | From (today) | Controls carried over |
|-------|--------------|-----------------------|
| **daemon** | Daemon · Console | daemon info, Refresh, bundled version, OTD UX launcher, Start/Stop/Restart; live console log |
| **drivers** | Windows Ink · VMulti Driver | plugin + driver install/status |
| **configs** | Configs | config-folder list (View/Delete), Refresh, Open Folder |
| **diagnostics** | Diagnostics | collect diagnostics report |
| **plugins** | Plugins | plugin list, enable/disable |

### top-level (unchanged pages)

`home` (Needs attention + Not-connected card + Your tablets + Supported tablets) · `scribble`
(pen-test surface + dynamics indicator) · `about` (what-is + version + Help/Discord + Resources).

### Risks flagged

- **mapping density** — biggest; mitigated by the wide multi-section page (built), sub-pivot as a
  fallback.
- **pen / dynamics** — three tabs each; fit as columns, pen diagram already unifies tip/eraser/buttons.
- **Developer's own sub-tabs** — kept as an inner rail inside the `developer` pivot (nested nav, one
  level, as today).
- **OS-gated & feature-gated pivots** — `system` items are Windows-only; `developer` and Per-App are
  gated. The pivot host must hide pivots per the same rules the current rails use (already modelled by
  `SettingsTabItem.IsVisible`).
