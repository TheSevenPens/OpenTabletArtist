using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletDriver.Plugin.Logging;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Watches the daemon log for conflicting-driver warnings (#245) — shared by the Driver cleanup page
/// and the Home alert so they agree. Seeds from the daemon's buffered log on (re)connect, then tracks
/// live messages; dedupes by name and drops OTD's false positive on our own process.
/// </summary>
public partial class DriverConflictMonitor : ObservableObject, IDisposable
{
    private readonly IDaemonLogSource? _log;
    private readonly IConnectionState? _connection;

    /// <summary>The conflicting drivers currently detected (one per manufacturer).</summary>
    public ObservableCollection<DetectedDriver> Drivers { get; } = new();
    [ObservableProperty] private bool _hasConflicts;

    public DriverConflictMonitor(IDaemonLogSource? log = null, IConnectionState? connection = null)
    {
        _log = log;
        _connection = connection;
        if (_log != null) _log.LogReceived += OnLogReceived;
        if (_connection != null)
        {
            _connection.PropertyChanged += OnConnectionChanged;
            if (_connection.IsConnected) _ = SeedAsync();
        }
    }

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IConnectionState.IsConnected) && _connection!.IsConnected)
            _ = SeedAsync();
    }

    private async Task SeedAsync()
    {
        if (_log == null) return;
        var log = await _log.GetCurrentLogAsync();
        Dispatcher.UIThread.Post(() =>
        {
            Drivers.Clear();
            foreach (var m in log) Add(ConflictingDriverParser.TryParse(m));
            HasConflicts = Drivers.Count > 0;
        });
    }

    private void OnLogReceived(LogMessage message) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (Add(ConflictingDriverParser.TryParse(message)))
                HasConflicts = Drivers.Count > 0;
        });

    private bool Add(DetectedDriver? driver)
    {
        // Skip OTD's false positive on our own app, and dedupe by manufacturer name.
        if (driver == null || driver.IsSelfMatch) return false;
        if (Drivers.Any(d => d.Name == driver.Name)) return false;
        Drivers.Add(driver);
        return true;
    }

    public void Dispose()
    {
        if (_log != null) _log.LogReceived -= OnLogReceived;
        if (_connection != null) _connection.PropertyChanged -= OnConnectionChanged;
    }
}
