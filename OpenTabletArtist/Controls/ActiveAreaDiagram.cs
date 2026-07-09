using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Controls;

/// <summary>A committed active-area edit from the diagram (#199): the new size + centre in tablet mm.</summary>
public readonly record struct ActiveAreaEdit(double Width, double Height, double CenterX, double CenterY);

/// <summary>
/// A focused picture of the tablet's active area: the full digitizer area with the effective (used) area
/// drawn inside it, to scale and in position. Interactive (#199): drag the area to move it, drag a corner
/// to resize proportionally (aspect stays locked to the display). When rotated, the tablet is drawn turned
/// as physically held with the active area upright, so the same upright rectangle is edited at any rotation.
/// </summary>
public sealed class ActiveAreaDiagram : Control
{
    private static readonly IBrush FullFill = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x92));
    private static readonly IPen FullBorder = new Pen(new SolidColorBrush(Color.FromRgb(0x5C, 0x5C, 0x63)), 1.5);
    private static readonly IBrush FullLabel = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF));
    private static Typeface UiFace => AppFonts.UiTypeface();
    private static readonly Color FallbackAccent = Color.FromRgb(0xE0, 0x21, 0x8A);

    private const double Pad = 10;
    private const double HandleHit = 11;   // px radius for grabbing a corner
    private const double HandleSize = 8;    // px drawn handle square

    public static readonly StyledProperty<TabletAreaInfo?> AreaProperty =
        AvaloniaProperty.Register<ActiveAreaDiagram, TabletAreaInfo?>(nameof(Area));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<ActiveAreaDiagram, IBrush?>(nameof(AccentBrush));
    public static readonly StyledProperty<bool> UseImperialUnitsProperty =
        AvaloniaProperty.Register<ActiveAreaDiagram, bool>(nameof(UseImperialUnits));
    public static readonly StyledProperty<bool> EditableProperty =
        AvaloniaProperty.Register<ActiveAreaDiagram, bool>(nameof(Editable));

    public TabletAreaInfo? Area { get => GetValue(AreaProperty); set => SetValue(AreaProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public bool UseImperialUnits { get => GetValue(UseImperialUnitsProperty); set => SetValue(UseImperialUnitsProperty, value); }
    /// <summary>Allow dragging to move / corner-dragging to resize the effective area (#199).</summary>
    public bool Editable { get => GetValue(EditableProperty); set => SetValue(EditableProperty, value); }

    /// <summary>Raised on drag release with the new area (tablet mm), for the view model to persist.</summary>
    public event EventHandler<ActiveAreaEdit>? AreaCommitted;

    static ActiveAreaDiagram()
    {
        AffectsRender<ActiveAreaDiagram>(AreaProperty, AccentBrushProperty, UseImperialUnitsProperty, EditableProperty);
        AffectsMeasure<ActiveAreaDiagram>(AreaProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width;
        return new Size(w, 320);
    }

    // ── Layout: maps between tablet mm and screen px for the current Area + Bounds ──────────────────
    private readonly record struct Layout(
        bool Valid, double Scale, Point Center, double FullW, double FullH, double RotRad, bool Perp);

    private Layout Compute()
    {
        var area = Area;
        var box = new Rect(Bounds.Size).Deflate(Pad);
        if (area is not { FullWidth: > 0, FullHeight: > 0 } || box.Width <= 0 || box.Height <= 0)
            return default;

        double fullW = area.FullWidth, fullH = area.FullHeight;
        double rot = ((area.Rotation % 360) + 360) % 360;
        bool perp = Math.Abs(rot % 180) > 0.5;
        double bboxW = perp ? fullH : fullW, bboxH = perp ? fullW : fullH;
        var fit = FitAspect(bboxW, bboxH, box);
        double scale = fit.Height / bboxH;
        return new Layout(true, scale, fit.Center, fullW, fullH, rot * Math.PI / 180.0, perp);
    }

    // Tablet mm point → screen (the tablet is drawn rotated by -rot about the diagram centre).
    private static Point TabletToScreen(Layout l, double tx, double ty)
    {
        double dx = (tx - l.FullW / 2) * l.Scale, dy = (ty - l.FullH / 2) * l.Scale;
        double c = Math.Cos(-l.RotRad), s = Math.Sin(-l.RotRad);
        return new Point(l.Center.X + dx * c - dy * s, l.Center.Y + dx * s + dy * c);
    }

    // Screen point → tablet mm (inverse of TabletToScreen).
    private static (double X, double Y) ScreenToTablet(Layout l, Point p)
    {
        double dx = p.X - l.Center.X, dy = p.Y - l.Center.Y;
        double c = Math.Cos(l.RotRad), s = Math.Sin(l.RotRad);   // rotate back by +rot
        double tx = dx * c - dy * s, ty = dx * s + dy * c;
        return (l.FullW / 2 + tx / l.Scale, l.FullH / 2 + ty / l.Scale);
    }

    // The effective area's upright screen rectangle (position follows the tablet rotation; orientation upright).
    private static Rect AreaRect(Layout l, double w, double h, double cx, double cy)
    {
        var c = TabletToScreen(l, cx, cy);
        double sw = w * l.Scale, sh = h * l.Scale;
        return new Rect(c.X - sw / 2, c.Y - sh / 2, sw, sh);
    }

    // ── Interaction ─────────────────────────────────────────────────────────────────────────────────
    private enum Mode { None, Move, Resize }
    private Mode _mode;
    private Point _anchorScreen;      // resize: the fixed (opposite) corner, in screen px
    private int _dirX, _dirY;         // resize: which way the grabbed corner extends from the anchor
    private (double X, double Y) _grabTablet;  // move: tablet-mm point under the cursor at press
    private double _grabW, _grabH, _grabX, _grabY; // area at press
    private ActiveAreaEdit? _preview; // live edit shown while dragging

    private double DisplayAspect(TabletAreaInfo a) =>
        a.DisplayWidth > 0 && a.DisplayHeight > 0 ? a.DisplayWidth / a.DisplayHeight
        : a.EffHeight > 0 ? a.EffWidth / a.EffHeight : 1;

    private float MinAreaWidth(TabletAreaInfo a, int rot)
    {
        var max = AreaMappingCalculator.FitForRotation((float)a.FullWidth, (float)a.FullHeight,
            (float)Math.Max(1, a.DisplayWidth), (float)Math.Max(1, a.DisplayHeight), rot);
        return (float)Math.Max(2, max.Width * 0.1);   // 10% of the maximum
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!Editable || Area is not { } a) return;
        var l = Compute();
        if (!l.Valid) return;

        var p = e.GetPosition(this);
        var rect = AreaRect(l, a.EffWidth, a.EffHeight, a.EffCenterX, a.EffCenterY);
        var corners = Corners(rect);

        // Grab a corner? → resize with the opposite corner anchored.
        for (int i = 0; i < 4; i++)
        {
            if (Distance(p, corners[i]) <= HandleHit)
            {
                _mode = Mode.Resize;
                var anchor = corners[(i + 2) % 4];
                _anchorScreen = anchor;
                _dirX = Math.Sign(corners[i].X - anchor.X);
                _dirY = Math.Sign(corners[i].Y - anchor.Y);
                if (_dirX == 0) _dirX = 1;
                if (_dirY == 0) _dirY = 1;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
        }
        // Inside the body? → move.
        if (rect.Contains(p))
        {
            _mode = Mode.Move;
            _grabTablet = ScreenToTablet(l, p);
            _grabW = a.EffWidth; _grabH = a.EffHeight; _grabX = a.EffCenterX; _grabY = a.EffCenterY;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_mode == Mode.None || Area is not { } a) return;
        var l = Compute();
        if (!l.Valid) return;

        int rot = (int)Math.Round(((a.Rotation % 360) + 360) % 360);
        float fullW = (float)a.FullWidth, fullH = (float)a.FullHeight;
        var p = e.GetPosition(this);

        AreaMappingCalculator.TabletArea result;
        if (_mode == Mode.Move)
        {
            var now = ScreenToTablet(l, p);
            float cx = (float)(_grabX + (now.X - _grabTablet.X));
            float cy = (float)(_grabY + (now.Y - _grabTablet.Y));
            result = AreaMappingCalculator.ClampArea((float)_grabW, (float)_grabH, cx, cy, rot, fullW, fullH, MinAreaWidth(a, rot));
        }
        else
        {
            double aspect = DisplayAspect(a);
            double rawW = Math.Abs(p.X - _anchorScreen.X), rawH = Math.Abs(p.Y - _anchorScreen.Y);
            double w = Math.Max(rawW, rawH * aspect);      // grow to reach the cursor, aspect-locked
            double h = w / aspect;
            var moving = new Point(_anchorScreen.X + _dirX * w, _anchorScreen.Y + _dirY * h);
            var centerScreen = new Point((_anchorScreen.X + moving.X) / 2, (_anchorScreen.Y + moving.Y) / 2);
            var tc = ScreenToTablet(l, centerScreen);
            result = AreaMappingCalculator.ClampArea((float)(w / l.Scale), (float)(h / l.Scale),
                (float)tc.X, (float)tc.Y, rot, fullW, fullH, MinAreaWidth(a, rot));
        }

        _preview = new ActiveAreaEdit(result.Width, result.Height, result.X, result.Y);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_mode == Mode.None) return;
        _mode = Mode.None;
        e.Pointer.Capture(null);
        if (_preview is { } edit) AreaCommitted?.Invoke(this, edit);
        _preview = null;
        InvalidateVisual();
        e.Handled = true;
    }

    private static Point[] Corners(Rect r) => new[]
    {
        r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft,
    };

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // ── Rendering ───────────────────────────────────────────────────────────────────────────────────
    public override void Render(DrawingContext ctx)
    {
        var area = Area;
        var accent = (AccentBrush as ISolidColorBrush)?.Color ?? FallbackAccent;

        var box = new Rect(Bounds.Size).Deflate(Pad);
        if (box.Width <= 0 || box.Height <= 0) return;

        double fullW = area?.FullWidth ?? 16, fullH = area?.FullHeight ?? 10;
        double rot = area != null ? (((area.Rotation % 360) + 360) % 360) : 0;

        if (area is not { FullWidth: > 0, FullHeight: > 0 })
        {
            var fr = FitAspect(fullW, fullH, box);
            ctx.DrawRectangle(FullFill, FullBorder, fr);
            DrawCentered(ctx, fr, "No active-area data", 12, Brushes.White);
            return;
        }

        var l = Compute();
        // Effective values: the live preview while dragging, else the bound area.
        double ew = _preview?.Width ?? area.EffWidth, eh = _preview?.Height ?? area.EffHeight;
        double ecx = _preview?.CenterX ?? area.EffCenterX, ecy = _preview?.CenterY ?? area.EffCenterY;

        // Tablet outline (turned as physically held when rotated) + a top-edge orientation marker.
        var tabletRect = new Rect(l.Center.X - fullW * l.Scale / 2, l.Center.Y - fullH * l.Scale / 2,
                                  fullW * l.Scale, fullH * l.Scale);
        if (rot < 0.5)
        {
            ctx.DrawRectangle(FullFill, FullBorder, tabletRect);
            ctx.DrawText(Text("Full tablet area", 11, FullLabel), new Point(tabletRect.X + 8, tabletRect.Y + 6));
        }
        else
        {
            var m = Matrix.CreateTranslation(-l.Center.X, -l.Center.Y)
                    * Matrix.CreateRotation(-l.RotRad)
                    * Matrix.CreateTranslation(l.Center.X, l.Center.Y);
            using (ctx.PushTransform(m))
            {
                ctx.DrawRectangle(FullFill, FullBorder, tabletRect);
                DrawTopMarker(ctx, tabletRect, accent);
            }
            ctx.DrawText(Text("Full tablet area", 11, FullLabel), new Point(box.X + 2, box.Y + 2));
        }

        // Effective area (upright), its label, and — when editable — corner handles.
        var effRect = AreaRect(l, ew, eh, ecx, ecy);
        ctx.DrawRectangle(new SolidColorBrush(accent, 0.22), new Pen(new SolidColorBrush(accent), 2), effRect);
        if (effRect is { Height: > 26, Width: > 70 })
            DrawCentered(ctx, effRect, FormatSize(ew, eh), 11.5, Brushes.White);

        if (Editable)
        {
            var handleFill = new SolidColorBrush(Colors.White);
            var handlePen = new Pen(new SolidColorBrush(accent), 2);
            foreach (var c in Corners(effRect))
                ctx.DrawRectangle(handleFill, handlePen,
                    new Rect(c.X - HandleSize / 2, c.Y - HandleSize / 2, HandleSize, HandleSize));
        }
    }

    private static Rect FitAspect(double w, double h, Rect box)
    {
        if (w <= 0 || h <= 0 || box.Width <= 0 || box.Height <= 0) return box;
        double s = Math.Min(box.Width / w, box.Height / h);
        return new Rect(box.X + (box.Width - w * s) / 2, box.Y + (box.Height - h * s) / 2, w * s, h * s);
    }

    private static void DrawTopMarker(DrawingContext ctx, Rect tablet, Color accent)
    {
        double cx = tablet.X + tablet.Width / 2;
        double s = Math.Clamp(tablet.Width * 0.06, 7, 16);
        double top = tablet.Y + 6;
        var geo = new PolylineGeometry(new[]
        {
            new Point(cx, top),
            new Point(cx - s, top + s * 1.4),
            new Point(cx + s, top + s * 1.4),
        }, true);
        ctx.DrawGeometry(new SolidColorBrush(accent), null, geo);
    }

    private static void DrawCentered(DrawingContext ctx, Rect area, string text, double size, IBrush brush)
    {
        var ft = Text(text, size, brush);
        ctx.DrawText(ft, new Point(area.X + (area.Width - ft.Width) / 2, area.Y + (area.Height - ft.Height) / 2));
    }

    private static FormattedText Text(string s, double size, IBrush brush) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, UiFace, size, brush);

    private string FormatSize(double wMm, double hMm) =>
        UseImperialUnits ? $"{wMm / 25.4:0.##} × {hMm / 25.4:0.##} in" : $"{wMm:0.#} × {hMm:0.#} mm";
}
