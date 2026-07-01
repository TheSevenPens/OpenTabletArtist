using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Controls;

/// <summary>
/// A focused picture of the tablet's active area: the full digitizer area as a large rectangle with
/// the effective (currently-used) area drawn inside it — to scale and in position. Read-only for now;
/// a future iteration will let the user resize/align the effective area here (proportional shrink,
/// edge alignment). Sibling of <see cref="ScreenMappingDiagram"/> but tablet-only (no displays/pen).
/// All text sits on the grey area box so it stays legible in any theme.
/// </summary>
public sealed class ActiveAreaDiagram : Control
{
    private static readonly IBrush FullFill = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x92));
    private static readonly IPen FullBorder = new Pen(new SolidColorBrush(Color.FromRgb(0x5C, 0x5C, 0x63)), 1.5);
    private static readonly IBrush FullLabel = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF));
    private static readonly Typeface Face = new("Segoe UI");
    private static readonly Color FallbackAccent = Color.FromRgb(0xE0, 0x21, 0x8A);

    public static readonly StyledProperty<TabletAreaInfo?> AreaProperty =
        AvaloniaProperty.Register<ActiveAreaDiagram, TabletAreaInfo?>(nameof(Area));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<ActiveAreaDiagram, IBrush?>(nameof(AccentBrush));

    public TabletAreaInfo? Area { get => GetValue(AreaProperty); set => SetValue(AreaProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    static ActiveAreaDiagram()
    {
        AffectsRender<ActiveAreaDiagram>(AreaProperty, AccentBrushProperty);
        AffectsMeasure<ActiveAreaDiagram>(AreaProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width;
        return new Size(w, 320);
    }

    public override void Render(DrawingContext ctx)
    {
        var area = Area;
        var accent = (AccentBrush as ISolidColorBrush)?.Color ?? FallbackAccent;

        const double pad = 10;
        var box = new Rect(Bounds.Size).Deflate(pad);
        if (box.Width <= 0 || box.Height <= 0) return;

        // The full digitizer area, fit to the box preserving the tablet's real aspect ratio.
        double fullW = area?.FullWidth ?? 16, fullH = area?.FullHeight ?? 10;
        var fullRect = FitAspect(fullW, fullH, box);
        ctx.DrawRectangle(FullFill, FullBorder, fullRect);
        ctx.DrawText(Text("Full tablet area", 11, FullLabel), new Point(fullRect.X + 8, fullRect.Y + 6));

        if (area is { FullWidth: > 0, FullHeight: > 0 })
        {
            // Effective area, positioned by its centre offset and scaled the same as the full area.
            double sx = fullRect.Width / area.FullWidth, sy = fullRect.Height / area.FullHeight;
            double ew = Math.Max(2, area.EffWidth * sx), eh = Math.Max(2, area.EffHeight * sy);
            var effRect = new Rect(fullRect.X + area.EffCenterX * sx - ew / 2,
                                   fullRect.Y + area.EffCenterY * sy - eh / 2, ew, eh);
            ctx.DrawRectangle(new SolidColorBrush(accent, 0.22), new Pen(new SolidColorBrush(accent), 2), effRect);
            if (effRect is { Height: > 26, Width: > 70 })
                DrawCentered(ctx, effRect, $"{area.EffWidth:0.#} × {area.EffHeight:0.#} mm", 11.5, Brushes.White);
        }
        else
        {
            DrawCentered(ctx, fullRect, "No active-area data", 12, Brushes.White);
        }
    }

    private static Rect FitAspect(double w, double h, Rect box)
    {
        if (w <= 0 || h <= 0 || box.Width <= 0 || box.Height <= 0) return box;
        double s = Math.Min(box.Width / w, box.Height / h);
        return new Rect(box.X + (box.Width - w * s) / 2, box.Y + (box.Height - h * s) / 2, w * s, h * s);
    }

    private static void DrawCentered(DrawingContext ctx, Rect area, string text, double size, IBrush brush)
    {
        var ft = Text(text, size, brush);
        ctx.DrawText(ft, new Point(area.X + (area.Width - ft.Width) / 2, area.Y + (area.Height - ft.Height) / 2));
    }

    private static FormattedText Text(string s, double size, IBrush brush) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush);
}
