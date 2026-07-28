using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace OpenTabletArtist.Controls;

/// <summary>
/// A classic hue-ring + saturation/value-triangle colour picker (#556 follow-up). Hue is chosen on the outer
/// ring; saturation and value on the triangle inscribed in it (one corner the pure hue, one white, one
/// black), which rotates so its hue corner points at the selected hue. HSV is the source of truth so the
/// ring/triangle positions survive a fully-desaturated or black colour; <see cref="Color"/> binds two-way.
/// </summary>
public class HueTriangleWheel : Control
{
    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<HueTriangleWheel, Color>(nameof(Color), Colors.Red,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public Color Color { get => GetValue(ColorProperty); set => SetValue(ColorProperty, value); }

    // HSV source of truth: hue 0..360, sat/value 0..1. Kept separately so a grey/black colour doesn't lose
    // the ring/triangle handle positions (RGB->HSV is ambiguous there).
    private double _h, _s = 1, _v = 1;
    private bool _fromSelf; // guard: we're writing Color from our own HSV, so don't re-derive HSV back

    private enum Drag { None, Ring, Triangle }
    private Drag _drag = Drag.None;

    private const double Pad = 5;         // outer margin
    private const double RingFraction = 0.16; // ring thickness as a fraction of the outer radius

    static HueTriangleWheel()
    {
        AffectsRender<HueTriangleWheel>(ColorProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Square, capped so it never grows unbounded in a StackPanel.
        double s = Math.Min(
            double.IsInfinity(availableSize.Width) ? 220 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 220 : availableSize.Height);
        if (s <= 0 || double.IsInfinity(s)) s = 220;
        return new Size(s, s);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColorProperty && !_fromSelf)
        {
            var hsv = Color.ToHsv();
            _s = hsv.S;
            _v = hsv.V;
            if (hsv.S > 0.0001 && hsv.V > 0.0001) _h = hsv.H; // keep hue when the colour is grey/black
            InvalidateVisual();
        }
    }

    // ── geometry ────────────────────────────────────────────────────────────────────────────────────
    private Point Center => new(Bounds.Width / 2, Bounds.Height / 2);
    private double OuterR => Math.Min(Bounds.Width, Bounds.Height) / 2 - Pad;
    private double InnerR => OuterR * (1 - RingFraction);
    private double TriR => InnerR - 3; // triangle inscribed just inside the ring hole

    // Hue 0 sits at the top (12 o'clock) to match Avalonia's conic gradient, increasing clockwise; the east-
    // convention angle for a hue is therefore (hue - 90)°.
    private static double HueRad(double hue) => (hue - 90) * Math.PI / 180.0;

    // Triangle vertices in control coords: hue corner points at the selected hue, white 120° counter-clockwise
    // of it, black 120° clockwise — so at hue 0 the hue apex is up with white/black across the bottom.
    private (Point hue, Point white, Point black) Triangle()
    {
        double a = HueRad(_h);
        var c = Center;
        double r = TriR;
        Point P(double ang) => new(c.X + r * Math.Cos(ang), c.Y + r * Math.Sin(ang));
        return (P(a), P(a - 2 * Math.PI / 3), P(a + 2 * Math.PI / 3));
    }

    // ── render ──────────────────────────────────────────────────────────────────────────────────────
    public override void Render(DrawingContext ctx)
    {
        if (OuterR <= 4) return;
        var c = Center;

        DrawHueRing(ctx, c);
        DrawTriangle(ctx);
        DrawRingHandle(ctx, c);
        DrawTriangleHandle(ctx);
    }

    private void DrawHueRing(DrawingContext ctx, Point c)
    {
        var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
        ring.Children.Add(new EllipseGeometry(new Rect(c.X - OuterR, c.Y - OuterR, 2 * OuterR, 2 * OuterR)));
        ring.Children.Add(new EllipseGeometry(new Rect(c.X - InnerR, c.Y - InnerR, 2 * InnerR, 2 * InnerR)));

        var conic = new ConicGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            Angle = 0,
        };
        // Pure hue every 30° so the wheel is smooth; 0°/360° close the loop at red.
        for (int deg = 0; deg <= 360; deg += 30)
            conic.GradientStops.Add(new GradientStop(new HsvColor(1, deg % 360, 1, 1).ToRgb(), deg / 360.0));

        ctx.DrawGeometry(conic, null, ring);
    }

