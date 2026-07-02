using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Domain.Health;

namespace OpenTabletArtist.Services;

/// <summary>
/// Runs the health-check catalog (#317) and exposes the current issues as a live, shared collection.
/// The Home page shows all of them in a "Needs attention" list; individual pages filter the same
/// collection to surface just the issues whose fix lives on that page. Re-evaluated on every daemon
/// data load and connection-state change, so it self-heals when settings change underneath us (e.g.
/// OTD's own UX editing the same daemon).
/// </summary>
public sealed partial class HealthService : ObservableObject, IDisposable
{
    private readonly IConnectionState _connection;
    private readonly IDeviceData _device;
    private readonly WindowsInkPluginService _winInk;

    /// <summary>Active issues, worst severity first (see <see cref="HealthEvaluator"/>).</summary>
    public ObservableCollection<HealthIssue> Issues { get; } = new();

    [ObservableProperty] private bool _hasIssues;

    public HealthService(IConnectionState connection, IDeviceData device, WindowsInkPluginService? winInk = null)
    {
        _connection = connection;
        _device = device;
        _winInk = winInk ?? new WindowsInkPluginService();

        _device.DataLoaded += Reevaluate;
        _connection.PropertyChanged += OnConnectionChanged;
        Reevaluate();
    }

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IConnectionState.IsConnected)
            or nameof(IConnectionState.ConnectionStatus)
            or nameof(IConnectionState.IsDaemonExeMissing)
            or nameof(IConnectionState.IsForeignDaemon))
            Reevaluate();
    }

    /// <summary>Force an immediate re-evaluation. Call after an action that changes health-relevant
    /// state without a daemon reload (e.g. installing/uninstalling the Windows Ink plugin), so the
    /// catalog updates at once instead of on the next background poll.</summary>
    public void Refresh() => Reevaluate();

    /// <summary>Issues whose fix lives in <paramref name="area"/> (optionally for one tablet) — used by a
    /// page to surface just the issues relevant to it, from the same shared catalog.</summary>
    public IEnumerable<HealthIssue> IssuesFor(RemediationArea area, string? tabletName = null) =>
        Issues.Where(i => i.Remediation is { } r
            && r.Area == area
            && (tabletName == null || string.Equals(r.TabletName, tabletName, StringComparison.OrdinalIgnoreCase)));

    private void Reevaluate()
    {
        var next = HealthEvaluator.Evaluate(GatherInputs());
        if (next.SequenceEqual(Issues)) return; // records compare by value — skip a no-op rebuild
        Issues.Clear();
        foreach (var issue in next) Issues.Add(issue);
        HasIssues = Issues.Count > 0;
    }

    private HealthInputs GatherInputs()
    {
        bool installed = false, mismatch = false;
        var meta = _winInk.ReadInstalled(_device.PluginDirectory);
        if (meta != null)
        {
            installed = true;
            mismatch = !WindowsInkPluginService.IsCompatible(meta);
        }

        var tablets = _device.Profiles
            .Select(p => new TabletHealthInput(
                p.Tablet,
                Detected: p.IsDetected,
                OutputModeIsWinInk: (p.Profile.OutputMode?.Path ?? "")
                    .Contains("WinInk", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return new HealthInputs
        {
            DaemonConnected = _connection.IsConnected,
            DaemonConnecting = _connection.ConnectionStatus == "Connecting...",
            DaemonExeMissing = _connection.IsDaemonExeMissing,
            ForeignDaemon = _connection.IsForeignDaemon,
            WinInkInstalled = installed,
            WinInkVersionMismatch = mismatch,
            Tablets = tablets,
        };
    }

    public void Dispose()
    {
        _device.DataLoaded -= Reevaluate;
        _connection.PropertyChanged -= OnConnectionChanged;
    }
}
