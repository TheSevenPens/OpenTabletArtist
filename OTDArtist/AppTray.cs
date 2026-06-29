using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using OtdArtist.Services;

namespace OtdArtist;

/// <summary>
/// System-tray icon + background mode (#72). While the app runs, a tray icon reflects daemon status
/// and offers Show / Start-or-Stop / Restart / Quit. Closing the window hides it to the tray (see
/// <see cref="MainWindow"/>); the app keeps running until Quit is chosen here.
/// </summary>
public sealed class AppTray : IDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly MainWindow _window;
    private readonly IConnectionState _conn;
    private readonly TrayIcon _tray;
    private readonly NativeMenuItem _startItem;
    private readonly NativeMenuItem _stopItem;
    private readonly NativeMenuItem _restartItem;

    public AppTray(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window, IConnectionState conn)
    {
        _desktop = desktop;
        _window = window;
        _conn = conn;

        _tray = new TrayIcon { ToolTipText = "OTD Artist", IsVisible = true };
        try
        {
            using var s = AssetLoader.Open(new Uri("avares://OtdArtist/Assets/appicon.png"));
            _tray.Icon = new WindowIcon(s);
        }
        catch { /* a missing icon shouldn't crash startup */ }

        var showItem = new NativeMenuItem("Show OTD Artist");
        showItem.Click += (_, _) => ShowWindow();

        _startItem = new NativeMenuItem("Start Daemon") { Command = _conn.StartDaemonCommand };
        _restartItem = new NativeMenuItem("Restart Daemon") { Command = _conn.RestartDaemonCommand };
        _stopItem = new NativeMenuItem("Stop Daemon") { Command = _conn.StopDaemonCommand };

        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += (_, _) => Quit();

        var menu = new NativeMenu();
        menu.Items.Add(showItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_startItem);
        menu.Items.Add(_restartItem);
        menu.Items.Add(_stopItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quitItem);
        _tray.Menu = menu;

        _tray.Clicked += (_, _) => ShowWindow();

        _conn.PropertyChanged += OnConnectionChanged;
        UpdateMenu();

        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _tray });
    }

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Status came from a daemon callback (possibly off the UI thread) — marshal before touching UI.
        Dispatcher.UIThread.Post(UpdateMenu);
    }

    private void UpdateMenu()
    {
        var connected = _conn.IsConnected;
        // Offer Start only when stopped AND not mid-connect (mirrors the dashboard's ShowStartButton);
        // while connecting, no daemon actions show. Stop/Restart only when connected.
        _startItem.IsVisible = _conn.ShowStartButton;
        _restartItem.IsVisible = connected;
        _stopItem.IsVisible = connected;
        _tray.ToolTipText = $"OTD Artist — {_conn.DaemonStatusText}";
    }

    private void ShowWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void Quit()
    {
        _window.AllowCloseForQuit();
        Dispose();
        _desktop.Shutdown(); // closes the window (→ MainViewModel.Dispose) and exits the app
    }

    public void Dispose()
    {
        _conn.PropertyChanged -= OnConnectionChanged;
        _tray.IsVisible = false;
        if (Application.Current is { } app)
            TrayIcon.SetIcons(app, new TrayIcons());
        _tray.Dispose();
    }
}
