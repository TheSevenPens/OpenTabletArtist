using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Controls;

/// <summary>
/// What a calibration actually corrected, drawn per point: a hollow ring where the target was, a filled
/// dot where the uncorrected pen landed, and a line between them.
/// <para>
/// This exists because the error is a <em>field</em>, not a figure. A tablet's drift usually grows toward
/// one corner, so a single average — even the RMS the fit already reports — hides the thing you would want
/// to know: <em>where</em> it is worst. It also answers, without an essay, why anyone would pick 25 points
/// over 4: you can see the variation that four samples would smooth away.
/// </para>
/// <para>
/// The same visual language as <see cref="CalibrationDots"/> (a screen rectangle with target dots), so the
/// picker and the result read as the same diagram in two states.
/// </para>
/// </summary>
public sealed class CalibrationErrorMap : Control
{
    private static readonly IBrush ScreenFill = new SolidColorBrush(Color.FromArgb(0x14, 0x80, 0x80, 0x80));
    private static readonly IPen ScreenBorder = new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0x80, 0x80, 0x80)), 1);
    private static readonly IBrush FallbackDot = new SolidColorBrush(Color.FromRgb(0xE0, 0x21, 0x8A));

    public static readonly StyledProperty<IReadOnlyList<CalibrationReportPoint>?> PointsProperty =
        AvaloniaProperty.Register<CalibrationErrorMap, IReadOnlyList<CalibrationReportPoint>?>(nameof(Points));

    /// <summary>How far the offsets are drawn relative to life size. Real parallax is a few pixels on a
    /// diagram a few hundred wide, so at 1× every pair would sit on top of itself. The view labels the
    /// factor next to the map — an unlabelled exaggeration reads as a much worse tablet than you have.</summary>
    public static readonly StyledProperty<double> ExaggerationProperty =
        AvaloniaProperty.Register<CalibrationErrorMap, double>(nameof(Exaggeration), 8.0);

    /// <summary>Colour of the measured dot and the connecting line (the theme accent).</summary>
    public static readonly StyledProperty<IBrush?> DotBrushProperty =
        AvaloniaProperty.Register<CalibrationErrorMap, IBrush?>(nameof(DotBrush));

    /// <summary>Colour of the hollow target ring — neutral ink, so the accent means "where it went".</summary>
    public static readonly StyledProperty<IBrush?> TargetBrushProperty =
        AvaloniaProperty.Register<CalibrationErrorMap, IBrush?>(nameof(TargetBrush));

    public IReadOnlyList<CalibrationReportPoint>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }
    public double Exaggeration { get => GetValue(ExaggerationProperty); set => SetValue(ExaggerationProperty, value); }
    public IBrush? DotBrush { get => GetValue(DotBrushProperty); set => SetValue(DotBrushProperty, value); }
    public IBrush? TargetBrush { get => GetValue(TargetBrushProperty); set => SetValue(TargetBrushProperty, value); }

    static CalibrationErrorMap() =>
        AffectsRender<CalibrationErrorMap>(PointsProperty, ExaggerationProperty, DotBrushProperty, TargetBrushProperty);

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 320 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 190 : availableSize.Height);

    public override void Render(DrawingContext ctx)
    {
        var b = Bounds;
        if (b.Width <= 2 || b.Height <= 2) return;

        ctx.DrawRectangle(ScreenFill, ScreenBorder, new RoundedRect(new Rect(0.5, 0.5, b.Width - 1, b.Height - 1), 4));

        // Only points that recorded a pixel-equivalent can be drawn; legacy captures stored NaN there.
        var pts = Points?.Where(p => !float.IsNaN(p.MeasuredX) && !float.IsNaN(p.MeasuredY)).ToList();
        if (pts is not { Count: > 0 }) return;

        // Normalise against the targets' own bounding box rather than the display rectangle: the targets
        // are inset from the screen edges anyway, so this fills the diagram without needing to know which
        // display the capture came from. A single row or column would divide by zero — centre it instead.
        double minX = pts.Min(p => p.TargetX), maxX = pts.Max(p => p.TargetX);
        double minY = pts.Min(p => p.TargetY), maxY = pts.Max(p => p.TargetY);
        double spanX = maxX - minX, spanY = maxY - minY;

        const double pad = 22;
        double innerW = b.Width - 2 * pad, innerH = b.Height - 2 * pad;
        double scaleX = spanX > 0.001 ? innerW / spanX : 0;
        double scaleY = spanY > 0.001 ? innerH / spanY : 0;

        var dotBrush = DotBrush ?? FallbackDot;
        var ringPen = new Pen(TargetBrush ?? Brushes.Gray, 1.2);
        var linePen = new Pen(dotBrush, 1.4);
        double ringR = pts.Count > 16 ? 3 : 4;
        double dotR = pts.Count > 16 ? 2.2 : 3;

        foreach (var p in pts)
        {
            double tx = pad + (scaleX > 0 ? (p.TargetX - minX) * scaleX : innerW / 2);
            double ty = pad + (scaleY > 0 ? (p.TargetY - minY) * scaleY : innerH / 2);

            // The offset is drawn in the diagram's own scale, then exaggerated — so a 3px error on a
            // 3840px-wide display is still visible on a 320px-wide map.
            double mx = tx + (p.MeasuredX - p.TargetX) * (scaleX > 0 ? scaleX : 1) * Exaggeration;
            double my = ty + (p.MeasuredY - p.TargetY) * (scaleY > 0 ? scaleY : 1) * Exaggeration;

            var target = new Point(tx, ty);
            var measured = new Point(mx, my);
            ctx.DrawLine(linePen, target, measured);
            ctx.DrawEllipse(null, ringPen, target, ringR, ringR);
            ctx.DrawEllipse(dotBrush, null, measured, dotR, dotR);
        }
    }
}
