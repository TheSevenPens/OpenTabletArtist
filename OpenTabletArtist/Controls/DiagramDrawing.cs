using System;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using OpenTabletArtist.Helpers;

namespace OpenTabletArtist.Controls;

/// <summary>Drawing helpers shared by the tablet diagrams — <see cref="ActiveAreaDiagram"/> (drag-resize)
/// and <see cref="ScreenMappingDiagram"/> (click-to-select-display). They keep their own interaction
/// models and layout; only the low-level look (tablet-outline colours, aspect fitting, centred text, and
/// the rotate-about-centre outline) lives here so it stays in sync (#620).</summary>
internal static class DiagramDrawing
{
    /// <summary>Neutral tablet-outline fill/border used by both diagrams.</summary>
    public static readonly IBrush TabletFill = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x92));
    public static readonly IPen TabletBorder = new Pen(new SolidColorBrush(Color.FromRgb(0x5C, 0x5C, 0x63)), 1.5);

    /// <summary>Accent used when no themed <c>AccentBrush</c> is available.</summary>
    public static readonly Color FallbackAccent = Color.FromRgb(0xE0, 0x21, 0x8A);

    /// <summary>Fit a <paramref name="w"/>×<paramref name="h"/> box inside <paramref name="box"/> preserving
    /// aspect, centred.</summary>
    public static Rect FitAspect(double w, double h, Rect box)
    {
        if (w <= 0 || h <= 0 || box.Width <= 0 || box.Height <= 0) return box;
        double s = Math.Min(box.Width / w, box.Height / h);
        return new Rect(box.X + (box.Width - w * s) / 2, box.Y + (box.Height - h * s) / 2, w * s, h * s);
    }

    /// <summary>Draw <paramref name="text"/> centred within <paramref name="area"/>.</summary>
    public static void DrawCentered(DrawingContext ctx, Rect area, string text, double size, IBrush brush)
    {
        var ft = Text(text, size, brush);
        ctx.DrawText(ft, new Point(area.X + (area.Width - ft.Width) / 2, area.Y + (area.Height - ft.Height) / 2));
    }

    /// <summary>A <see cref="FormattedText"/> in the app UI face — the diagrams' single text factory.</summary>
    public static FormattedText Text(string s, double size, IBrush brush) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, AppFonts.UiTypeface(), size, brush);

    /// <summary>Draw a rectangle outline turned as physically held: rotated by −<paramref name="rotRad"/>
    /// about <paramref name="center"/>. A negligible rotation (&lt; ~0.5°) draws upright, matching each
    /// diagram's previous inline branch.</summary>
    public static void DrawRotatedOutline(DrawingContext ctx, Rect rect, Point center, double rotRad,
        IBrush fill, IPen border)
    {
        if (Math.Abs(rotRad) < 0.5 * Math.PI / 180.0)
        {
            ctx.DrawRectangle(fill, border, rect);
            return;
        }
        var m = Matrix.CreateTranslation(-center.X, -center.Y)
                * Matrix.CreateRotation(-rotRad)
                * Matrix.CreateTranslation(center.X, center.Y);
        using (ctx.PushTransform(m))
            ctx.DrawRectangle(fill, border, rect);
    }
}
