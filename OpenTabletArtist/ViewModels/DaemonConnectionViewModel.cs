using System;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// The DAEMON CONNECTION card (Daemon page, left column): whether OTA is connected to a daemon, and how
/// long this connection has been up. Wraps the shared <see cref="DaemonStatusViewModel"/> for the live
/// connection state and the Refresh command; the connection age is measured from when OTA attached, not the
/// daemon's own uptime (that's the DAEMON PROCESS card).
/// </summary>
public sealed partial class DaemonConnectionViewModel : ObservableObject, IDisposable
{
    private readonly DaemonStatusViewModel _status;
    private readonly DispatcherTimer _uptimeTimer;
    private DateTime? _connectedAt;

    public DaemonConnectionViewModel(DaemonStatusViewModel status)
    {
        _status = status;

        // Tick once a second so "Up for …" stays live; only runs while connected (started/stopped below).
        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uptimeTimer.Tick += (_, _) => OnPropertyChanged(nameof(ConnectedDuration));

        _status.PropertyChanged += OnStatusChanged;
        if (_status.IsConnected) MarkConnected();
    }

    /// <summary>The shared status VM — bound for the connection state and the Refresh command.</summary>
    public DaemonStatusViewModel Status => _status;

    private void OnStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DaemonStatusViewModel.IsConnected)) return;
        if (_status.IsConnected) MarkConnected(); else MarkDisconnected();
    }

    private void MarkConnected()
    {
        _connectedAt = DateTime.Now;
        _uptimeTimer.Start();
        RaiseTiming();
    }

    private void MarkDisconnected()
    {
        _connectedAt = null;
        _uptimeTimer.Stop();
        RaiseTiming();
    }

    private void RaiseTiming()
    {
        OnPropertyChanged(nameof(ConnectedSince));
        OnPropertyChanged(nameof(ConnectedDuration));
        OnPropertyChanged(nameof(ShowConnectionTiming));
    }

    /// <summary>Wall-clock time the current connection was established (short time), or "" when disconnected.</summary>
    public string ConnectedSince => _connectedAt is { } t ? t.ToString("t") : "";

    /// <summary>Elapsed time since OTA connected ("1h 04m", "3m 12s", "8s"), or "" when disconnected.</summary>
    public string ConnectedDuration => _connectedAt is { } t ? DurationFormat.Compact(DateTime.Now - t) : "";

    /// <summary>Show the "connected since / up for" lines only while a connection is established.</summary>
    public bool ShowConnectionTiming => _status.IsConnected && _connectedAt != null;

    public void Dispose()
    {
        _uptimeTimer.Stop();
        _status.PropertyChanged -= OnStatusChanged;
    }
}
