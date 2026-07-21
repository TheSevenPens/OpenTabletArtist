using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Controls;

/// <summary>
/// A small, non-interactive preview of a pressure-curve preset — a bordered box with just the curve
/// trace (no grid, nodes, or labels), used as the visual for the curve-preset picker instead of a text
/// button. Takes a <see cref="Softness"/> (the only axis our presets vary) and draws the resulting shape
/// through the same shared <see cref="PressureCurve.Apply"/> math as the full <see cref="PressureCurveChart"/>,
/// so a thumbnail matches exactly what applying the preset produces.
/// </summary>
public sealed class PressureCurveThumbnail : Control
{
    private const double Pad = 3;

    private static readonly IBrush PlotBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00)), 1);
    private static readonly IPen CurvePen = new Pen(new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1)), 1.6);

    /// <summary>Preset softness in the same units as <see cref="PressureCurveSettings.Softness"/>
    /// (0 = linear, &gt;0 concave/soft, &lt;0 convex/hard).</summary>
    public static readonly StyledProperty<double> SoftnessProperty =
        AvaloniaProperty.Register<PressureCurveThumbnail, double>(nameof(Softness));

    public double Softness
    {
        get => GetValue(SoftnessProperty);
        set => SetValue(SoftnessProperty, value);
    }

    static PressureCurveThumbnail()
    {
        AffectsRender<PressureCurveThumbnail>(SoftnessProperty);
    }

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(0.5, 0.5, Bounds.Width - 1, Bounds.Height - 1);
        context.FillRectangle(PlotBrush, rect);
        context.DrawRectangle(null, BorderPen, rect);

        double plotW = Bounds.Width - 2 * Pad;
        double plotH = Bounds.Height - 2 * Pad;
        if (plotW <= 0 || plotH <= 0) return;

        var curve = PressureCurveSettings.Default with { Softness = Softness };
        var figure = new PathFigure { IsClosed = false, Segments = new PathSegments() };
        bool started = false;
        for (int px = 0; px <= (int)plotW; px++)
        {
            double x = px / plotW;
            double y = PressureCurve.Apply(x, curve);
            var pt = new Point(Pad + px, Pad + plotH - y * plotH);
            if (!started) { figure.StartPoint = pt; started = true; }
            else figure.Segments.Add(new LineSegment { Point = pt });
        }
        context.DrawGeometry(null, CurvePen, new PathGeometry { Figures = new PathFigures { figure } });
    }
}
