# Title bar — extend the client area under the caption (Option A)

> Status: **implemented (Option A).** The Sakura/Anime backdrop now reaches the top edge instead of a
> gray system caption bar, and the extra vertical strip is reclaimed. Option B (a fully custom themed
> title bar) is documented below as the future path if the residual Avalonia caveats become worth
> removing.

## Problem

The standard Windows title bar renders as an opaque gray system strip above our themed content. Two
issues:

1. **It clashes with the theme.** The Sakura/Anime skins paint a soft pink backdrop across the whole
   window, but the OS caption bar sat on top as a flat gray band — a hard, unthemed edge.
2. **It wastes vertical space.** A full system caption bar (~32px) on top of our own content is dead
   space in a window that already has a compact layout.

## Options considered

### Option A — extend the client area under the caption (**chosen**)

Tell the window to draw its own content all the way to the top edge, *under* the OS caption buttons,
so the themed backdrop is what shows behind the min/max/close glyphs. The buttons stay; the gray band
goes away.

- **Pros:** small, low-risk change; keeps the real OS caption buttons (correct hit-targets, snap
  layouts on hover, accessibility, DPI behaviour — all free); backdrop reaches the top edge; reclaims
  the separate caption strip.
- **Cons (Avalonia 12 limitations, see below):** the managed caption still shows the window **Title
  text** top-left and an extra **fullscreen (⤢) button** next to min/max/close. Neither is removable
  by styling in Avalonia 12. The title text duplicates the sidebar wordmark; the fullscreen button is
  a harmless-but-unwanted extra.

### Option B — fully custom themed title bar (deferred)

Go borderless (`WindowDecorations="None"`, as the calibration overlay already does) and draw our own
title bar: brand/wordmark on the left, our own min/max/close buttons styled to the theme on the right,
our own drag + double-click-maximize behaviour across the strip.

- **Pros:** total control — no duplicate title text, no fullscreen button, caption buttons themed to
  match Sakura/Anime, exact height. The cleanest possible result.
- **Cons:** we own everything the OS gave us for free — button glyphs and their hover/pressed/active
  states, correct light/dark/high-contrast rendering, Windows 11 snap-layout fly-outs on maximize
  hover, per-monitor DPI, RTL, and accessibility roles. More code and more surface area for
  cross-version rendering bugs. Only worth it if the two cosmetic caveats from Option A become a real
  irritation.

## Why not just restyle the managed caption? (the Avalonia 12 constraint)

The natural middle path — keep the extended client area but hide the title text and fullscreen button —
is **not available in Avalonia 12**:

- **`ExtendClientAreaChromeHints` was removed.** In earlier Avalonia this enum let you ask for
  "system chrome, no title, no fullscreen." It no longer exists. The surviving window properties are
  `ExtendClientAreaToDecorationsHint`, `ExtendClientAreaTitleBarHeightHint`,
  `IsExtendedIntoWindowDecorations`, `WindowDecorationMargin`, and `OffScreenMargin` — none of which
  toggle the title text or the fullscreen button.
- **The managed chrome types aren't stylable by selector.** `Avalonia.Controls.Chrome.TitleBar` and
  `CaptionButtons` are internal; a `Selector="TitleBar ..."` or `chrome|CaptionButtons ...` style
  fails to compile (`AVLN2000 Unable to resolve type`). So we can't hide `PART_FullScreenButton` or
  the title `TextBlock` the way we'd hide a normal control part.

Net: with the extended-client-area approach you take the managed caption as-is. Removing the title
text and fullscreen button means owning the whole bar — that's Option B.

## Implementation (Option A, as shipped)

All changes anchored with a `#titlebar` comment marker.

- **`MainWindow.axaml`** — on the `<Window>`:
  ```xml
  ExtendClientAreaToDecorationsHint="True"
  ExtendClientAreaTitleBarHeightHint="-1"
  ```
  `-1` lets the platform pick the natural caption height rather than forcing one.
- **Root `<Panel>` margin** bound to the window's `OffScreenMargin`:
  ```xml
  <Panel Margin="{Binding OffScreenMargin, RelativeSource={RelativeSource AncestorType=Window}}">
  ```
  When maximized, Windows over-sizes an extended-chrome window by the frame width; `OffScreenMargin`
  is non-zero only then and keeps content on-screen (verified: maximize no longer clips the edges).
- **Content top padding** raised to clear the caption buttons now that content reaches the top
  (`Padding="20,44,20,18"` on the content border).
- **Drag strip** (last child of the root panel): a transparent `Border` pinned to the top,
  `Height="34"`, `Margin="0,0,150,0"` so it stops short of the top-right and the native caption
  buttons still receive their clicks:
  ```xml
  <Border VerticalAlignment="Top" HorizontalAlignment="Stretch" Height="34" Margin="0,0,150,0"
          Background="Transparent" PointerPressed="OnTitleBarPressed" DoubleTapped="OnTitleBarDoubleTapped" />
  ```
- **`MainWindow.axaml.cs`** — `OnTitleBarPressed` calls `BeginMoveDrag(e)` on left-press;
  `OnTitleBarDoubleTapped` toggles `WindowState` between `Normal` and `Maximized` — standard
  title-bar behaviour, since our strip replaces the (now transparent) OS caption for dragging.

## Known caveats (accepted)

1. **Duplicate title text** — the managed caption shows `OpenTabletArtist  v0.28.0  BETA` top-left,
   overlapping the sidebar wordmark. Kept because the version label is genuinely useful and the OS
   also uses `Title` for the taskbar tooltip/entry. Removing it from the caption specifically requires
   Option B.
2. **Fullscreen (⤢) button** — an extra managed caption button we don't need. Not removable in
   Avalonia 12 (see above). Harmless; toggles borderless fullscreen.

Both disappear only under Option B. They were judged not worth that cost now, since Option A already
resolves the two problems that motivated the change (themed edge + reclaimed space).

## Decision

**Ship Option A.** Revisit **Option B** if the duplicate title text / fullscreen button become a real
annoyance, or if a future Avalonia release restores a supported way to trim the managed caption.

## Related

- Calibration overlay already uses `WindowDecorations="None"` — a working precedent for the borderless
  base Option B would build on.
- `MainWindow.axaml` / `MainWindow.axaml.cs` — search `#titlebar` for the change sites.
