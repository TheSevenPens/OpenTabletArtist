using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Controls;

/// <summary>
/// The whole display-mapping picture in one view (#250/#252): the connected displays across the top
/// (click to select — Windows-Display-Settings style), the tablet's full + effective area below, and
/// red corner-to-corner lines from the effective area up to the selected display (Wacom-style, so the
/// 1:1 correspondence between the active area and the screen is obvious).
/// </summary>
public sealed class ScreenMappingDiagram : Control
{
    /// <summary>Every colour the diagram draws with, mixed once per render from the theme's accent and ink.
    ///
    /// Nothing here is a fixed value, and that is the point. The diagram used to be half themed and half
    /// hardcoded: the beams and the active-area outline read <c>AccentBrush</c>, while the selected display
    /// was nailed to #6366F1 — which is the Light theme's own accent. On any other skin the beams changed
    /// colour and the box they pointed at did not, so pink beams arrived at an indigo screen. A hardcoded
    /// #22C55E bottom beam and cool fixed greys added two more hue families on top.
    ///
    /// So the accent now marks only the mapping — the selected display's border, the active area's outline
    /// and all three beams — and everything else is ink at a low alpha. Selection is carried by the border,
    /// the glow and a slightly heavier fill rather than by a saturated block.</summary>
    private readonly record struct Palette(
        IBrush SelFill, IPen SelBorder, BoxShadows Glow,
        IBrush UnselFill, IPen UnselBorder,
        IBrush Text, IBrush SubText, IBrush EffFill)
    {
        public static Palette From(Color accent, Color ink) => new(
            SelFill: DiagramDrawing.Neutral(ink, 0x21),
            SelBorder: new Pen(new SolidColorBrush(accent), 2),
            Glow: new BoxShadows(new BoxShadow
            {
                OffsetX = 0, OffsetY = 0, Blur = 16, Spread = 1,
                Color = Color.FromArgb(0x73, accent.R, accent.G, accent.B),
            }),
            UnselFill: DiagramDrawing.Neutral(ink, 0x0D),
            UnselBorder: new Pen(DiagramDrawing.Neutral(ink, 0x24), 1),
            Text: DiagramDrawing.Neutral(ink, 0xF0),
            SubText: DiagramDrawing.Neutral(ink, 0xA6),
            // Kept white rather than ink: this is a lightened *hole* in the tablet body, so it has to
            // read brighter than the neutral around it in every theme.
            EffFill: new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF)));
    }

    private readonly List<(DisplayInfo Display, Rect Box)> _hitRects = new();

    public static readonly StyledProperty<IReadOnlyList<DisplayInfo>?> DisplaysProperty =
        AvaloniaProperty.Register<ScreenMappingDiagram, IReadOnlyList<DisplayInfo>?>(nameof(Displays));
    public static readonly StyledProperty<int?> SelectedNumberProperty =
        AvaloniaProperty.Register<ScreenMappingDiagram, int?>(nameof(SelectedNumber), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<TabletAreaInfo?> AreaProperty =
        AvaloniaProperty.Register<ScreenMappingDiagram, TabletAreaInfo?>(nameof(Area));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<ScreenMappingDiagram, IBrush?>(nameof(AccentBrush));
    // The stored output rectangle + how it grades, so an off-screen/custom mapping is drawn (overlaid on
    // the monitors), not just described in the note above (#mapping-visual).
    public static readonly StyledProperty<MappedOutputArea?> MappedOutputProperty =
        AvaloniaProperty.Register<ScreenMappingDiagram, MappedOutputArea?>(nameof(MappedOutput));
    public static readonly StyledProperty<DisplayMappingValidity> MappingValidityProperty =
        AvaloniaProperty.Register<ScreenMappingDiagram, DisplayMappingValidity>(nameof(MappingValidity));
    public static readonly StyledProperty<IBrush?> WarningBrushProperty =
        AvaloniaProperty.Register<ScreenMappingDiagram, IBrush?>(nameof(WarningBrush));
    // The theme's text colour, which every neutral in the diagram is mixed from (see Palette).
    public static readonly StyledProperty<IBrush?> InkBrushProperty =
        AvaloniaProperty.Register<ScreenMappingDiagram, IBrush?>(nameof(InkBrush));

    public IReadOnlyList<DisplayInfo>? Displays { get => GetValue(DisplaysProperty); set => SetValue(DisplaysProperty, value); }
    public int? SelectedNumber { get => GetValue(SelectedNumberProperty); set => SetValue(SelectedNumberProperty, value); }
    public TabletAreaInfo? Area { get => GetValue(AreaProperty); set => SetValue(AreaProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public MappedOutputArea? MappedOutput { get => GetValue(MappedOutputProperty); set => SetValue(MappedOutputProperty, value); }
    public DisplayMappingValidity MappingValidity { get => GetValue(MappingValidityProperty); set => SetValue(MappingValidityProperty, value); }
    public IBrush? WarningBrush { get => GetValue(WarningBrushProperty); set => SetValue(WarningBrushProperty, value); }
    public IBrush? InkBrush { get => GetValue(InkBrushProperty); set => SetValue(InkBrushProperty, value); }

    static ScreenMappingDiagram()
    {
        AffectsRender<ScreenMappingDiagram>(DisplaysProperty, SelectedNumberProperty, AreaProperty,
            AccentBrushProperty, MappedOutputProperty, MappingValidityProperty, WarningBrushProperty,
            InkBrushProperty);
        AffectsMeasure<ScreenMappingDiagram>(DisplaysProperty, AreaProperty, MappedOutputProperty,
            MappingValidityProperty);
    }

    private static readonly Color FallbackWarning = Color.FromRgb(0xE0, 0x8A, 0x1E);

    public ScreenMappingDiagram()
    {
        // Keyboard-operable (#603): the diagram is the main in-page way to choose a display, so it must be
        // reachable and drivable without a mouse. Focusable → tab-stop + a focus adorner; arrow keys move
        // the selection like a radio group.
        Focusable = true;
        UpdateAutomationName();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width;
        return new Size(w, 320);
    }

    public override void Render(DrawingContext ctx)
    {
        _hitRects.Clear();
        var accent = (AccentBrush as ISolidColorBrush)?.Color ?? DiagramDrawing.FallbackAccent;
        var accentBrush = new SolidColorBrush(accent);
        var ink = (InkBrush as ISolidColorBrush)?.Color ?? DiagramDrawing.FallbackInk;
        var pal = Palette.From(accent, ink);

        var displays = Displays;
        if (displays == null || displays.Count == 0)
        {
            DiagramDrawing.DrawCentered(ctx, new Rect(Bounds.Size), "No displays detected", 13, pal.SubText);
            return;
        }

        const double pad = 20;
        var inner = new Rect(Bounds.Size).Deflate(pad);
        if (inner.Width <= 0 || inner.Height <= 0) return;

        // Top ~52% = displays, bottom ~30% = tablet, the gap between holds the connector. The tablet
        // region sits flush to the inner bottom (only the outer `pad` below it) — no extra cushion, so
        // the drawn tablet ends near the control's bottom edge instead of leaving dead space.
        var dispRegion = new Rect(inner.X, inner.Y, inner.Width, inner.Height * 0.52);
        double tabH = inner.Height * 0.30;
        var tabRegion = new Rect(inner.X, inner.Bottom - tabH, inner.Width, tabH);

        // ── Displays ──
        double minX = displays.Min(d => d.X), minY = displays.Min(d => d.Y);
        double monW = displays.Max(d => d.X + d.Width) - minX, monH = displays.Max(d => d.Y + d.Height) - minY;
        if (monW <= 0 || monH <= 0) return;

        // When the stored mapping is off-screen/custom, overlay its output rectangle (0-based desktop
        // coords — same origin as the monitors) so the problem is shown, not just described. Expand the
        // fitted viewbox to include it, so an off-screen area is visibly seen spilling past the monitors.
        var overlay = MappedOutput;
        bool showOverlay = overlay is not null && (MappingValidity == DisplayMappingValidity.OffScreen
                                                   || MappingValidity == DisplayMappingValidity.Custom);
        double vbMinX = 0, vbMinY = 0, vbMaxX = monW, vbMaxY = monH;
        if (showOverlay && overlay is { } ov0)
        {
            vbMinX = Math.Min(vbMinX, ov0.Left);
            vbMinY = Math.Min(vbMinY, ov0.Top);
            vbMaxX = Math.Max(vbMaxX, ov0.Left + ov0.Width);
            vbMaxY = Math.Max(vbMaxY, ov0.Top + ov0.Height);
        }
        double vbW = vbMaxX - vbMinX, vbH = vbMaxY - vbMinY;
        if (vbW <= 0 || vbH <= 0) return;
        double dScale = Math.Min(dispRegion.Width / vbW, dispRegion.Height / vbH);
        // Project a rectangle given in 0-based desktop coords (monitor origin) into the fitted region.
        double dBaseX = dispRegion.X + (dispRegion.Width - vbW * dScale) / 2 - vbMinX * dScale;
        double dBaseY = dispRegion.Y + (dispRegion.Height - vbH * dScale) / 2 - vbMinY * dScale;
        Rect Project0(double left0, double top0, double w, double h) =>
            new(dBaseX + left0 * dScale, dBaseY + top0 * dScale, w * dScale, h * dScale);

        Rect? selectedBox = null;
        DisplayInfo? selDisplay = null;
        foreach (var d in displays)
        {
            // No inset: adjacent monitors touch, matching how Windows Display Settings and OTD draw them
            // (a gap would misrepresent the real desktop layout).
            var box = Project0(d.X - minX, d.Y - minY, d.Width, d.Height);
            if (box.Width <= 1 || box.Height <= 1) continue;
            _hitRects.Add((d, box));
            if (SelectedNumber == d.Number) { selectedBox = box; selDisplay = d; continue; } // drawn last
            ctx.DrawRectangle(pal.UnselFill, pal.UnselBorder, box);
            DrawDisplayLabels(ctx, box, d, pal);
        }
        // (The selected display is drawn later — after the connector beams — so it sits above them.)

        // ── Tablet (full + effective area) — rotation-aware so a turned tablet reads the same here as on
        //    the Active Area tab (#199): the full outline is drawn turned as physically held (portrait for
        //    90°/270°), while the effective area stays upright. ──
        var area = Area;
        double fullW = area?.FullWidth ?? 16, fullH = area?.FullHeight ?? 10;
        double rot = area != null ? (((area.Rotation % 360) + 360) % 360) : 0;
        bool perp = Math.Abs(rot % 180) > 0.5;
        double rotRad = rot * Math.PI / 180.0;

        double tabBoxW = Math.Min(tabRegion.Width * 0.5, tabRegion.Height * 2.4);
        var tabBox = new Rect(tabRegion.X + (tabRegion.Width - tabBoxW) / 2, tabRegion.Y, tabBoxW, tabRegion.Height);

        // Fit the tablet's (rotated) bounding box, then centre the un-rotated outline and turn it.
        double bboxW = perp ? fullH : fullW, bboxH = perp ? fullW : fullH;
        var fitBox = DiagramDrawing.FitAspect(bboxW, bboxH, tabBox);
        double tScale = bboxH > 0 ? fitBox.Height / bboxH : 1;
        var tCenter = fitBox.Center;
        var fullRect = new Rect(tCenter.X - fullW * tScale / 2, tCenter.Y - fullH * tScale / 2,
                                fullW * tScale, fullH * tScale);
        var (tabFill, tabBorder) = DiagramDrawing.Tablet(ink);
        DiagramDrawing.DrawRotatedOutline(ctx, fullRect, tCenter, rotRad, tabFill, tabBorder);

        // Effective area — upright, positioned within the (possibly turned) tablet (mirrors the Active
        // Area diagram's TabletToScreen mapping).
        Rect effRect = fullRect;
        if (area != null && area.FullWidth > 0 && area.FullHeight > 0)
        {
            double dx = (area.EffCenterX - fullW / 2) * tScale, dy = (area.EffCenterY - fullH / 2) * tScale;
            double cs = Math.Cos(-rotRad), sn = Math.Sin(-rotRad);
            var ec = new Point(tCenter.X + dx * cs - dy * sn, tCenter.Y + dx * sn + dy * cs);
            double ew = Math.Max(2, area.EffWidth * tScale), eh = Math.Max(2, area.EffHeight * tScale);
            effRect = new Rect(ec.X - ew / 2, ec.Y - eh / 2, ew, eh);
            ctx.DrawRectangle(pal.EffFill, new Pen(accentBrush, 1.5), effRect);
        }

        // ── Connector: two gradient "beams" mapping like edges together — the active area's LEFT edge to
        //    the display's LEFT edge, and RIGHT edge to RIGHT edge (the two side faces of the frustum), so
        //    the left↔left / right↔right correspondence is obvious. Brighter at each box, fading across the
        //    gap; the selected display is drawn afterwards so it sits above the beams. ──
        if (selectedBox is { } selBox)
        {
            void Beam(IBrush brush, Point a1, Point a2, Point b2, Point b1)
            {
                var geo = new StreamGeometry();
                using (var gc = geo.Open())
                {
                    gc.BeginFigure(a1, isFilled: true);
                    gc.LineTo(a2);
                    gc.LineTo(b2);
                    gc.LineTo(b1);
                    gc.EndFigure(true);
                }
                ctx.DrawGeometry(brush, null, geo);
            }

            // A directional beam fade: strong at the active-area end, fading to nearly nothing at the display
            // end. `start`/`end` are absolute points projected into the quad's bounds → relative gradient
            // coords, so the direction is exact regardless of the beam's shape.
            LinearGradientBrush BeamBrush(Point start, Point end, Point[] quad, Color color)
            {
                double bl = quad.Min(p => p.X), bt = quad.Min(p => p.Y);
                double bw = Math.Max(1e-6, quad.Max(p => p.X) - bl), bh = Math.Max(1e-6, quad.Max(p => p.Y) - bt);
                RelativePoint Rel(Point p) => new(( p.X - bl) / bw, (p.Y - bt) / bh, RelativeUnit.Relative);
                return new LinearGradientBrush
                {
                    StartPoint = Rel(start),
                    EndPoint = Rel(end),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0x00, color.R, color.G, color.B), 0.0),
                        // 0x60, down from 0x9C. The beams used to be read against saturated display
                        // boxes, which held them in place; against the tinted neutrals they now sit on
                        // they were the loudest thing in the diagram, and the three of them overlap, so
                        // the alpha compounded into a solid wedge where they crossed.
                        new GradientStop(Color.FromArgb(0x60, color.R, color.G, color.B), 1.0),
                    }
                };
            }

            // Left beam (left edge → left edge): gradient from the active area's left-mid (Q) to the
            // display's left-mid (H).
            var leftQuad = new[] { selBox.TopLeft, selBox.BottomLeft, effRect.BottomLeft, effRect.TopLeft };
            Beam(BeamBrush(new Point(effRect.Left, effRect.Center.Y), new Point(selBox.Left, selBox.Center.Y), leftQuad, accent),
                 selBox.TopLeft, selBox.BottomLeft, effRect.BottomLeft, effRect.TopLeft);

            // Right beam (right edge → right edge): gradient from the active area's right-mid (M) to the
            // display's right-mid (D).
            var rightQuad = new[] { selBox.TopRight, selBox.BottomRight, effRect.BottomRight, effRect.TopRight };
            Beam(BeamBrush(new Point(effRect.Right, effRect.Center.Y), new Point(selBox.Right, selBox.Center.Y), rightQuad, accent),
                 selBox.TopRight, selBox.BottomRight, effRect.BottomRight, effRect.TopRight);

            // Bottom beam (P N E G — active area's bottom edge → display's bottom edge): from the active
            // area's bottom-mid (O) to the display's bottom-mid (F). It used to be green, which made a third
            // hue out of what is the same relationship as the other two; the bottom edge is already the only
            // beam meeting the tablet along its width, so geometry alone distinguishes it.
            var bottomQuad = new[] { effRect.BottomLeft, effRect.BottomRight, selBox.BottomRight, selBox.BottomLeft };
            Beam(BeamBrush(new Point(effRect.Center.X, effRect.Bottom), new Point(selBox.Center.X, selBox.Bottom), bottomQuad, accent),
                 effRect.BottomLeft, effRect.BottomRight, selBox.BottomRight, selBox.BottomLeft);
        }

        // The selected display, drawn last so it sits above the connector beams.
        if (selectedBox is { } sbx && selDisplay is { } sdd)
        {
            ctx.DrawRectangle(pal.SelFill, pal.SelBorder, new RoundedRect(sbx), pal.Glow);
            DrawDisplayLabels(ctx, sbx, sdd, pal);
        }

        // ── Problem overlay: the stored output rectangle over the monitors, so an off-screen mapping is
        //    seen spilling into dead space (solid warning fill) and a custom/multi-display one is seen not
        //    lining up with a single monitor (dashed warning outline). Drawn on top so the issue reads. ──
        if (showOverlay && overlay is { } ov)
        {
            var warnColor = (WarningBrush as ISolidColorBrush)?.Color ?? FallbackWarning;
            var oRect = Project0(ov.Left, ov.Top, ov.Width, ov.Height);
            if (MappingValidity == DisplayMappingValidity.OffScreen)
            {
                var fill = new SolidColorBrush(Color.FromArgb(0x38, warnColor.R, warnColor.G, warnColor.B));
                ctx.DrawRectangle(fill, new Pen(new SolidColorBrush(warnColor), 2), oRect);
            }
            else // Custom — fully on-screen but not a single whole monitor.
            {
                var pen = new Pen(new SolidColorBrush(warnColor), 1.75)
                { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
                ctx.DrawRectangle(null, pen, oRect);
            }
        }

        // TEMP (gradient-direction scratch): label the 8 points of the selected display (A–H) and the
        // active area (J–Q), clockwise from each box's top-left, so we can name a gradient's begin/end.
        // Commented out for now — re-enable both calls (and DrawCornerLabels stays below) when tuning beams.
        // if (selectedBox is { } dispBox)
        //     DrawCornerLabels(ctx, dispBox, new[] { "A", "B", "C", "D", "E", "F", "G", "H" });
        // DrawCornerLabels(ctx, effRect, new[] { "J", "K", "L", "M", "N", "O", "P", "Q" });
    }

    // TEMP scratch helper (remove with the labels above): draw a letter chip at each corner + edge-midpoint
    // of an axis-aligned box, clockwise from the top-left.
    private void DrawCornerLabels(DrawingContext ctx, Rect r, string[] labels)
    {
        var pts = new[]
        {
            r.TopLeft,                              // 0 top-left
            new Point(r.Center.X, r.Top),           // 1 top-middle
            r.TopRight,                             // 2 top-right
            new Point(r.Right, r.Center.Y),         // 3 right-middle
            r.BottomRight,                          // 4 bottom-right
            new Point(r.Center.X, r.Bottom),        // 5 bottom-middle
            r.BottomLeft,                           // 6 bottom-left
            new Point(r.Left, r.Center.Y),          // 7 left-middle
        };
        var chip = new SolidColorBrush(Color.FromArgb(0xE0, 0, 0, 0));
        for (int i = 0; i < pts.Length && i < labels.Length; i++)
        {
            var ft = DiagramDrawing.Text(labels[i], 12, Brushes.White);
            var p = pts[i];
            var bg = new Rect(p.X - ft.Width / 2 - 3, p.Y - ft.Height / 2 - 1, ft.Width + 6, ft.Height + 2);
            ctx.DrawRectangle(chip, null, bg, 3, 3);
            ctx.DrawText(ft, new Point(p.X - ft.Width / 2, p.Y - ft.Height / 2));
        }
    }

    // No selected/unselected split in the text any more: with the selected display a tinted neutral
    // rather than a saturated fill, both boxes take the same ink.
    private void DrawDisplayLabels(DrawingContext ctx, Rect box, DisplayInfo d, Palette pal)
    {
        double numSize = Math.Clamp(Math.Min(box.Height * 0.34, box.Width * 0.4), 12, 30);
        var num = DiagramDrawing.Text(d.Number.ToString(), numSize, pal.Text);
        var subBrush = pal.SubText;
        bool roomy = box.Height > numSize + 24 && box.Width > 70;
        // Number + a "Primary" marker only; resolution/refresh and port live in the per-display list
        // below the diagram, so the boxes stay uncluttered (#570).
        var res = roomy && d.IsPrimary ? DiagramDrawing.Text("Primary", 10, subBrush) : null;

        double totalH = num.Height + (res != null ? res.Height + 1 : 0);
        double y = box.Y + (box.Height - totalH) / 2, cx = box.Center.X;
        ctx.DrawText(num, new Point(cx - num.Width / 2, y));
        if (res != null) ctx.DrawText(res, new Point(cx - res.Width / 2, y + num.Height + 1));
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus(); // clicking should also focus, so the keyboard can take over from there
        var p = e.GetPosition(this);
        foreach (var (display, box) in _hitRects)
            if (box.Contains(p)) { SelectedNumber = display.Number; break; }
    }

    // Arrow keys move the selection through the connected displays (radio-group semantics — moving the
    // selection applies it, the same as clicking a monitor). Home/End jump to the first/last (#603).
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Left: case Key.Up: MoveSelection(-1); e.Handled = true; break;
            case Key.Right: case Key.Down: MoveSelection(1); e.Handled = true; break;
            case Key.Home: SelectIndex(0); e.Handled = true; break;
            case Key.End: SelectIndex((Displays?.Count ?? 0) - 1); e.Handled = true; break;
        }
    }

    private void MoveSelection(int dir)
    {
        var displays = Displays;
        if (displays == null || displays.Count == 0) return;
        int cur = SelectedNumber is { } n ? IndexOfNumber(displays, n) : -1;
        int next = cur < 0 ? (dir > 0 ? 0 : displays.Count - 1)
                           : Math.Clamp(cur + dir, 0, displays.Count - 1);
        SelectIndex(next);
    }

    private void SelectIndex(int i)
    {
        var displays = Displays;
        if (displays != null && i >= 0 && i < displays.Count)
            SelectedNumber = displays[i].Number;
    }

    private static int IndexOfNumber(IReadOnlyList<DisplayInfo> displays, int number)
    {
        for (int i = 0; i < displays.Count; i++)
            if (displays[i].Number == number) return i;
        return -1;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedNumberProperty || change.Property == DisplaysProperty)
            UpdateAutomationName();
    }

    // A screen-reader-friendly name that reflects the current selection (the custom-drawn diagram has no
    // per-monitor controls of its own).
    private void UpdateAutomationName()
    {
        var sel = Displays?.FirstOrDefault(d => d.Number == SelectedNumber);
        AutomationProperties.SetName(this, sel != null
            ? $"Display mapping — {sel.Name} (display {sel.Number}) selected. Arrow keys choose a monitor."
            : "Display mapping — no monitor selected. Arrow keys choose a monitor.");
    }
}
