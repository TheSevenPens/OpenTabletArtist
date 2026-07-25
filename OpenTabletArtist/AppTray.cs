using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist;

/// <summary>
/// System-tray icon + background mode (#72). While the app runs, a tray icon reflects daemon status
/// and offers Show / daemon control / Quit. Closing the window hides it to the tray (see
/// <see cref="MainWindow"/>); the app keeps running until Quit is chosen here.
///
/// The tray also surfaces a few tablet actions so they're reachable with the window closed:
/// an "Active Tablet" picker (#190) and a "Switch display" submenu for the active tablet (#187).
/// </summary>
public sealed class AppTray : IDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly MainWindow _window;
    private readonly IConnectionState _conn;
    private readonly IDeviceData _deviceData;
    private readonly ISettingsCoordinator _settingsCoord;
    private readonly Func<Task>? _onQuitAsync; // restore per-app default before exit (#167)

    private readonly TrayIcon _tray;
    private readonly NativeMenuItem _activeTabletItem;

    private readonly NativeMenu _activeTabletMenu;
    private readonly NativeMenuItem _switchDisplayItem;
    private readonly NativeMenu _displayMenu;
    private readonly NativeMenuItemSeparator _tabletSeparator;
    private readonly NativeMenuItem _startItem;
    private readonly NativeMenuItem _stopItem;
    private readonly NativeMenuItem _restartItem;
    private readonly NativeMenuItem _quitStopItem; // Quit + stop the daemon (#596)

    // Signatures of the last-built submenus (so the 3s data poll doesn't churn an open menu): the
    // display submenu (active tablet + monitor geometry + mapped display) and the active-tablet
    // picker (connected tablets + which is active).
    private string _displaySignature = "";
    private string _activeTabletSignature = "";

    public AppTray(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window,
        IConnectionState conn, IDeviceData deviceData, ISettingsCoordinator settingsCoord,
        Func<Task>? onQuitAsync = null)
    {
        _desktop = desktop;
        _window = window;
        _conn = conn;
        _deviceData = deviceData;
        _settingsCoord = settingsCoord;
        _onQuitAsync = onQuitAsync;

        _tray = new TrayIcon { ToolTipText = "OpenTabletArtist", IsVisible = true };
        try
        {
            using var s = AssetLoader.Open(new Uri("avares://OpenTabletArtist/Assets/appicon.png"));
            _tray.Icon = new WindowIcon(s);
        }
        catch { /* a missing icon shouldn't crash startup */ }

        var showItem = new NativeMenuItem("Show OpenTabletArtist");
        showItem.Click += (_, _) => ShowWindow();

        // Active-tablet picker (#190 phase 3): only shown when more than one tablet is connected; the
        // tablet actions below all target the active tablet.
        _activeTabletMenu = new NativeMenu();
        _activeTabletItem = new NativeMenuItem("Active Tablet") { Menu = _activeTabletMenu };

        // Tablet group (#187): the "Switch Display" submenu for the active tablet.
        _displayMenu = new NativeMenu();
        _switchDisplayItem = new NativeMenuItem("Switch Display") { Menu = _displayMenu };
        _tabletSeparator = new NativeMenuItemSeparator();

        _startItem = new NativeMenuItem("Start Daemon") { Command = _conn.StartDaemonCommand };
        _restartItem = new NativeMenuItem("Restart Daemon") { Command = _conn.RestartDaemonCommand };
        _stopItem = new NativeMenuItem("Stop Daemon") { Command = _conn.StopDaemonCommand };

        // Quit leaves the daemon running (it's a separate process); "Quit and stop the daemon" also
        // stops it (#596), shown only when there's a running daemon to stop.
        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += (_, _) => Quit();
        _quitStopItem = new NativeMenuItem("Quit and stop the daemon");
        _quitStopItem.Click += (_, _) => Quit(stopDaemon: true);

        var menu = new NativeMenu();
        menu.Items.Add(showItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_activeTabletItem);
        menu.Items.Add(_switchDisplayItem);
        menu.Items.Add(_tabletSeparator);
        menu.Items.Add(_startItem);
        menu.Items.Add(_restartItem);
        menu.Items.Add(_stopItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quitItem);
        menu.Items.Add(_quitStopItem);
        _tray.Menu = menu;

        _tray.Clicked += (_, _) => ShowWindow();

        _conn.PropertyChanged += OnConnectionChanged;
        _deviceData.DataLoaded += OnDataLoaded;
        UpdateMenu();

        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _tray });
    }

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Status came from a daemon callback (possibly off the UI thread) — marshal before touching UI.
        Dispatcher.UIThread.Post(UpdateMenu);
    }

    // The data load already raises this on the UI thread, but Post keeps it uniform with the
    // connection callback above and harmless if a future caller isn't on the dispatcher.
    private void OnDataLoaded() => Dispatcher.UIThread.Post(UpdateMenu);

    private void UpdateMenu()
    {
        var connected = _conn.IsConnected;

        // Daemon controls (unchanged): Start only when stopped AND not mid-connect; Stop/Restart
        // only when connected.
        _startItem.IsVisible = _conn.ShowStartButton;
        _restartItem.IsVisible = connected;
        _stopItem.IsVisible = connected;
        _quitStopItem.IsVisible = connected; // only offer "quit + stop" when there's a daemon to stop
        _tray.ToolTipText = $"OpenTabletArtist — {_conn.DaemonStatusText}";

        UpdateTabletItems(connected);
    }

    /// <summary>Refresh the active-tablet picker, the dynamics-reveal line, and the tablet actions —
    /// all targeting the active tablet (#190 phase 3).</summary>
    private void UpdateTabletItems(bool connected)
    {
        var activeProfile = connected ? ActiveProfile() : null;

        // #187 — switch the active tablet's mapped display. Only when it's in an Absolute mode we can
        // map (otherwise there's no display area to set).
        var mappable = activeProfile != null && IsAbsoluteMappable(activeProfile);
        _switchDisplayItem.IsVisible = mappable;
        if (mappable)
            RebuildDisplayMenu(activeProfile!);
        else
            _displaySignature = ""; // force a rebuild next time it becomes mappable

        // #190 — active-tablet picker, only when there's a choice to make (>1 connected).
        var multipleTablets = connected && _deviceData.DetectedTablets.Count > 1;
        _activeTabletItem.IsVisible = multipleTablets;
        if (multipleTablets)
            RebuildActiveTabletMenu();
        else
            _activeTabletSignature = "";

        _tabletSeparator.IsVisible = mappable || multipleTablets;
    }

    /// <summary>The detected profile for the active tablet, falling back to any detected one.</summary>
    private OpenTabletDriver.Desktop.Profiles.Profile? ActiveProfile()
    {
        var name = _deviceData.ActiveTabletName;
        return _deviceData.Profiles.FirstOrDefault(p => p.IsDetected && p.Profile.Tablet == name)?.Profile
            ?? _deviceData.Profiles.FirstOrDefault(p => p.IsDetected)?.Profile;
    }

    private void RebuildActiveTabletMenu()
    {
        var names = _deviceData.DetectedTablets.Select(t => t.Name).ToList();
        var active = _deviceData.ActiveTabletName;

        var signature = (active ?? "-") + "|" + string.Join(";", names);
        if (signature == _activeTabletSignature) return;
        _activeTabletSignature = signature;

        _activeTabletMenu.Items.Clear();
        foreach (var name in names)
        {
            var item = new NativeMenuItem(name)
            {
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = name == active,
            };
            var target = name; // capture per-iteration
            item.Click += (_, _) => { _deviceData.SetActiveTablet(target); UpdateMenu(); };
            _activeTabletMenu.Items.Add(item);
        }
    }

    private static bool IsAbsoluteMappable(OpenTabletDriver.Desktop.Profiles.Profile profile) =>
        profile.AbsoluteModeSettings != null &&
        (profile.OutputMode?.Path?.Contains("Absolute", StringComparison.OrdinalIgnoreCase) ?? false);

    private void RebuildDisplayMenu(OpenTabletDriver.Desktop.Profiles.Profile profile)
    {
        var displays = DisplayEnumerator.Enumerate();
        var mapped = DisplayMappingApplier.CurrentlyMapped(profile, displays);

        var signature = profile.Tablet + "|" + (mapped?.Number.ToString() ?? "-") + "|" +
            string.Join(";", displays.Select(d => $"{d.Number},{d.X},{d.Y},{d.Width},{d.Height}"));
        if (signature == _displaySignature) return;
        _displaySignature = signature;

        _displayMenu.Items.Clear();
        foreach (var d in displays)
        {
            var label = $"Display {d.Number}";
            if (!string.IsNullOrWhiteSpace(d.Name)) label += $" — {d.Name}";
            label += $"  ({d.Width}×{d.Height})";
            if (d.IsPrimary) label += " · primary";

            var item = new NativeMenuItem(label)
            {
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = mapped != null && d.Number == mapped.Number,
            };
            var target = d; // capture per-iteration
            item.Click += (_, _) => _ = SwitchDisplayAsync(target);
            _displayMenu.Items.Add(item);
        }
    }

    private async Task SwitchDisplayAsync(DisplayInfo display)
    {
        var settings = _settingsCoord.CurrentSettings;
        var activeName = _deviceData.ActiveTabletName;
        if (settings == null || string.IsNullOrEmpty(activeName)) return;

        // Mutate the live profile inside CurrentSettings, then persist the whole settings object —
        // same path the tablet dialog's "Apply mapping" uses.
        var profile = settings.Profiles.FirstOrDefault(p => p.Tablet == activeName);
        if (profile == null) return;

        var digitizer = _deviceData.GetTabletDigitizer(activeName);
        // Re-enumerate so the area is placed against the current monitor layout (the menu the click came
        // from may be a moment stale); the full set is needed for OTD's virtual-screen coordinates.
        var displays = DisplayEnumerator.Enumerate();
        if (!DisplayMappingApplier.ApplyToProfile(profile, digitizer, display, displays)) return;

        try { await _settingsCoord.ApplyAndSaveSettingsAsync(settings); }
        catch { /* best-effort; the next data load will resync the menu's checkmark */ }
    }

    private void ShowWindow() => _window.BringToFront();

    private async void Quit(bool stopDaemon = false)
    {
        _window.AllowCloseForQuit();
        // Restore the user's default while the daemon is still connected, so no per-app snapshot lingers
        // after exit (#167). Awaited on the UI thread (no blocking); bounded so a stuck RPC can't hang Quit.
        if (_onQuitAsync != null)
        {
            try
            {
                var restore = _onQuitAsync();
                var delay = Task.Delay(5000);
                if (await Task.WhenAny(restore, delay) == delay)
                    Trace.TraceWarning("Per-app restore on quit timed out after 5s; a snapshot may remain applied.");
            }
            catch { }
        }
        // #596 — optionally stop the daemon too (after the restore above, which needs the connection).
        // Bounded like the restore so a stuck stop can't hang Quit.
        if (stopDaemon)
        {
            try
            {
                var stop = _conn.StopDaemonCommand.ExecuteAsync(null);
                if (await Task.WhenAny(stop, Task.Delay(5000)) != stop)
                    Trace.TraceWarning("Stopping the daemon on quit timed out after 5s.");
            }
            catch { }
        }
        Dispose();
        _desktop.Shutdown(); // closes the window (→ MainViewModel.Dispose) and exits the app
    }

    public void Dispose()
    {
        _conn.PropertyChanged -= OnConnectionChanged;
        _deviceData.DataLoaded -= OnDataLoaded;
        _tray.IsVisible = false;
        if (Application.Current is { } app)
            TrayIcon.SetIcons(app, new TrayIcons());
        _tray.Dispose();
    }
}
