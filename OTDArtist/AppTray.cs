using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using OtdArtist.Domain;
using OtdArtist.Services;

namespace OtdArtist;

/// <summary>
/// System-tray icon + background mode (#72). While the app runs, a tray icon reflects daemon status
/// and offers Show / daemon control / Quit. Closing the window hides it to the tray (see
/// <see cref="MainWindow"/>); the app keeps running until Quit is chosen here.
///
/// The tray also surfaces a few tablet actions so they're reachable with the window closed:
/// a read-only line revealing whether pen dynamics are affecting the pen (#186), and "Open Tablet
/// Settings" + a "Switch display" submenu for the detected tablet (#187).
/// </summary>
public sealed class AppTray : IDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly MainWindow _window;
    private readonly IConnectionState _conn;
    private readonly IDeviceData _deviceData;
    private readonly ISettingsCoordinator _settingsCoord;
    private readonly IDialogService _dialogs;

    private readonly TrayIcon _tray;
    private readonly NativeMenuItem _dynamicsItem;
    private readonly NativeMenuItem _openSettingsItem;
    private readonly NativeMenuItem _switchDisplayItem;
    private readonly NativeMenu _displayMenu;
    private readonly NativeMenuItemSeparator _tabletSeparator;
    private readonly NativeMenuItem _startItem;
    private readonly NativeMenuItem _stopItem;
    private readonly NativeMenuItem _restartItem;

    // Signature of the last-built display submenu (detected tablet + monitor geometry + mapped
    // display); lets us skip rebuilding the submenu when nothing relevant changed, so the 3s data
    // poll doesn't churn the menu under the user.
    private string _displaySignature = "";

    public AppTray(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window,
        IConnectionState conn, IDeviceData deviceData, ISettingsCoordinator settingsCoord, IDialogService dialogs)
    {
        _desktop = desktop;
        _window = window;
        _conn = conn;
        _deviceData = deviceData;
        _settingsCoord = settingsCoord;
        _dialogs = dialogs;

        _tray = new TrayIcon { ToolTipText = "OTD Artist", IsVisible = true };
        try
        {
            using var s = AssetLoader.Open(new Uri("avares://OtdArtist/Assets/appicon.png"));
            _tray.Icon = new WindowIcon(s);
        }
        catch { /* a missing icon shouldn't crash startup */ }

        var showItem = new NativeMenuItem("Show OTD Artist");
        showItem.Click += (_, _) => ShowWindow();

        // Tablet group (#186/#187): a non-clickable dynamics-reveal line, then the tablet actions.
        _dynamicsItem = new NativeMenuItem("Pen dynamics: off") { IsEnabled = false };
        _openSettingsItem = new NativeMenuItem("Open Tablet Settings…");
        _openSettingsItem.Click += (_, _) => _ = OpenTabletSettingsAsync();
        _displayMenu = new NativeMenu();
        _switchDisplayItem = new NativeMenuItem("Switch Display") { Menu = _displayMenu };
        _tabletSeparator = new NativeMenuItemSeparator();

        _startItem = new NativeMenuItem("Start Daemon") { Command = _conn.StartDaemonCommand };
        _restartItem = new NativeMenuItem("Restart Daemon") { Command = _conn.RestartDaemonCommand };
        _stopItem = new NativeMenuItem("Stop Daemon") { Command = _conn.StopDaemonCommand };

        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += (_, _) => Quit();

        var menu = new NativeMenu();
        menu.Items.Add(showItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_dynamicsItem);
        menu.Items.Add(_openSettingsItem);
        menu.Items.Add(_switchDisplayItem);
        menu.Items.Add(_tabletSeparator);
        menu.Items.Add(_startItem);
        menu.Items.Add(_restartItem);
        menu.Items.Add(_stopItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quitItem);
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
        _tray.ToolTipText = $"OTD Artist — {_conn.DaemonStatusText}";

        UpdateTabletItems(connected);
    }

    /// <summary>Refresh the dynamics-reveal line and the tablet actions for the detected tablet.</summary>
    private void UpdateTabletItems(bool connected)
    {
        var detected = connected
            ? _deviceData.Profiles.FirstOrDefault(p => p.IsDetected)
            : null;

        // #186 — reveal what dynamics are doing to the pen. Only meaningful with a detected tablet.
        if (detected != null)
        {
            var read = PressureCurveProfile.ReadProfile(detected.Profile);
            var enabled = read is { Enabled: true };
            var dynamics = enabled ? read!.Value.Dynamics : PenDynamicsSettings.Default;
            _dynamicsItem.Header = DynamicsStatus.Describe(enabled, dynamics);
        }
        _dynamicsItem.IsVisible = detected != null;

        // #187 — open the per-tablet settings dialog (prefer the detected tablet, else the first).
        _openSettingsItem.IsVisible = connected && _deviceData.Profiles.Count > 0;

        // #187 — switch the detected tablet's mapped display. Only when it's in an Absolute mode we
        // can map (otherwise there's no display area to set).
        var mappable = detected != null && IsAbsoluteMappable(detected.Profile);
        _switchDisplayItem.IsVisible = mappable;
        _tabletSeparator.IsVisible = _dynamicsItem.IsVisible || _openSettingsItem.IsVisible || mappable;
        if (mappable)
            RebuildDisplayMenu(detected!.Profile);
        else
            _displaySignature = ""; // force a rebuild next time it becomes mappable
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

    private async Task OpenTabletSettingsAsync()
    {
        var profile = (_deviceData.Profiles.FirstOrDefault(p => p.IsDetected)
                       ?? _deviceData.Profiles.FirstOrDefault())?.Profile;
        if (profile == null) return;

        // The dialog is owned by the main window, so it must be visible first (we may be in the tray).
        ShowWindow();
        try { await _dialogs.ShowTabletSettingsAsync(profile); }
        catch { /* dialog flow failed — nothing actionable from the tray */ }
    }

    private async Task SwitchDisplayAsync(DisplayInfo display)
    {
        var settings = _settingsCoord.CurrentSettings;
        var detectedName = _deviceData.Profiles.FirstOrDefault(p => p.IsDetected)?.Profile.Tablet;
        if (settings == null || string.IsNullOrEmpty(detectedName)) return;

        // Mutate the live profile inside CurrentSettings, then persist the whole settings object —
        // same path the tablet dialog's "Apply mapping" uses.
        var profile = settings.Profiles.FirstOrDefault(p => p.Tablet == detectedName);
        if (profile == null) return;

        var digitizer = _deviceData.GetTabletDigitizer(detectedName);
        if (!DisplayMappingApplier.ApplyToProfile(profile, digitizer, display)) return;

        try { await _settingsCoord.ApplyAndSaveSettingsAsync(settings); }
        catch { /* best-effort; the next data load will resync the menu's checkmark */ }
    }

    private void ShowWindow() => _window.BringToFront();

    private void Quit()
    {
        _window.AllowCloseForQuit();
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
