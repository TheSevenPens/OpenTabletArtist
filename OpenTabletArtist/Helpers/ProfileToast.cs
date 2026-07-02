using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace OpenTabletArtist.Helpers;

/// <summary>
/// A small transient "Switched to …" overlay shown when a profile hotkey fires (#320). It's a topmost,
/// non-activating tool window so it appears over whatever app the user is drawing in and never steals
/// keyboard focus mid-stroke. Auto-dismisses after a moment with a fade. Only one is on screen at a time
/// — a rapid second switch replaces the first. UI-thread only.
/// </summary>
public static class ProfileToast
{
    private static Window? _current;
    private static DispatcherTimer? _timer;

    private static readonly TimeSpan Linger = TimeSpan.FromMilliseconds(1900);
    private static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(200);

    /// <summary>Show a toast with the given message (e.g. «Switched to "Portrait"»). Call on the UI thread.</summary>
    public static void Show(string message)
    {
        Dismiss();

        var accent = Brush("AccentBrush", Color.FromRgb(0x7C, 0x93, 0xFF));

        var card = new Border
        {
            Background = Brush("GlassBgBrush", Color.FromArgb(0xF2, 0x20, 0x22, 0x2A)),
            BorderBrush = accent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12),
            Opacity = 0,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "⌨", // ⌨ keyboard glyph — this switch came from a hotkey
                        FontSize = 22,
                        Foreground = accent,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = message,
                        FontSize = 14,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brush("TextPrimaryBrush", Colors.White),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };
        card.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = Fade },
        };

        var window = new Window
        {
            Background = Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            Topmost = true,
            ShowActivated = false, // never grab focus from the app the user is drawing in
            Content = new Border { Padding = new Thickness(6), Child = card },
        };

        window.WindowDecorations = WindowDecorations.None;

        _current = window;
        window.Show();
        PositionBottomCenter(window);

        // Fade in once the window is laid out, then schedule the fade-out + close.
        Dispatcher.UIThread.Post(() => { if (_current == window) card.Opacity = 1; },
            DispatcherPriority.Loaded);

        _timer = new DispatcherTimer { Interval = Linger };
        _timer.Tick += (_, _) =>
        {
            _timer?.Stop();
            _timer = null;
            if (_current != window) return;
            card.Opacity = 0;
            var closeTimer = new DispatcherTimer { Interval = Fade };
            closeTimer.Tick += (_, _) =>
            {
                closeTimer.Stop();
                if (_current == window) { _current = null; window.Close(); }
            };
            closeTimer.Start();
        };
        _timer.Start();
    }

    /// <summary>Close any visible toast immediately (used before showing a new one).</summary>
    public static void Dismiss()
    {
        _timer?.Stop();
        _timer = null;
        var w = _current;
        _current = null;
        w?.Close();
    }

    private static void PositionBottomCenter(Window window)
    {
        var screen = window.Screens?.ScreenFromWindow(window) ?? window.Screens?.Primary;
        if (screen is null) return;

        var wa = screen.WorkingArea;                       // physical px, excludes the taskbar
        var size = window.FrameSize ?? window.ClientSize;  // logical (DIPs)
        var w = (int)(size.Width * screen.Scaling);
        var h = (int)(size.Height * screen.Scaling);

        window.Position = new PixelPoint(
            wa.X + (wa.Width - w) / 2,
            wa.Y + wa.Height - h - (int)(48 * screen.Scaling));
    }

    private static IBrush Brush(string key, Color fallback)
        => Application.Current?.TryFindResource(key, out var res) == true && res is IBrush b
            ? b
            : new SolidColorBrush(fallback);
}
