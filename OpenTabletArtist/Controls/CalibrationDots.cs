using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OpenTabletArtist.Controls;

/// <summary>
/// A tiny "screen with calibration targets" diagram for the calibration cards: a rounded rectangle
/// (the display) with an N×N grid of dots — 4 points → 2×2 (corners), 9 → 3×3, 25 → 5×5. Dot colour is
/// the theme accent via <see cref="DotBrush"/>.
/// </summary>
public sealed class CalibrationDots : Control
{
    private static readonly IBrush ScreenFill = new SolidColorBrush(Color.FromArgb(0x14, 0x80, 0x80, 0x80));
    private static readonly IPen ScreenBorder = new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0x80, 0x80, 0x80)), 1);
    private static readonly IBrush FallbackDot = new SolidColorBrush(Color.FromRgb(0xE0, 0x21, 0x8A));

    public static readonly StyledProperty<int> PointsProperty =
        AvaloniaProperty.Register<CalibrationDots, int>(nameof(Points), 4);
    public static readonly StyledProperty<IBrush?> DotBrushProperty =
        AvaloniaProperty.Register<CalibrationDots, IBrush?>(nameof(DotBrush));

    public int Points { get => GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public IBrush? DotBrush { get => GetValue(DotBrushProperty); set => SetValue(DotBrushProperty, value); }

    static CalibrationDots() => AffectsRender<CalibrationDots>(PointsProperty, DotBrushProperty);

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 96 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 60 : availableSize.Height);

    public override void Render(DrawingContext ctx)
    {
        var b = Bounds;
        if (b.Width <= 2 || b.Height <= 2) return;

        ctx.DrawRectangle(ScreenFill, ScreenBorder, new RoundedRect(new Rect(0.5, 0.5, b.Width - 1, b.Height - 1), 4));

        int n = Points switch { <= 4 => 2, 9 => 3, 25 => 5, _ => Math.Max(2, (int)Math.Round(Math.Sqrt(Points))) };
        var dot = DotBrush ?? FallbackDot;
        double pad = 10;
        double innerW = b.Width - 2 * pad, innerH = b.Height - 2 * pad;
        double r = n >= 5 ? 1.7 : 2.6;

        for (int iy = 0; iy < n; iy++)
        for (int ix = 0; ix < n; ix++)
        {
            double x = pad + (n == 1 ? innerW / 2 : ix / (double)(n - 1) * innerW);
            double y = pad + (n == 1 ? innerH / 2 : iy / (double)(n - 1) * innerH);
            ctx.DrawEllipse(dot, null, new Point(x, y), r, r);
        }
    }
}
