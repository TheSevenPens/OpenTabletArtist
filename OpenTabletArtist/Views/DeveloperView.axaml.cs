using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.Views;

public partial class DeveloperView : UserControl
{
    public DeveloperView() => InitializeComponent();

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