    private void DrawTriangle(DrawingContext ctx)
    {
        var (pHue, pWhite, pBlack) = Triangle();
        var hueColor = new HsvColor(1, _h, 1, 1).ToRgb();

        var tri = new StreamGeometry();
        using (var g = tri.Open())
        {
            g.BeginFigure(pHue, true);
            g.LineTo(pWhite);
            g.LineTo(pBlack);
            g.EndFigure(true);
        }

        // Saturation axis (white -> hue) then a value overlay (white/hue edge -> black), clipped to the
        // triangle. Two linear gradients approximate the barycentric SV triangle well enough visually.
        var satBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(pWhite.X, pWhite.Y, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(pHue.X, pHue.Y, RelativeUnit.Absolute),
            GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(hueColor, 1) },
        };
        var edgeMid = new Point((pWhite.X + pHue.X) / 2, (pWhite.Y + pHue.Y) / 2);
        var valBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(edgeMid.X, edgeMid.Y, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(pBlack.X, pBlack.Y, RelativeUnit.Absolute),
            GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0), new GradientStop(Colors.Black, 1) },
        };

        using (ctx.PushGeometryClip(tri))
        {
            var full = new Rect(Bounds.Size);
            ctx.FillRectangle(satBrush, full);
            ctx.FillRectangle(valBrush, full);
        }
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), 1), tri);
    }

    private void DrawRingHandle(DrawingContext ctx, Point c)
    {
        double a = HueRad(_h);
        double mid = (OuterR + InnerR) / 2;
        var p = new Point(c.X + mid * Math.Cos(a), c.Y + mid * Math.Sin(a));
        double rr = (OuterR - InnerR) / 2;
        ctx.DrawEllipse(null, new Pen(Brushes.White, 2), p, rr, rr);
        ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), 1), p, rr + 1, rr + 1);
    }

    private void DrawTriangleHandle(DrawingContext ctx)
    {
        var p = SvToPoint(_s, _v);
        ctx.DrawEllipse(null, new Pen(Brushes.White, 2), p, 6, 6);
        ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), 1), p, 7, 7);
    }

    // ── s/v <-> point ───────────────────────────────────────────────────────────────────────────────
    // Barycentric weights: black = 1-V, hue = S*V, white = (1-S)*V.
    private Point SvToPoint(double s, double v)
    {
        var (pHue, pWhite, pBlack) = Triangle();
        double wHue = s * v, wWhite = (1 - s) * v, wBlack = 1 - v;
        return new Point(
            wHue * pHue.X + wWhite * pWhite.X + wBlack * pBlack.X,
            wHue * pHue.Y + wWhite * pWhite.Y + wBlack * pBlack.Y);
    }

    private void PointToSv(Point p, out double s, out double v)
    {
        var (a, b, cc) = Triangle();
        // Barycentric coords of p in triangle (a=hue, b=white, cc=black).
        double det = (b.Y - cc.Y) * (a.X - cc.X) + (cc.X - b.X) * (a.Y - cc.Y);
        double wa = Math.Abs(det) < 1e-9 ? 0
            : ((b.Y - cc.Y) * (p.X - cc.X) + (cc.X - b.X) * (p.Y - cc.Y)) / det;
        double wb = Math.Abs(det) < 1e-9 ? 0
            : ((cc.Y - a.Y) * (p.X - cc.X) + (a.X - cc.X) * (p.Y - cc.Y)) / det;
        double wc = 1 - wa - wb;
        // Clamp into the triangle so dragging past an edge still tracks the nearest valid colour.
        wa = Math.Max(0, wa); wb = Math.Max(0, wb); wc = Math.Max(0, wc);
        double sum = wa + wb + wc;
        if (sum <= 0) { s = _s; v = _v; return; }
        wa /= sum; wb /= sum; wc /= sum;
        v = wa + wb;                       // value = 1 - black weight
        s = v > 1e-6 ? wa / v : 0;         // saturation = hue share of the non-black mix
    }

    // ── interaction ─────────────────────────────────────────────────────────────────────────────────
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        double d = Distance(p, Center);
        _drag = d >= InnerR && d <= OuterR + 2 ? Drag.Ring : Drag.Triangle;
        e.Pointer.Capture(this);
        Apply(p);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag != Drag.None) Apply(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = Drag.None;
        e.Pointer.Capture(null);
    }

    private void Apply(Point p)
    {
        if (_drag == Drag.Ring)
        {
            double ang = Math.Atan2(p.Y - Center.Y, p.X - Center.X) * 180.0 / Math.PI;
            _h = (ang + 90 + 360) % 360; // inverse of HueRad: screen angle -> hue (hue 0 at top)
        }
        else
        {
            PointToSv(p, out _s, out _v);
        }
        _fromSelf = true;
        Color = new HsvColor(1, _h, _s, _v).ToRgb();
        _fromSelf = false;
        InvalidateVisual();
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
