using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;

namespace OpenTabletArtist;

public partial class MainWindow : Window
{
    private bool _allowClose;
    // One-time "still running in the tray" hint, persisted so it only shows on the first close (#72).
    // Skipped for --background sign-in launches (#381).
    private bool _trayHintShown = Program.LaunchInBackground || AppSettings.Get("TrayHintShown") == "true";
    private ProfileSwitchService? _switchSub;  // tracked so we can unsubscribe on DataContext change / close
    private MonitorCycleService? _cycleSub;    // ditto, for the monitor-cycle toast (#89)
    private PerAppSwitcher? _perAppSub;        // ditto, for the automatic per-app switch toast (#167)

    public MainWindow()
    {
        InitializeComponent();
        // The version + BETA now lives in the sidebar footer (bound to MainViewModel.TitleBarText).
        // Title stays the bare app name so the taskbar / Alt-Tab entry reads "OpenTabletArtist" (it also
        // shows small in the extended-chrome caption top-left); the version no longer clutters it.
        Title = "OpenTabletArtist";
        // DataContext is set in XAML (<vm:MainViewModel/>), so it's already assigned here and the
        // DataContextChanged from that assignment fired inside InitializeComponent, before we could
        // handle it. Wire the switch subscription now for the current VM, and keep the handler for
        // any later DataContext change. (#320)
        OnDataContextChanged(this, EventArgs.Empty);
        DataContextChanged += OnDataContextChanged;
        // Regaining focus re-pulls settings so an external edit (e.g. the mapping changed in the OTD UX
        // while we were in the background) is picked up promptly rather than on the next fallback poll.
        Activated += (_, _) => (DataContext as MainViewModel)?.OnWindowActivated();
        Closed += (_, _) =>
        {
            if (_switchSub != null) _switchSub.Switched -= OnProfileSwitched;
            if (_cycleSub != null) _cycleSub.Cycled -= OnMonitorCycled;
            if (_perAppSub != null) _perAppSub.ActiveProfileChanged -= OnPerAppSwitched;
            ProfileToast.Dismiss();
            (DataContext as MainViewModel)?.Dispose();
        };
    }

