using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletDriver.Plugin.Logging;
using OpenTabletArtist.Domain;
using OtdInterop;
// Aliases, not `using OtdHealth.Collector`: that namespace has its own DetectedDriver and
// ConflictingDriverParser, which would make the Domain ones ambiguous here.
using DriverStorePackage = OtdHealth.Collector.DriverStorePackage;
using DriverStorePackages = OtdHealth.Collector.DriverStorePackages;

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
    private readonly Func<IReadOnlyList<DriverStorePackage>> _packageSource;

    /// <summary>The name OpenTabletDriver gives its Wacom detection (<c>WacomDriverInfoProvider.FriendlyName</c>).</summary>
    internal const string WacomDriverName = "Wacom";

    /// <summary>The conflicting drivers currently detected (one per manufacturer).</summary>
    public ObservableCollection<DetectedDriver> Drivers { get; } = new();
    [ObservableProperty] private bool _hasConflicts;

    /// <param name="packageSource">Every package in the Windows driver store; the real registry by default.</param>
    public DriverConflictMonitor(IDaemonLogSource? log = null, IConnectionState? connection = null,
        Func<IReadOnlyList<DriverStorePackage>>? packageSource = null)
    {
        _log = log;
        _connection = connection;
        _packageSource = packageSource ?? DriverStorePackages.ReadAll;
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
        Drivers.Add(WithPackages(driver));
        return true;
    }

    /// <summary>OTD says only that "Wacom" appeared somewhere in the driver list, never which package. For
    /// that one driver, name the packages. Any failure leaves the warning exactly as the daemon sent it.</summary>
    private DetectedDriver WithPackages(DetectedDriver driver)
    {
        if (driver.Name != WacomDriverName) return driver;
        try
        {
            return driver with { Packages = DriverStorePackages.Matching(_packageSource(), DriverStorePackages.Wacom) };
        }
        catch (Exception ex)
        {
            AppLog.Debug("Couldn't list the driver-store packages behind the Wacom detection.", ex);
            return driver;
        }
    }

    public void Dispose()
    {
        if (_log != null) _log.LogReceived -= OnLogReceived;
        if (_connection != null) _connection.PropertyChanged -= OnConnectionChanged;
    }
}
