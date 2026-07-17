using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace OpenTabletArtist.Services;

/// <summary>Which image format(s) the developer screenshot aids write. Default is <see cref="PNG"/>.</summary>
public enum ScreenshotFormat { PNG, JPG, Both }

/// <summary>
/// Renders app controls to image files for the developer screenshot aids (#437). Captures the live visual
/// tree at physical resolution (RenderScaling) so the image is crisp on scaled displays. Saved under
/// <c>Pictures\OpenTabletArtist</c>; the "screenshot all pages" sweep groups its shots in a timestamped
/// sub-folder.
/// </summary>
public static class PageScreenshot
{
    private const int JpegQuality = 92;

    /// <summary>The base folder all screenshots live under (<c>Pictures\OpenTabletArtist</c>).</summary>
    public static string Directory()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "OpenTabletArtist");
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Create (and return) a fresh timestamped sub-folder under <see cref="Directory"/> for one
    /// "screenshot all pages" run, so a burst's shots are grouped together instead of scattered.</summary>
    public static string CreateSweepDirectory()
    {
        var dir = Path.Combine(Directory(), $"pages-{DateTime.Now:yyyyMMdd-HHmmss}");
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Render <paramref name="visual"/> into <paramref name="dir"/> as <paramref name="baseName"/>
    /// with the requested format(s). Returns the number of files written (0 if the control isn't laid out
    /// yet). Used by the sweep, where the folder already carries the timestamp.</summary>
    public static int RenderTo(Control visual, double scale, string baseName, string dir, ScreenshotFormat format)
    {
        if (visual.Bounds.Width < 1 || visual.Bounds.Height < 1) return 0;
        var pixels = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(visual.Bounds.Width * scale)),
            Math.Max(1, (int)Math.Ceiling(visual.Bounds.Height * scale)));
        using var rtb = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        rtb.Render(visual);

        int saved = 0;
        if (format is ScreenshotFormat.PNG or ScreenshotFormat.Both)
        {
            rtb.Save(Path.Combine(dir, baseName + ".png"));
            saved++;
        }
        if (format is ScreenshotFormat.JPG or ScreenshotFormat.Both)
        {
            SaveJpeg(rtb, Path.Combine(dir, baseName + ".jpg"));
            saved++;
        }
        return saved;
    }

    /// <summary>Render <paramref name="visual"/> to a timestamped file in the base folder, in the requested
    /// format(s); returns the number of files written. The <paramref name="suffix"/> keeps a burst from
    /// colliding within the same second. Used by the on-page Capture button.</summary>
    public static int Render(Control visual, double scale, string suffix, ScreenshotFormat format) =>
        RenderTo(visual, scale, $"OTA-{suffix}-{DateTime.Now:yyyyMMdd-HHmmss}", Directory(), format);

    // Avalonia's Bitmap.Save only writes PNG, so re-encode through SkiaSharp (already referenced for the
    // Test-tab paint surface) for JPEG. The PNG round-trip is fine for a developer aid.
    private static void SaveJpeg(RenderTargetBitmap rtb, string path)
    {
        using var png = new MemoryStream();
        rtb.Save(png);
        png.Position = 0;
        using var bitmap = SKBitmap.Decode(png);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        using var file = File.Create(path);
        data.SaveTo(file);
    }
}
