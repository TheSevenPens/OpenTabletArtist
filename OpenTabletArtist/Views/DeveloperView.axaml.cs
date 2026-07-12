using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.Views;

public partial class DeveloperView : UserControl
{
    private Window? _window;

    public DeveloperView() => InitializeComponent();

    // #546: keep the "current size" readout live while the Developer page is shown, and force the window
    // to a preset size for layout previews / consistent screenshots.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            _window = window;
            window.Resized += OnWindowResized;
            UpdateWindowSize(window);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window != null)
        {
            _window.Resized -= OnWindowResized;
            _window = null;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowResized(object? sender, WindowResizedEventArgs e)
    {
        if (_window != null) UpdateWindowSize(_window);
    }

    private void UpdateWindowSize(Window window)
    {
        var scale = window.RenderScaling <= 0 ? 1 : window.RenderScaling;
        var s = window.ClientSize;
        // Report actual pixels (what the user thinks in), with the DIP size + scale as context.
        WindowSizeText.Text =
            $"{s.Width * scale:0} × {s.Height * scale:0} px   ({s.Width:0} × {s.Height:0} dip @ {scale:0.##}× scale)";
    }

    // Force the window to occupy the button's Tag size in ACTUAL PIXELS (not DIPs), regardless of display
    // scaling: Width/Height are logical, so divide by the render scale. Restore from maximized first, and
    // relax the min size so small targets aren't clamped up (previewing a cramped layout is the point).
    private void OnSetWindowSize(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || TopLevel.GetTopLevel(this) is not Window window) return;
        var parts = tag.Split(',');
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var pw)
            || !double.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var ph))
            return;

        var scale = window.RenderScaling <= 0 ? 1 : window.RenderScaling;
        window.WindowState = WindowState.Normal;
        window.MinWidth = 0;
        window.MinHeight = 0;
        window.Width = pw / scale;
        window.Height = ph / scale;
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
