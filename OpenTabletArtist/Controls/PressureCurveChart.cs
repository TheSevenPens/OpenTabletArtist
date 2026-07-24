using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Controls;

/// <summary>
/// Interactive editor + visualization for the Extended pressure curve
/// (<see cref="PressureCurveSettings"/>). Draws the live curve trace and three draggable control
/// nodes — min (input/output minimum, pink), max (input/output maximum, cyan), and a bend node
/// (amber) that sits on the curve at the middle of the input range and shapes <see cref="Softness"/>
/// by vertical drag: pull up = softer (concave), down = firmer (convex). The bend node and the host's
/// Softness slider both write the same <see cref="PressureCurveSettings.Softness"/>, so they stay in
/// sync. Adapted from PenDynamicsLab's PressureChartControl, trimmed to the single curve type we ship.
/// </summary>
public sealed class PressureCurveChart : Control
{
    private const double PadLeft = 16;
    private const double PadRight = 16;
    private const double PadTop = 16;
    private const double PadBottom = 16;
    private const double NodeRadius = 9;     // hit-test radius
    private const double NodeDrawRadius = 6;

    // Light palette to match the app's surfaces (white panels, dark text).
    private static readonly IBrush PlotBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x1F, 0x00, 0x00, 0x00)), 1);
    private static readonly IPen CurvePen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00)), 2.2);
    private static readonly IBrush MinNodeBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x1E, 0x7A));
    private static readonly IBrush MaxNodeBrush = new SolidColorBrush(Color.FromRgb(0x0E, 0x9F, 0xD6));
    private static readonly IBrush BendNodeBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)); // softness
    private static readonly IPen NodeGuidePen = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00)), 1)
    { DashStyle = new DashStyle(new double[] { 3, 4 }, 0) };
    private static readonly IPen NodeOutline = new Pen(Brushes.White, 1.5);
    private static readonly IBrush LiveDotBrush = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
    private static readonly IPen LiveGuidePen = new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0x10, 0xB9, 0x81)), 1)
    { DashStyle = new DashStyle(new double[] { 3, 4 }, 0) };

    public static readonly StyledProperty<PressureCurveSettings> CurveProperty =
        AvaloniaProperty.Register<PressureCurveChart, PressureCurveSettings>(
            nameof(Curve), defaultValue: PressureCurveSettings.Default,
            defaultBindingMode: BindingMode.TwoWay);

    public PressureCurveSettings Curve
    {
        get => GetValue(CurveProperty);
        set => SetValue(CurveProperty, value);
    }

    /// <summary>Optional live input pressure [0,1] to plot against the curve (null hides it).</summary>
    public static readonly StyledProperty<double?> LivePressureProperty =
        AvaloniaProperty.Register<PressureCurveChart, double?>(nameof(LivePressure));

    public double? LivePressure
    {
        get => GetValue(LivePressureProperty);
        set => SetValue(LivePressureProperty, value);
    }

    static PressureCurveChart()
    {
        AffectsRender<PressureCurveChart>(CurveProperty, LivePressureProperty);
    }

    /// <summary>Keep the inner plot area square at the available width.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = availableSize.Width;
        if (double.IsInfinity(width) || double.IsNaN(width) || width <= 0)
            return base.MeasureOverride(availableSize);
        double plotSide = Math.Max(0, width - PadLeft - PadRight);
        return new Size(width, plotSide + PadTop + PadBottom);
    }

    private (double plotW, double plotH) Layout()
        => (Bounds.Width - PadLeft - PadRight, Bounds.Height - PadTop - PadBottom);

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
    private static double Round2(double v) => Math.Round(v * 100) / 100;

    /// <summary>Input-space x of the softness bend node: the middle of the active input range,
    /// i.e. the remapped xNorm = 0.5 where the power curve bends most. Null when the range is empty.</summary>
    private static double? BendInputX(PressureCurveSettings c)
    {
        double range = c.InputMaximum - c.InputMinimum;
        return range > 0 ? c.InputMinimum + 0.5 * range : (double?)null;
    }

    private double XValueFromCanvas(double x)
    {
        var (plotW, _) = Layout();
        return plotW > 0 ? Clamp01((x - PadLeft) / plotW) : 0;
    }

    private double YValueFromCanvas(double y)
    {
        var (_, plotH) = Layout();
        return plotH > 0 ? Clamp01((PadTop + plotH - y) / plotH) : 0;
    }

    // ── Render ──────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        var (plotW, plotH) = Layout();
        if (plotW <= 0 || plotH <= 0) return;

        context.FillRectangle(PlotBrush, new Rect(PadLeft, PadTop, plotW, plotH));
        for (int i = 0; i <= 4; i++)
        {
            double gx = PadLeft + i / 4.0 * plotW;
            double gy = PadTop + i / 4.0 * plotH;
            context.DrawLine(GridPen, new Point(gx, PadTop), new Point(gx, PadTop + plotH));
            context.DrawLine(GridPen, new Point(PadLeft, gy), new Point(PadLeft + plotW, gy));
        }

        DrawCurve(context, plotW, plotH);

        var curve = Curve;
        DrawNode(context, curve.InputMinimum, curve.Minimum, MinNodeBrush);
        DrawNode(context, curve.InputMaximum, curve.Maximum, MaxNodeBrush);

        // Bend (softness) node — rides on the curve at the middle of the input range. No axis guides
        // (it tracks the curve, not an input/output value), so it reads as a handle on the line itself.
        if (BendInputX(curve) is { } bendX)
        {
            double bendY = PressureCurve.Apply(bendX, curve);
            double cx = PadLeft + bendX * plotW;
            double cy = PadTop + plotH - bendY * plotH;
            context.DrawEllipse(BendNodeBrush, NodeOutline, new Point(cx, cy), NodeDrawRadius, NodeDrawRadius);
        }

        if (LivePressure is { } live)
        {
            double mapped = PressureCurve.Apply(live, curve);
            double dotX = PadLeft + Clamp01(live) * plotW;
            double dotY = PadTop + plotH - mapped * plotH;
            context.DrawLine(LiveGuidePen, new Point(dotX, PadTop + plotH), new Point(dotX, dotY));
            context.DrawLine(LiveGuidePen, new Point(PadLeft, dotY), new Point(dotX, dotY));
            context.DrawEllipse(LiveDotBrush, null, new Point(dotX, dotY), 4, 4);
        }
    }

    private void DrawCurve(DrawingContext context, double plotW, double plotH)
    {
        // Apply() covers the full [0,1] domain (lead-in floor, curve, lead-out cap, Cut dead-zone),
        // so we just sample it per pixel.
        var curve = Curve;
        var figure = new PathFigure { IsClosed = false, Segments = new PathSegments() };
        bool started = false;
        for (int px = 0; px <= (int)plotW; px++)
        {
            double x = px / plotW;
            double y = PressureCurve.Apply(x, curve);
            var pt = new Point(PadLeft + px, PadTop + plotH - y * plotH);
            if (!started) { figure.StartPoint = pt; started = true; }
            else figure.Segments.Add(new LineSegment { Point = pt });
        }
        context.DrawGeometry(null, CurvePen, new PathGeometry { Figures = new PathFigures { figure } });
    }

    private void DrawNode(DrawingContext context, double xValue, double yValue, IBrush color)
    {
        var (plotW, plotH) = Layout();
        double cx = PadLeft + xValue * plotW;
        double cy = PadTop + plotH - yValue * plotH;
        context.DrawLine(NodeGuidePen, new Point(cx, cy), new Point(cx, PadTop + plotH));
        context.DrawLine(NodeGuidePen, new Point(cx, cy), new Point(PadLeft, cy));
        context.DrawEllipse(color, NodeOutline, new Point(cx, cy), NodeDrawRadius, NodeDrawRadius);
    }

    // ── Pointer interaction ─────────────────────────────────────

    private enum DragKind { None, Min, Max, Softness }
    private DragKind _dragging = DragKind.None;

    private static double Distance(Point p, double cx, double cy)
        => Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));

    private DragKind HitTest(Point p)
    {
        var (plotW, plotH) = Layout();
        var c = Curve;
        // Endpoints take priority — the bend node can visually overlap them at narrow input ranges.
        if (Distance(p, PadLeft + c.InputMinimum * plotW, PadTop + plotH - c.Minimum * plotH) <= NodeRadius)
            return DragKind.Min;
        if (Distance(p, PadLeft + c.InputMaximum * plotW, PadTop + plotH - c.Maximum * plotH) <= NodeRadius)
            return DragKind.Max;
        if (BendInputX(c) is { } bendX)
        {
            double bendY = PressureCurve.Apply(bendX, c);
            if (Distance(p, PadLeft + bendX * plotW, PadTop + plotH - bendY * plotH) <= NodeRadius)
                return DragKind.Softness;
        }
        return DragKind.None;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var hit = HitTest(e.GetPosition(this));
        if (hit != DragKind.None)
        {
            _dragging = hit;
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        if (_dragging == DragKind.None)
        {
            // Bend node only travels vertically → a vertical-resize cursor; endpoints move freely.
            Cursor = HitTest(pos) switch
            {
                DragKind.Softness => new Cursor(StandardCursorType.SizeNorthSouth),
                DragKind.None => Cursor.Default,
                _ => new Cursor(StandardCursorType.SizeAll),
            };
            return;
        }

        var c = Curve;
        if (_dragging == DragKind.Softness)
        {
            DragSoftness(pos, c);
            return;
        }

        double inVal = Round2(XValueFromCanvas(pos.X));
        double outVal = Round2(YValueFromCanvas(pos.Y));
        if (_dragging == DragKind.Min)
        {
            inVal = Math.Min(inVal, c.InputMaximum - 0.01);
            outVal = Math.Min(outVal, c.Maximum);
            Curve = c with { InputMinimum = Clamp01(inVal), Minimum = Clamp01(outVal) };
        }
        else
        {
            inVal = Math.Max(inVal, c.InputMinimum + 0.01);
            outVal = Math.Max(outVal, c.Minimum);
            Curve = c with { InputMaximum = Clamp01(inVal), Maximum = Clamp01(outVal) };
        }
    }

    /// <summary>
    /// Invert the bend node's vertical position back into <see cref="PressureCurveSettings.Softness"/>.
    /// At the input midpoint the (pre-output-scale) curve value is 0.5^exponent, so from the node's
    /// height y we recover exponent = ln(curved)/ln(0.5) — then map exponent back to the slider's
    /// softness. Identical inverse of <see cref="PressureCurve.Apply"/>'s power step, so node and
    /// slider agree exactly. Horizontal drag is ignored (x is pinned to the input midpoint).
    /// </summary>
    private void DragSoftness(Point pos, PressureCurveSettings c)
    {
        double outRange = c.Maximum - c.Minimum;
        if (outRange <= 0) return; // flat output range — nothing to shape

        // Un-scale the node's output value back to the [0,1] curved value, away from the log's poles.
        double curved = Math.Clamp((YValueFromCanvas(pos.Y) - c.Minimum) / outRange, 0.001, 0.999);
        double exponent = Math.Log(curved) / Math.Log(0.5);

        // Inverse of Apply()'s softness→exponent mapping.
        double softness = exponent <= 1 ? 1 - exponent : 1 / exponent - 1;
        softness = Math.Clamp(Round2(softness), -0.95, 0.95);

        if (softness != c.Softness)
            Curve = c with { Softness = softness };
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging != DragKind.None)
        {
            _dragging = DragKind.None;
            e.Pointer.Capture(null);
        }
    }
}
