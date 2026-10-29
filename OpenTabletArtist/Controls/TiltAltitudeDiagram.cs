using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenTabletArtist.Helpers;

namespace OpenTabletArtist.Controls;

/// <summary>
/// How far the pen is tipped over, seen from the side: the tablet as a line, a quarter protractor over it, and
/// the pen rising from the tip at its real angle above the surface (upright is straight up, lying flat is along
/// the tablet). The view is taken in the plane the pen leans in, and the pen is always drawn leaning right: only
/// the angle matters here, and a pen that jumped between the left and the right as the lean direction crossed
/// the line would read as a change in altitude that did not happen. The direction it leans is the other
/// diagram's job. The value is null before the first report, and nothing is drawn but the protractor.
/// </summary>
public sealed class TiltAltitudeDiagram : Control
{
    public static readonly StyledProperty<double?> AltitudeProperty =
        AvaloniaProperty.Register<TiltAltitudeDiagram, double?>(nameof(Altitude));
    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<TiltAltitudeDiagram, IBrush?>(nameof(TrackBrush));
    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<TiltAltitudeDiagram, IBrush?>(nameof(LabelBrush));
    public static readonly StyledProperty<IBrush?> PenBrushProperty =
        AvaloniaProperty.Register<TiltAltitudeDiagram, IBrush?>(nameof(PenBrush));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<TiltAltitudeDiagram, IBrush?>(nameof(AccentBrush));

    /// <summary>The pen's angle above the tablet, degrees: 90 upright.</summary>
    public double? Altitude { get => GetValue(AltitudeProperty); set => SetValue(AltitudeProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? LabelBrush { get => GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public IBrush? PenBrush { get => GetValue(PenBrushProperty); set => SetValue(PenBrushProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    static TiltAltitudeDiagram()
    {
        AffectsRender<TiltAltitudeDiagram>(AltitudeProperty, TrackBrushProperty,
            LabelBrushProperty, PenBrushProperty, AccentBrushProperty);
    }

    private static Typeface UiFace => AppFonts.UiTypeface();

    protected override Size MeasureOverride(Size availableSize) => new(236, 170);

    public override void Render(DrawingContext ctx)
    {
        var track = TrackBrush ?? new SolidColorBrush(Color.FromArgb(0x55, 0x88, 0x88, 0x90));
        var label = LabelBrush ?? new SolidColorBrush(Color.FromArgb(0xAA, 0x88, 0x88, 0x90));
        var pen = PenBrush ?? Brushes.Gray;
        var accent = AccentBrush ?? Brushes.HotPink;

        var b = Bounds;
        const double left = 24, labelRoom = 30;
        double baseY = b.Height - 20;                         // the tablet's surface, with room under it
        var tip = new Point(left, baseY);                     // the tip, at the foot of the quarter dial
        double r = Math.Min(b.Width - left - labelRoom, baseY - 18);
        if (r <= 0) return;

        // The tablet, and the protractor over it: a quarter dial from flat to upright, a tick every 15 degrees.
        ctx.DrawLine(new Pen(track, 3, lineCap: PenLineCap.Round),
            new Point(tip.X - 14, baseY), new Point(tip.X + r + 4, baseY));
        var dial = new StreamGeometry();
        using (var g = dial.Open())
        {
            g.BeginFigure(new Point(tip.X + r, baseY), false);
            g.ArcTo(new Point(tip.X, baseY - r), new Size(r, r), 0, false, SweepDirection.CounterClockwise);
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(track, 1.5), dial);
        ctx.DrawLine(new Pen(track, 1.5), tip, new Point(tip.X, baseY - r));   // the upright, for reference
        var tick = new Pen(track, 1.5);
        for (int deg = 0; deg <= 90; deg += 15)
        {
            var dir = Direction(deg);
            ctx.DrawLine(tick, tip + dir * (deg % 30 == 0 ? r - 8 : r - 4), tip + dir * r);
        }
        // Labelled every 30, as the angle above the surface.
        foreach (int deg in new[] { 0, 30, 60, 90 })
            DrawCentered(ctx, $"{deg}°", tip + Direction(deg) * (r + 18), label);

        if (Altitude is double alt)
        {
            alt = Math.Clamp(alt, 0, 90);
            var top = tip + Direction(alt) * r;

            // The angle itself: a small arc from the tablet up to the pen.
            double ar = r * 0.4;
            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                g.BeginFigure(new Point(tip.X + ar, baseY), false);
                g.ArcTo(tip + Direction(alt) * ar, new Size(ar, ar), 0, false, SweepDirection.CounterClockwise);
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(accent, 2.5), arc);

            ctx.DrawLine(new Pen(pen, 6, lineCap: PenLineCap.Round), tip, top);
            ctx.DrawEllipse(accent, null, top, 7, 7);
        }

        // The tip, always on the surface.
        ctx.DrawEllipse(Altitude is null ? track : pen, null, tip, 4, 4);
    }

    // A unit vector at an angle above the surface, leaning right (screen y grows downward).
    private static Vector Direction(double degrees)
    {
        double rad = degrees * Math.PI / 180.0;
        return new Vector(Math.Cos(rad), -Math.Sin(rad));
    }

    private static void DrawCentered(DrawingContext ctx, string text, Point at, IBrush brush)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, UiFace, 12, brush);
        ctx.DrawText(ft, new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2));
    }
}
