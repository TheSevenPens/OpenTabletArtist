using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Helpers;

namespace OpenTabletArtist.Controls;

/// <summary>
/// The pen's tilt direction seen from above: a dial with the tip at the centre and the pen laid out from it the
/// way it leans, so a pen leaning right points right. How far the pen reaches from the centre is how far it is
/// tipped over: upright sits on the centre, lying flat reaches the rim. The dial is in the tablet's own axes, so
/// 0° is toward the tablet's right edge and the angle grows clockwise, toward the edge nearest you. Both values
/// are null before the first report, and nothing is drawn but the dial.
/// </summary>
public sealed class TiltAzimuthDiagram : Control
{
    public static readonly StyledProperty<double?> AzimuthProperty =
        AvaloniaProperty.Register<TiltAzimuthDiagram, double?>(nameof(Azimuth));
    public static readonly StyledProperty<double?> AltitudeProperty =
        AvaloniaProperty.Register<TiltAzimuthDiagram, double?>(nameof(Altitude));
    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<TiltAzimuthDiagram, IBrush?>(nameof(TrackBrush));
    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<TiltAzimuthDiagram, IBrush?>(nameof(LabelBrush));
    public static readonly StyledProperty<IBrush?> PenBrushProperty =
        AvaloniaProperty.Register<TiltAzimuthDiagram, IBrush?>(nameof(PenBrush));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<TiltAzimuthDiagram, IBrush?>(nameof(AccentBrush));

    /// <summary>Which way the pen leans, degrees: 0 toward +X, increasing toward +Y.</summary>
    public double? Azimuth { get => GetValue(AzimuthProperty); set => SetValue(AzimuthProperty, value); }
    /// <summary>The pen's angle above the tablet, degrees: 90 upright.</summary>
    public double? Altitude { get => GetValue(AltitudeProperty); set => SetValue(AltitudeProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? LabelBrush { get => GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public IBrush? PenBrush { get => GetValue(PenBrushProperty); set => SetValue(PenBrushProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    static TiltAzimuthDiagram()
    {
        AffectsRender<TiltAzimuthDiagram>(AzimuthProperty, AltitudeProperty, TrackBrushProperty,
            LabelBrushProperty, PenBrushProperty, AccentBrushProperty);
    }

    private static Typeface UiFace => AppFonts.UiTypeface();

    protected override Size MeasureOverride(Size availableSize) => new(236, 236);

    public override void Render(DrawingContext ctx)
    {
        var track = TrackBrush ?? new SolidColorBrush(Color.FromArgb(0x55, 0x88, 0x88, 0x90));
        var label = LabelBrush ?? new SolidColorBrush(Color.FromArgb(0xAA, 0x88, 0x88, 0x90));
        var pen = PenBrush ?? Brushes.Gray;
        var accent = AccentBrush ?? Brushes.HotPink;

        var b = Bounds;
        var c = new Point(b.Width / 2, b.Height / 2);
        double r = Math.Min(b.Width, b.Height) / 2 - 24;   // room outside the rim for the angle labels
        if (r <= 0) return;

        // The dial: a rim, a half-way ring and the two axes.
        var ring = new Pen(track, 1.5);
        ctx.DrawEllipse(null, ring, c, r, r);
        ctx.DrawEllipse(null, new Pen(track, 1, DashStyle.Dash), c, r / 2, r / 2);
        ctx.DrawLine(ring, new Point(c.X - r, c.Y), new Point(c.X + r, c.Y));
        ctx.DrawLine(ring, new Point(c.X, c.Y - r), new Point(c.X, c.Y + r));

        // Angle labels just outside the rim: 0° right, then clockwise.
        DrawCentered(ctx, "0°", new Point(c.X + r + 14, c.Y), label);
        DrawCentered(ctx, "90°", new Point(c.X, c.Y + r + 12), label);
        DrawCentered(ctx, "180°", new Point(c.X - r - 16, c.Y), label);
        DrawCentered(ctx, "270°", new Point(c.X, c.Y - r - 12), label);

        if (Azimuth is double az && Altitude is double alt)
        {
            var (fx, fy) = TiltGeometry.Footprint(az, alt);
            var top = new Point(c.X + fx * r, c.Y + fy * r);

            // The pen from the tip to its top, and a dot on the top so a short lean still reads as a direction.
            ctx.DrawLine(new Pen(pen, 6, lineCap: PenLineCap.Round), c, top);
            ctx.DrawEllipse(accent, null, top, 7, 7);
        }

        // The tip, always on the centre.
        ctx.DrawEllipse(Azimuth is null ? track : pen, null, c, 4, 4);
    }

    private static void DrawCentered(DrawingContext ctx, string text, Point at, IBrush brush)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, UiFace, 12, brush);
        ctx.DrawText(ft, new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2));
    }
}
