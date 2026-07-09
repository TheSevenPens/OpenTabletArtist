using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace OpenTabletArtist.Services;

/// <summary>
/// Renders app controls to PNG files for the developer screenshot aids (#437). Captures the live visual
/// tree at physical resolution (RenderScaling) so the image is crisp on scaled displays. Saved to
/// <c>Pictures\OpenTabletArtist</c> with a timestamped, suffixed name.
/// </summary>
public static class PageScreenshot
{
    public static string Directory()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "OpenTabletArtist");
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Render <paramref name="visual"/> to a timestamped PNG; returns the saved path, or null if
    /// the control isn't laid out yet. The <paramref name="suffix"/> keeps a burst (e.g. per-page or
    /// per-theme) from colliding within the same second.</summary>
    public static string? Render(Control visual, double scale, string suffix)
    {
        if (visual.Bounds.Width < 1 || visual.Bounds.Height < 1) return null;
        var pixels = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(visual.Bounds.Width * scale)),
            Math.Max(1, (int)Math.Ceiling(visual.Bounds.Height * scale)));
        var path = Path.Combine(Directory(), $"OTA-{suffix}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        using var rtb = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        rtb.Render(visual);
        rtb.Save(path);
        return path;
    }
}
