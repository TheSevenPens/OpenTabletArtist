using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OpenTabletArtist.Controls;

/// <summary>
/// A compact horizontal gauge of the live pen pressure (#559): a track with a "raw" dot at the incoming
/// pressure and, when pen dynamics are shaping pressure, a second "processed" dot at the value after the
/// curve. Both are 0..1; null (pen up) hides the dots. Brushes are passed in so it follows the theme.
/// </summary>
public sealed class PressureLevelBar : Control
{
    public static readonly StyledProperty<double?> RawProperty =
        AvaloniaProperty.Register<PressureLevelBar, double?>(nameof(Raw));
    public static readonly StyledProperty<double?> ProcessedProperty =
        AvaloniaProperty.Register<PressureLevelBar, double?>(nameof(Processed));
    /// <summary>Show the processed dot (only when the curve/smoothing actually change pressure).</summary>
    public static readonly StyledProperty<bool> ShowProcessedProperty =
        AvaloniaProperty.Register<PressureLevelBar, bool>(nameof(ShowProcessed));
    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<PressureLevelBar, IBrush?>(nameof(TrackBrush));
    public static readonly StyledProperty<IBrush?> RawBrushProperty =
        AvaloniaProperty.Register<PressureLevelBar, IBrush?>(nameof(RawBrush));
    public static readonly StyledProperty<IBrush?> ProcessedBrushProperty =
        AvaloniaProperty.Register<PressureLevelBar, IBrush?>(nameof(ProcessedBrush));

    public double? Raw { get => GetValue(RawProperty); set => SetValue(RawProperty, value); }
    public double? Processed { get => GetValue(ProcessedProperty); set => SetValue(ProcessedProperty, value); }
    public bool ShowProcessed { get => GetValue(ShowProcessedProperty); set => SetValue(ShowProcessedProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? RawBrush { get => GetValue(RawBrushProperty); set => SetValue(RawBrushProperty, value); }
    public IBrush? ProcessedBrush { get => GetValue(ProcessedBrushProperty); set => SetValue(ProcessedBrushProperty, value); }

    static PressureLevelBar()
    {
        AffectsRender<PressureLevelBar>(RawProperty, ProcessedProperty, ShowProcessedProperty,
            TrackBrushProperty, RawBrushProperty, ProcessedBrushProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 240 : availableSize.Width;
        return new Size(w, 18);
    }

    public override void Render(DrawingContext ctx)
    {
        var b = Bounds;
        const double pad = 8, trackH = 6, dotR = 6;
        double y = b.Height / 2;
        double x0 = pad, x1 = b.Width - pad, w = x1 - x0;
        if (w <= 0) return;

        // Track.
        var track = TrackBrush ?? new SolidColorBrush(Color.FromArgb(0x33, 0x88, 0x88, 0x88));
        ctx.DrawRectangle(track, null,
            new RoundedRect(new Rect(x0, y - trackH / 2, w, trackH), trackH / 2));

        // Filled portion up to the raw pressure.
        var rawBrush = RawBrush ?? Brushes.Gray;
        if (Raw is { } raw)
        {
            double rx = x0 + Clamp01(raw) * w;
            ctx.DrawRectangle(new SolidColorBrush(AsColor(rawBrush), 0.35), null,
                new RoundedRect(new Rect(x0, y - trackH / 2, rx - x0, trackH), trackH / 2));

            // Processed dot first (under the raw dot) so the raw reading stays legible.
            if (ShowProcessed && Processed is { } proc)
            {
                double px = x0 + Clamp01(proc) * w;
                ctx.DrawEllipse(ProcessedBrush ?? Brushes.LimeGreen, null, new Point(px, y), dotR - 1, dotR - 1);
            }

            // Raw dot with a thin white ring so it reads on any track colour.
            ctx.DrawEllipse(rawBrush, new Pen(Brushes.White, 1.5), new Point(rx, y), dotR, dotR);
        }
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
    private static Color AsColor(IBrush b) => (b as ISolidColorBrush)?.Color ?? Colors.Gray;
}
