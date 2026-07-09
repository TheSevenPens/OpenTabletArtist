using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using OpenTabletArtist.Services;
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

    // #437: screenshot every page. The window owns both the shell navigation and the page visual, so the
    // loop lives on MainWindow; this button just drives it and reports the result.
    private async void OnScreenshotAllPages(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not MainWindow window)
        {
            SetStatus("Couldn't reach the main window to capture pages.");
            return;
        }
        try
        {
            SetStatus("Capturing every page…");
            var count = await window.CaptureAllPagesAsync();
            SetStatus($"Saved {count} page screenshot(s) to {PageScreenshot.Directory()}.");
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
