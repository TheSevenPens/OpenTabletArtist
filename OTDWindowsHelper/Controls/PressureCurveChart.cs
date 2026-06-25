using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using OtdWindowsHelper.Domain;

namespace OtdWindowsHelper.Controls;

/// <summary>
/// Interactive editor + visualization for the Extended pressure curve
/// (<see cref="PressureCurveSettings"/>). Draws the live curve trace and two draggable control
/// nodes — min (input/output minimum, pink) and max (input/output maximum, cyan). Softness and the
/// Clamp/Cut dead-zone are driven from the host's controls; this control owns the node geometry.
/// Adapted from PenDynamicsLab's PressureChartControl, trimmed to the single curve type we ship.
/// </summary>
public sealed class PressureCurveChart : Control
{
    private const double PadLeft = 40;
    private const double PadRight = 16;
    private const double PadTop = 16;
    private const double PadBottom = 28;
    private const double NodeRadius = 9;     // hit-test radius
    private const double NodeDrawRadius = 6;

    // Tuned for the app's dark theme rather than the reference's white chart.
    private static readonly IBrush PlotBrush = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x2A));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xB0));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), 1);
    private static readonly IPen CurvePen = new Pen(new SolidColorBrush(Color.FromRgb(0x6B, 0x6F, 0xF5)), 2.2);
    private static readonly IBrush MinNodeBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x33, 0x99));
    private static readonly IBrush MaxNodeBrush = new SolidColorBrush(Color.FromRgb(0x33, 0xCC, 0xFF));
    private static readonly IPen NodeGuidePen = new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), 1)
    { DashStyle = new DashStyle(new double[] { 3, 4 }, 0) };
    private static readonly IPen NodeOutline = new Pen(Brushes.White, 1.5);
    private static readonly IBrush LiveDotBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xD1, 0x6B));
    private static readonly IPen LiveGuidePen = new Pen(new SolidColorBrush(Color.FromArgb(0x44, 0x2E, 0xD1, 0x6B)), 1)
    { DashStyle = new DashStyle(new double[] { 3, 4 }, 0) };
    private static readonly Typeface ChartTypeface = new("Segoe UI");
    private const double ChartFontSize = 11;

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

        DrawLabels(context, plotW, plotH);
        DrawCurve(context, plotW, plotH);

        var curve = Curve;
        DrawNode(context, curve.InputMinimum, curve.Minimum, MinNodeBrush);
        DrawNode(context, curve.InputMaximum, curve.Maximum, MaxNodeBrush);

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

    private void DrawLabels(DrawingContext context, double plotW, double plotH)
    {
        for (int i = 0; i <= 4; i++)
        {
            double gx = Math.Round(PadLeft + i / 4.0 * plotW);
            var ft = Text(FormatTick(i * 0.25));
            context.DrawText(ft, new Point(gx - ft.Width / 2, Math.Round(PadTop + plotH + 6)));

            double gy = Math.Round(PadTop + plotH - i / 4.0 * plotH);
            var fy = Text(FormatTick(i * 0.25));
            context.DrawText(fy, new Point(Math.Round(PadLeft - 6) - fy.Width, gy - fy.Height / 2));
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

    private static FormattedText Text(string s) => new(
        s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, ChartTypeface, ChartFontSize, LabelBrush);

    private static string FormatTick(double v)
    {
        string s = v.ToString("0.00", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
        return s.Length == 0 ? "0" : s;
    }

    // ── Pointer interaction ─────────────────────────────────────

    private enum DragKind { None, Min, Max }
    private DragKind _dragging = DragKind.None;

    private static double Distance(Point p, double cx, double cy)
        => Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));

    private DragKind HitTest(Point p)
    {
        var (plotW, plotH) = Layout();
        var c = Curve;
        if (Distance(p, PadLeft + c.InputMinimum * plotW, PadTop + plotH - c.Minimum * plotH) <= NodeRadius)
            return DragKind.Min;
        if (Distance(p, PadLeft + c.InputMaximum * plotW, PadTop + plotH - c.Maximum * plotH) <= NodeRadius)
            return DragKind.Max;
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
            Cursor = HitTest(pos) != DragKind.None ? new Cursor(StandardCursorType.SizeAll) : Cursor.Default;
            return;
        }

        double inVal = Round2(XValueFromCanvas(pos.X));
        double outVal = Round2(YValueFromCanvas(pos.Y));
        var c = Curve;
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
