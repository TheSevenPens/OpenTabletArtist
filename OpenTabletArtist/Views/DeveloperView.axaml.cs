using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;

namespace OpenTabletArtist.Views;

public partial class DeveloperView : UserControl
{
    public DeveloperView() => InitializeComponent();

    // #437: render the live app window to a PNG via RenderTargetBitmap. Done in code-behind because it
    // needs the visual (the view model can't render). Captures at physical resolution (RenderScaling), so
    // the PNG is crisp on scaled displays. Saved to Pictures\OpenTabletArtist with a timestamped name.
    private void OnCaptureScreenshot(object? sender, RoutedEventArgs e)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            var visual = (top as Window)?.Content as Control ?? top as Control;
            if (visual is null || visual.Bounds.Width < 1 || visual.Bounds.Height < 1)
            {
                SetStatus("Nothing to capture yet — try again once the window is fully laid out.");
                return;
            }

            double scale = top?.RenderScaling ?? 1.0;
            var pixels = new PixelSize(
                Math.Max(1, (int)Math.Ceiling(visual.Bounds.Width * scale)),
                Math.Max(1, (int)Math.Ceiling(visual.Bounds.Height * scale)));

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "OpenTabletArtist");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"OTA-{DateTime.Now:yyyyMMdd-HHmmss}.png");

            using (var rtb = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale)))
            {
                rtb.Render(visual);
                rtb.Save(path);
            }

            SetStatus($"Saved {path} ({pixels.Width}×{pixels.Height}).");
        }
        catch (Exception ex)
        {
            SetStatus($"Capture failed: {ex.Message}");
        }
    }

    private void SetStatus(string text)
    {
        ScreenshotStatus.Text = text;
        ScreenshotStatus.IsVisible = true;
    }
}
