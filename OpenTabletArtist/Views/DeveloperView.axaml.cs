using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

public partial class DeveloperView : UserControl
{
    public DeveloperView() => InitializeComponent();

    private static readonly FilePickerFileType CaptureFileType =
        new("Calibration capture") { Patterns = new[] { "*.json" } };

    // #484: export the active tablet's calibration capture to a JSON file. The VM builds the JSON
    // (pure); the file picker lives here because it needs the window's StorageProvider.
    private async void OnExportCapture(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DeveloperViewModel vm) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var json = vm.BuildCaptureJson();
        if (json is null) return; // vm set CaptureStatus explaining why

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export calibration capture",
            SuggestedFileName = vm.SuggestedCaptureFileName,
            DefaultExtension = "json",
            FileTypeChoices = new[] { CaptureFileType },
        });

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return; // cancelled

        try
        {
            await File.WriteAllTextAsync(path, json);
            vm.NoteCaptureExported(path);
        }
        catch (Exception ex)
        {
            vm.CaptureStatus = $"Couldn't write the file: {ex.Message}";
        }
    }

    // #484: import a previously exported capture. The VM parses + match-checks it and holds it for
    // apply / re-solve.
    private async void OnImportCapture(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DeveloperViewModel vm) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import calibration capture",
            AllowMultiple = false,
            FileTypeFilter = new[] { CaptureFileType },
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return; // cancelled

        try
        {
            var json = await File.ReadAllTextAsync(path);
            vm.LoadCapture(json, Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            vm.CaptureStatus = $"Couldn't read the file: {ex.Message}";
        }
    }

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