    // Follow the VM's switch services so a hotkey-driven profile switch (#320), monitor cycle (#89), or an
    // automatic per-app switch (#167) pops a transient toast, even when the app is in the tray / behind
    // the drawing app.
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_switchSub != null) _switchSub.Switched -= OnProfileSwitched;
        if (_cycleSub != null) _cycleSub.Cycled -= OnMonitorCycled;
        if (_perAppSub != null) _perAppSub.ActiveProfileChanged -= OnPerAppSwitched;

        var vm = DataContext as MainViewModel;
        _switchSub = vm?.ProfileSwitch;
        _cycleSub = vm?.MonitorCycle;
        _perAppSub = vm?.PerAppSwitch;
        if (_switchSub != null) _switchSub.Switched += OnProfileSwitched;
        if (_cycleSub != null) _cycleSub.Cycled += OnMonitorCycled;
        if (_perAppSub != null) _perAppSub.ActiveProfileChanged += OnPerAppSwitched;
    }

    private void OnProfileSwitched(string? snapshot)
    {
        // The Switched continuation may resume off the UI thread; marshal before touching windows.
        Dispatcher.UIThread.Post(() => ProfileToast.Show(
            snapshot == null ? "Restored saved settings" : $"Switched to “{snapshot}”"));
    }

    private void OnMonitorCycled(string message) =>
        Dispatcher.UIThread.Post(() => ProfileToast.Show(message));

    // Automatic per-app switch: same transient toast as the hotkey paths, with a ⧉ glyph so it reads as an
    // app-triggered change rather than a keyboard one. The switcher dedupes by target, so this only fires
    // when the applied profile actually changes — not on every focus flip.
    private void OnPerAppSwitched(string? snapshot) =>
        Dispatcher.UIThread.Post(() => ProfileToast.Show(
            snapshot == null ? "Restored saved settings" : $"Switched to “{snapshot}”", glyph: "⧉"));

    /// <summary>Permit a real close — used by the tray's Quit. Without it, closing hides to the tray.</summary>
    public void AllowCloseForQuit() => _allowClose = true;

    // The client area is extended under the title bar (#titlebar), so the top strip is our own drag
    // handle: left-drag moves the window; double-click toggles maximize (standard title-bar behaviour).
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ClampToWorkingArea();
        // Turn off Windows' pen/touch tap-feedback rings across the app — they're just visual noise on a
        // drawing-tablet UI. Per-window; gestures (e.g. right-click) still work.
        ShellPenFeedback.DisableFor(this);
    }

    /// <summary>
    /// Shrink + re-center the window when the default size doesn't fit the current screen's working
    /// area. A 1080p display at 150% scale is only 1280x720 logical — less than the default height
    /// once the taskbar and title bar are subtracted — so without this the top edge sits above the
    /// screen and the bottom hides under the taskbar (#274).
    /// </summary>
    private void ClampToWorkingArea()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        var scaling = screen.Scaling;
        var wa = screen.WorkingArea;        // physical pixels, excludes the taskbar
        var workW = wa.Width / scaling;     // logical (DIPs)
        var workH = wa.Height / scaling;

        // Headroom so the title bar and window edges stay comfortably reachable.
        var maxW = Math.Max(MinWidth, workW - 24);
        var maxH = Math.Max(MinHeight, workH - 48);

        var newW = Math.Min(Width, maxW);
        var newH = Math.Min(Height, maxH);
        if (newW >= Width && newH >= Height) return;   // already fits

        Width = newW;
        Height = newH;

        // Re-center within the working area (Position is in physical pixels).
        Position = new PixelPoint(
            wa.X + (int)((wa.Width - newW * scaling) / 2),
            wa.Y + (int)((wa.Height - newH * scaling) / 2));
    }

    /// <summary>Surface the window from a hidden/minimized state and focus it. Used by the tray click
    /// and by a second app instance asking the running one to come forward (#191).</summary>
    public void BringToFront()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_allowClose)
        {
            // Close minimizes to the tray instead of exiting; Quit is via the tray menu (#72).
            e.Cancel = true;
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                AppSettings.Set("TrayHintShown", "true");
                _ = ShowTrayHintThenHide();
            }
            else
            {
                Hide();
            }
        }
        base.OnClosing(e);
    }

    private async Task ShowTrayHintThenHide()
    {
        await Dialogs.ShowMessageAsync(
            "Still running in the tray",
            "OpenTabletArtist is still running in the system tray. Click the tray icon to reopen it, " +
            "or right-click it and choose Quit to exit.",
            this);
        Hide();
    }

    // ── Developer screenshots (#437) ──────────────────────────────────────────────────────────────

    // Capture the whole window UI (backdrop + sidebar + the current page), not just the content host —
    // rendering the content alone drops the backdrop and leaves cards floating on transparent.
    private Control? RootVisual => Content as Control;

    // The nav-bar on-page capture control: render whatever page is currently shown.
    private void OnCapturePage(object? sender, RoutedEventArgs e)
    {
        var path = RootVisual is { } v ? PageScreenshot.Render(v, RenderScaling, "page") : null;
        _ = FlashCaptureButton(path is not null);
    }

    // Briefly confirm the capture on the button itself (no room for a status line in the nav).
    private async Task FlashCaptureButton(bool ok)
    {
        var original = CapturePageButton.Content;
        CapturePageButton.Content = ok ? "✓ Saved" : "✗ Failed";
        await Task.Delay(1100);
        CapturePageButton.Content = original;
    }

    /// <summary>Navigate every page in turn (Home, root nav leaves, each ADVANCED sub-tab), saving a
    /// screenshot of each, then restore the original page. Returns how many were saved. Driven from the
    /// Developer tab's "Screenshot all pages" button (which owns the visual + the shell it navigates).</summary>
    public async Task<int> CaptureAllPagesAsync()
    {
        if (DataContext is not MainViewModel vm || RootVisual is not { } visual) return 0;
        double scale = RenderScaling;
        var originalPage = vm.CurrentPage;
        var originalTab = vm.Advanced.SelectedTab;
        int saved = 0;
        try
        {
            foreach (var (slug, navigate) in vm.ScreenshotTargets())
            {
                navigate();
                await Task.Delay(240); // let the page bind + lay out + a frame compose before rendering

                // A tablet page: capture each of its visible sub-tabs, then restore the selected one.
                if (vm.CurrentPage is TabletDetailViewModel
                    && this.GetVisualDescendants().OfType<TabletDetailView>().FirstOrDefault() is { } tabletView)
                {
                    var tabs = tabletView.VisibleTabButtons();
                    var selected = tabs.FirstOrDefault(t => t.IsChecked == true);
                    foreach (var tab in tabs)
                    {
                        tab.IsChecked = true;
                        await Task.Delay(200);
                        if (PageScreenshot.Render(visual, scale, $"page-{slug}-{TabSlug(tab)}") is not null) saved++;
                    }
                    if (selected != null) selected.IsChecked = true;
                    continue;
                }

                if (PageScreenshot.Render(visual, scale, $"page-{slug}") is not null) saved++;
            }
        }
        finally
        {
            vm.Advanced.SelectedTab = originalTab;
            vm.CurrentPage = originalPage;
        }
        return saved;
    }

    private static string TabSlug(RadioButton tab) =>
        (tab.Content?.ToString() ?? "tab").ToLowerInvariant().Replace(' ', '-');
}
