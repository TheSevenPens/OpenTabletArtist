using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenTabletArtist.Helpers;
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
    private static Typeface UiFace => AppFonts.UiTypeface();
    private static readonly Color FallbackAccent = Color.FromRgb(0xE0, 0x21, 0x8A);

    public static readonly StyledProperty<TabletAreaInfo?> AreaProperty =
        AvaloniaProperty.Register<ActiveAreaDiagram, TabletAreaInfo?>(nameof(Area));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<ActiveAreaDiagram, IBrush?>(nameof(AccentBrush));
    public static readonly StyledProperty<bool> UseImperialUnitsProperty =
        AvaloniaProperty.Register<ActiveAreaDiagram, bool>(nameof(UseImperialUnits));

    public TabletAreaInfo? Area { get => GetValue(AreaProperty); set => SetValue(AreaProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    /// <summary>Label the effective-area size in inches instead of millimetres (matches the tab's toggle).</summary>
    public bool UseImperialUnits { get => GetValue(UseImperialUnitsProperty); set => SetValue(UseImperialUnitsProperty, value); }

    static ActiveAreaDiagram()
    {
        AffectsRender<ActiveAreaDiagram>(AreaProperty, AccentBrushProperty, UseImperialUnitsProperty);
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

        double fullW = area?.FullWidth ?? 16, fullH = area?.FullHeight ?? 10;
        double rot = area != null ? (((area.Rotation % 360) + 360) % 360) : 0;

        // ── Un-rotated (the common case): full tablet with the effective area in its stored position. ──
        if (rot < 0.5)
        {
            var fullRect = FitAspect(fullW, fullH, box);
            ctx.DrawRectangle(FullFill, FullBorder, fullRect);
            ctx.DrawText(Text("Full tablet area", 11, FullLabel), new Point(fullRect.X + 8, fullRect.Y + 6));

            if (area is { FullWidth: > 0, FullHeight: > 0 })
            {
                double sx = fullRect.Width / area.FullWidth, sy = fullRect.Height / area.FullHeight;
                double ew0 = Math.Max(2, area.EffWidth * sx), eh0 = Math.Max(2, area.EffHeight * sy);
                var effRect0 = new Rect(fullRect.X + area.EffCenterX * sx - ew0 / 2,
                                        fullRect.Y + area.EffCenterY * sy - eh0 / 2, ew0, eh0);
                ctx.DrawRectangle(new SolidColorBrush(accent, 0.22), new Pen(new SolidColorBrush(accent), 2), effRect0);
                if (effRect0 is { Height: > 26, Width: > 70 })
                    DrawCentered(ctx, effRect0, FormatSize(area.EffWidth, area.EffHeight), 11.5, Brushes.White);
            }
            else
            {
                DrawCentered(ctx, fullRect, "No active-area data", 12, Brushes.White);
            }
            return;
        }

        // ── Rotated (#199, Option 2): the tablet is drawn TURNED as physically held, with the active
        //    area upright (it matches the display). A marker points to the tablet's native top edge so
        //    the direction (90 vs 270 / left vs right) is unambiguous. Areas are centred when rotated. ──
        bool perp = Math.Abs(rot % 180) > 0.5;                 // 90/270 swap the on-screen bounding box
        double bboxW = perp ? fullH : fullW, bboxH = perp ? fullW : fullH;
        var fit = FitAspect(bboxW, bboxH, box);
        double scale = fit.Height / bboxH;
        var center = fit.Center;

        // Avalonia rotates clockwise for positive angles (Y-down); the tablet turns opposite OTD's
        // -Rotation, so draw it at -rot to match the physical turn.
        var m = Matrix.CreateTranslation(-center.X, -center.Y)
                * Matrix.CreateRotation(-rot * Math.PI / 180.0)
                * Matrix.CreateTranslation(center.X, center.Y);
        var tabletRect = new Rect(center.X - fullW * scale / 2, center.Y - fullH * scale / 2,
                                  fullW * scale, fullH * scale);
        using (ctx.PushTransform(m))
        {
            ctx.DrawRectangle(FullFill, FullBorder, tabletRect);
            DrawTopMarker(ctx, tabletRect, accent);
        }

        if (area is { FullWidth: > 0, FullHeight: > 0 })
        {
            double ew = Math.Max(2, area.EffWidth * scale), eh = Math.Max(2, area.EffHeight * scale);
            var effRect = new Rect(center.X - ew / 2, center.Y - eh / 2, ew, eh);
            ctx.DrawRectangle(new SolidColorBrush(accent, 0.22), new Pen(new SolidColorBrush(accent), 2), effRect);
            if (effRect is { Height: > 26, Width: > 70 })
                DrawCentered(ctx, effRect, FormatSize(area.EffWidth, area.EffHeight), 11.5, Brushes.White);
        }
        ctx.DrawText(Text("Full tablet area", 11, FullLabel), new Point(box.X + 2, box.Y + 2));
    }

    // A small filled triangle at the centre of the tablet's native top edge, pointing "up" in the
    // tablet's own frame — drawn inside the tablet transform so it turns with the tablet, showing which
    // way it's physically rotated.
    private static void DrawTopMarker(DrawingContext ctx, Rect tablet, Color accent)
    {
        double cx = tablet.X + tablet.Width / 2;
        double s = Math.Clamp(tablet.Width * 0.06, 7, 16);
        double top = tablet.Y + 6;
        var geo = new PolylineGeometry(new[]
        {
            new Point(cx, top),                    // apex (points up in tablet frame)
            new Point(cx - s, top + s * 1.4),
            new Point(cx + s, top + s * 1.4),
        }, true);
        ctx.DrawGeometry(new SolidColorBrush(accent), null, geo);
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
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, UiFace, size, brush);

    // Effective-area label in the current unit (OTD areas are mm; inches = mm / 25.4).
    private string FormatSize(double wMm, double hMm)
    {
        if (!UseImperialUnits)
            return $"{wMm:0.#} × {hMm:0.#} mm";
        return $"{wMm / 25.4:0.##} × {hMm / 25.4:0.##} in";
    }
}
