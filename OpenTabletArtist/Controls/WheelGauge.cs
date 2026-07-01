using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OpenTabletArtist.Controls;

/// <summary>
/// A live circular gauge for an absolute wheel (touch ring): a track, a marker at the finger's current
/// position, a fill arc, and a centre readout that shows the rotation direction while turning. Purely a
/// status view — driven by <see cref="Position"/> (0..1, null before the first touch / for relative
/// wheels) and the turning flags. Bound to a WheelEditor in the Wheel tab, it makes it obvious that the
/// tablet is reporting and which way the ring is moving.
/// </summary>
public sealed class WheelGauge : Control
{
    private static readonly IBrush Track = new SolidColorBrush(Color.FromArgb(0x30, 0x88, 0x88, 0x90));
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromArgb(0xAA, 0x88, 0x88, 0x90));
    private static readonly IBrush DotRing = Brushes.White;
    private static readonly Typeface Face = new("Segoe UI");
    private static readonly Color FallbackAccent = Color.FromRgb(0xE0, 0x21, 0x8A);

    public static readonly StyledProperty<double?> PositionProperty =
        AvaloniaProperty.Register<WheelGauge, double?>(nameof(Position));
    public static readonly StyledProperty<bool> TurningClockwiseProperty =
        AvaloniaProperty.Register<WheelGauge, bool>(nameof(TurningClockwise));
    public static readonly StyledProperty<bool> TurningCounterClockwiseProperty =
        AvaloniaProperty.Register<WheelGauge, bool>(nameof(TurningCounterClockwise));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<WheelGauge, IBrush?>(nameof(AccentBrush));

    public double? Position { get => GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public bool TurningClockwise { get => GetValue(TurningClockwiseProperty); set => SetValue(TurningClockwiseProperty, value); }
    public bool TurningCounterClockwise { get => GetValue(TurningCounterClockwiseProperty); set => SetValue(TurningCounterClockwiseProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    static WheelGauge()
    {
        AffectsRender<WheelGauge>(PositionProperty, TurningClockwiseProperty,
            TurningCounterClockwiseProperty, AccentBrushProperty);
    }

    protected override Size MeasureOverride(Size availableSize) => new(132, 132);

    public override void Render(DrawingContext ctx)
    {
        var accent = (AccentBrush as ISolidColorBrush)?.Color ?? FallbackAccent;
        var accentBrush = new SolidColorBrush(accent);

        var b = Bounds;
        var center = new Point(b.Width / 2, b.Height / 2);
        double radius = Math.Min(b.Width, b.Height) / 2 - 12;
        if (radius <= 0) return;

        // Track ring.
        ctx.DrawEllipse(null, new Pen(Track, 6), center, radius, radius);

        if (Position is double norm)
        {
            norm = norm < 0 ? 0 : norm > 1 ? 1 : norm;
            // The ring's reported position increases counter-clockwise, so 360*(1-norm) makes the marker
            // travel clockwise as the finger turns clockwise. (Flip the "1 -" if it reads backwards.)
            double angleDeg = 360.0 * (1 - norm);
            double rad = (angleDeg - 90) * Math.PI / 180.0;   // -90° puts 0° at the top (12 o'clock)
            var marker = new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));

            // Fill arc: from the top clockwise round to the marker.
            if (angleDeg > 0.5)
            {
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(new Point(center.X, center.Y - radius), false);
                    g.ArcTo(marker, new Size(radius, radius), 0, angleDeg > 180, SweepDirection.Clockwise);
                }
                ctx.DrawGeometry(null, new Pen(accentBrush, 6) { LineCap = PenLineCap.Round }, geo);
            }

            ctx.DrawEllipse(accentBrush, new Pen(DotRing, 2), marker, 7, 7);
        }

        // Centre: direction glyph while turning; otherwise the position % (or an idle dash).
        if (TurningClockwise || TurningCounterClockwise)
            DrawCentered(ctx, center, TurningClockwise ? "↻" : "↺", 34, accentBrush);
        else if (Position is double p)
            DrawCentered(ctx, center, $"{p * 100:0}%", 18, Muted);
        else
            DrawCentered(ctx, center, "—", 20, Muted);
    }

    private static void DrawCentered(DrawingContext ctx, Point center, string text, double size, IBrush brush)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush);
        ctx.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
    }
}
