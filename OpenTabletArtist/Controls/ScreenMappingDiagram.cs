using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
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
    private static readonly IBrush SelFill = new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1));
    private static readonly IBrush SelText = Brushes.White;
    private static readonly IBrush UnselFill = new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xE3));
    private static readonly IBrush UnselText = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
    private static readonly IBrush SubText = new SolidColorBrush(Color.FromArgb(0xCC, 0x33, 0x33, 0x33));
    private static readonly IBrush SubTextOnSel = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF));
    private static readonly IPen UnselBorder = new Pen(new SolidColorBrush(Color.FromRgb(0xBF, 0xBF, 0xCB)), 1);
    private static readonly IPen SelBorder = new Pen(new SolidColorBrush(Color.FromRgb(0x4B, 0x4E, 0xC9)), 1.5);
    private static readonly BoxShadows Glow = new(new BoxShadow
    { OffsetX = 0, OffsetY = 0, Blur = 16, Spread = 2, Color = Color.FromArgb(0xB0, 0x63, 0x66, 0xF1) });

    private static readonly IBrush TabletFill = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x92));
    private static readonly IPen TabletBorder = new Pen(new SolidColorBrush(Color.FromRgb(0x5C, 0x5C, 0x63)), 1.5);
    private static readonly IBrush EffFill = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush Label = new SolidColorBrush(Color.FromArgb(0xCC, 0x33, 0x33, 0x33));
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromArgb(0x99, 0x33, 0x33, 0x33));
    private static Typeface UiFace => AppFonts.UiTypeface();

    // The connector/effective-area accent; bound to the theme accent so it's pink in Sakura, etc.
    private static readonly Color FallbackAccent = Color.FromRgb(0xE0, 0x21, 0x8A);

    // Corner-mapping lines (Wacom-style): a clear red, independent of the theme accent so the
    // active-area↔display correspondence reads the same in every theme.
    private static readonly IBrush MapLineBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0x28, 0x28));

    private readonly List<(DisplayInfo Display, Rect Box)> _hitRects = new();

    public static readonly StyledProperty<IReadOnlyList<DisplayInfo>?> DisplaysProperty =
        AvaloniaProperty.Register<ScreenMappingDiagram, IReadOnlyList<DisplayInfo>?>(nameof(Displays));
    public static readonly StyledProperty<int?> SelectedNumberProperty =
        AvaloniaProperty.Register<ScreenMappingDiagram, int?>(nameof(SelectedNumber), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<TabletAreaInfo?> AreaProperty =
        AvaloniaProperty.Register<ScreenMappingDiagram, TabletAreaInfo?>(nameof(Area));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<ScreenMappingDiagram, IBrush?>(nameof(AccentBrush));

    public IReadOnlyList<DisplayInfo>? Displays { get => GetValue(DisplaysProperty); set => SetValue(DisplaysProperty, value); }
    public int? SelectedNumber { get => GetValue(SelectedNumberProperty); set => SetValue(SelectedNumberProperty, value); }
    public TabletAreaInfo? Area { get => GetValue(AreaProperty); set => SetValue(AreaProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    static ScreenMappingDiagram()
    {
        AffectsRender<ScreenMappingDiagram>(DisplaysProperty, SelectedNumberProperty, AreaProperty,
            AccentBrushProperty);
        AffectsMeasure<ScreenMappingDiagram>(DisplaysProperty, AreaProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width;
        return new Size(w, 320);
    }

    public override void Render(DrawingContext ctx)
    {
        _hitRects.Clear();
        var displays = Displays;
        if (displays == null || displays.Count == 0)
        {
            DrawCentered(ctx, new Rect(Bounds.Size), "No displays detected", 13, UnselText);
            return;
        }

        var accent = (AccentBrush as ISolidColorBrush)?.Color ?? FallbackAccent;
        var accentBrush = new SolidColorBrush(accent);

        const double pad = 20;
        var inner = new Rect(Bounds.Size).Deflate(pad);
        if (inner.Width <= 0 || inner.Height <= 0) return;

        // Top ~52% = displays, bottom ~30% = tablet, the gap between holds the connector.
        var dispRegion = new Rect(inner.X, inner.Y, inner.Width, inner.Height * 0.52);
        double tabH = inner.Height * 0.30;
        var tabRegion = new Rect(inner.X, inner.Bottom - tabH - 16, inner.Width, tabH);

        // ── Displays ──
        double minX = displays.Min(d => d.X), minY = displays.Min(d => d.Y);
        double vbW = displays.Max(d => d.X + d.Width) - minX, vbH = displays.Max(d => d.Y + d.Height) - minY;
        if (vbW <= 0 || vbH <= 0) return;
        double dScale = Math.Min(dispRegion.Width / vbW, dispRegion.Height / vbH);
        double dOffX = dispRegion.X + (dispRegion.Width - vbW * dScale) / 2;
        double dOffY = dispRegion.Y + (dispRegion.Height - vbH * dScale) / 2;

        Rect? selectedBox = null;
        DisplayInfo? selDisplay = null;
        foreach (var d in displays)
        {
            var box = new Rect(dOffX + (d.X - minX) * dScale, dOffY + (d.Y - minY) * dScale,
                               d.Width * dScale, d.Height * dScale).Deflate(3);
            if (box.Width <= 1 || box.Height <= 1) continue;
            _hitRects.Add((d, box));
            if (SelectedNumber == d.Number) { selectedBox = box; selDisplay = d; continue; } // drawn last
            ctx.DrawRectangle(UnselFill, UnselBorder, box);
            DrawDisplayLabels(ctx, box, d, false);
        }
        if (selectedBox is { } sb && selDisplay is { } sd)
        {
            ctx.DrawRectangle(SelFill, SelBorder, new RoundedRect(sb), Glow);
            DrawDisplayLabels(ctx, sb, sd, true);
        }

        // ── Tablet (full + effective area) — rotation-aware so a turned tablet reads the same here as on
        //    the Active Area tab (#199): the full outline is drawn turned as physically held (portrait for
        //    90°/270°), while the effective area stays upright. ──
        var area = Area;
        double fullW = area?.FullWidth ?? 16, fullH = area?.FullHeight ?? 10;
        double rot = area != null ? (((area.Rotation % 360) + 360) % 360) : 0;
        bool perp = Math.Abs(rot % 180) > 0.5;
        double rotRad = rot * Math.PI / 180.0;

        double tabBoxW = Math.Min(tabRegion.Width * 0.5, tabRegion.Height * 2.4);
        var tabBox = new Rect(tabRegion.X + (tabRegion.Width - tabBoxW) / 2, tabRegion.Y, tabBoxW, tabRegion.Height - 16);

        // Fit the tablet's (rotated) bounding box, then centre the un-rotated outline and turn it.
        double bboxW = perp ? fullH : fullW, bboxH = perp ? fullW : fullH;
        var fitBox = FitAspect(bboxW, bboxH, tabBox);
        double tScale = bboxH > 0 ? fitBox.Height / bboxH : 1;
        var tCenter = fitBox.Center;
        var fullRect = new Rect(tCenter.X - fullW * tScale / 2, tCenter.Y - fullH * tScale / 2,
                                fullW * tScale, fullH * tScale);
        if (rot < 0.5)
        {
            ctx.DrawRectangle(TabletFill, TabletBorder, fullRect);
        }
        else
        {
            var m = Matrix.CreateTranslation(-tCenter.X, -tCenter.Y)
                    * Matrix.CreateRotation(-rotRad)
                    * Matrix.CreateTranslation(tCenter.X, tCenter.Y);
            using (ctx.PushTransform(m))
            {
                ctx.DrawRectangle(TabletFill, TabletBorder, fullRect);
            }
        }
        DrawCentered(ctx, fullRect, "Tablet", 12, Brushes.White);

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
            ctx.DrawRectangle(EffFill, new Pen(accentBrush, 1.5), effRect);
        }

        // ── Connector: red lines joining each corner of the effective area to the matching corner of
        //    the selected display (Wacom-style), so the 1:1 mapping is obvious. Corners map in place
        //    (top-left→top-left, etc.); the effective area sits below the display, so the four lines
        //    fan out into a frustum rather than crossing. ──
        if (selectedBox is { } selBox)
        {
            var mapPen = new Pen(MapLineBrush, 1.5) { LineCap = PenLineCap.Round };
            ctx.DrawLine(mapPen, effRect.TopLeft, selBox.TopLeft);
            ctx.DrawLine(mapPen, effRect.TopRight, selBox.TopRight);
            ctx.DrawLine(mapPen, effRect.BottomLeft, selBox.BottomLeft);
            ctx.DrawLine(mapPen, effRect.BottomRight, selBox.BottomRight);
        }

        if (area != null)
            DrawCentered(ctx, new Rect(tabBox.X, fullRect.Bottom + 1, tabBox.Width, 16),
                $"{area.EffWidth:0.#} × {area.EffHeight:0.#} mm", 10.5, Label);
    }

    private void DrawDisplayLabels(DrawingContext ctx, Rect box, DisplayInfo d, bool selected)
    {
        double numSize = Math.Clamp(Math.Min(box.Height * 0.34, box.Width * 0.4), 12, 30);
        var num = Text(d.Number.ToString(), numSize, selected ? SelText : UnselText);
        var subBrush = selected ? SubTextOnSel : SubText;
        bool roomy = box.Height > numSize + 24 && box.Width > 70;
        // Number + a "Primary" marker only; resolution/refresh and port live in the per-display list
        // below the diagram, so the boxes stay uncluttered (#570).
        var res = roomy && d.IsPrimary ? Text("Primary", 10, subBrush) : null;

        double totalH = num.Height + (res != null ? res.Height + 1 : 0);
        double y = box.Y + (box.Height - totalH) / 2, cx = box.Center.X;
        ctx.DrawText(num, new Point(cx - num.Width / 2, y));
        if (res != null) ctx.DrawText(res, new Point(cx - res.Width / 2, y + num.Height + 1));
    }

    private static Rect FitAspect(double w, double h, Rect box)
    {
        if (w <= 0 || h <= 0 || box.Width <= 0 || box.Height <= 0) return box;
        double s = Math.Min(box.Width / w, box.Height / h);
        return new Rect(box.X + (box.Width - w * s) / 2, box.Y + (box.Height - h * s) / 2, w * s, h * s);
    }

    private static void DrawCentered(DrawingContext ctx, Rect area, string text, double size, IBrush brush)
    {
        var ft = Text(text, size, brush);
        ctx.DrawText(ft, new Point(area.X + (area.Width - ft.Width) / 2, area.Y + (area.Height - ft.Height) / 2));
    }

    private static FormattedText Text(string s, double size, IBrush brush) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, UiFace, size, brush);

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        foreach (var (display, box) in _hitRects)
            if (box.Contains(p)) { SelectedNumber = display.Number; break; }
    }
}
