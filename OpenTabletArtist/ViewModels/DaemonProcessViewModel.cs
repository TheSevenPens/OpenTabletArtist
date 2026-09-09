using System;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// The DAEMON PROCESS card (Daemon page, left column): whether a daemon process is running, which one it is
/// (ownership + path), its version and whether that matches the OTD build OTA ships, its process uptime, and
/// the Start/Stop controls. Process facts (running/path/uptime) come from <see cref="DaemonProcess"/> — a
/// direct process lookup, so they're accurate even when OTA isn't connected — while ownership/version come
/// from the shared <see cref="DaemonStatusViewModel"/> (known only while connected). Polls once a second, but
/// only while the card is on screen (StartPolling/StopPolling from the view).
/// </summary>
public sealed partial class DaemonProcessViewModel : ObservableObject, IDisposable
{
    private readonly DaemonStatusViewModel _status;
    private readonly DispatcherTimer _pollTimer;
    private DateTime? _startTime;

    public DaemonProcessViewModel(DaemonStatusViewModel status)
    {
        _status = status;
        BuiltAgainstVersion = typeof(Settings).Assembly.GetName().Version?.ToString() ?? "Unknown";

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pollTimer.Tick += (_, _) => Refresh();

        _status.PropertyChanged += OnStatusChanged;
        Refresh();
    }

    /// <summary>The shared status VM — bound for ownership, connected version, and the Start/Stop commands.</summary>
    public DaemonStatusViewModel Status => _status;

    /// <summary>The OTD version OTA was built against (the bundled OpenTabletDriver assembly version).</summary>
    public string BuiltAgainstVersion { get; }

    [ObservableProperty] private bool _isRunning;

    /// <summary>Start polling the process state (when the card comes on screen). Refreshes immediately.</summary>
    public void StartPolling() { Refresh(); _pollTimer.Start(); }

    /// <summary>Stop polling (when the card leaves the screen), so the /proc scan doesn't run in the background.</summary>
    public void StopPolling() => _pollTimer.Stop();

    private void OnStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DaemonStatusViewModel.IsConnected)
            or nameof(DaemonStatusViewModel.DaemonVersion)
            or nameof(DaemonStatusViewModel.HasDaemonVersion))
        {
            Refresh();       // connect/disconnect changes the running state
            RaiseVersion();
            OnPropertyChanged(nameof(ProcessLine));
            OnPropertyChanged(nameof(HasProcessLine));
        }
    }

    private void Refresh()
    {
        var info = DaemonProcess.Query();
        IsRunning = info.Running;
        _startTime = info.StartTime;
        OnPropertyChanged(nameof(ProcessUptime));
        OnPropertyChanged(nameof(ShowProcessUptime));
        OnPropertyChanged(nameof(ProcessLine));
        OnPropertyChanged(nameof(HasProcessLine));
    }

    private void RaiseVersion()
    {
        OnPropertyChanged(nameof(VersionMatches));
        OnPropertyChanged(nameof(ShowVersionMatch));
        OnPropertyChanged(nameof(ShowVersionMismatch));
    }

    /// <summary>How long the daemon process itself has been running ("1h 04m", "3m 12s"), or "" if unknown.</summary>
    public string ProcessUptime => _startTime is { } t ? DurationFormat.Compact(DateTime.Now - t) : "";

    /// <summary>Show the uptime line only when a process is running with a readable start time.</summary>
    public bool ShowProcessUptime => IsRunning && _startTime != null;

    /// <summary>The daemon's version and how long it has been up, as one line. Either half can be
    /// unavailable — a version can't always be read, and a process start time can't either — so the line
    /// carries whichever it has and disappears when it has neither.</summary>
    public string ProcessLine
    {
        get
        {
            var version = _status.HasDaemonVersion ? $"v{_status.DaemonVersion}" : "";
            var uptime = ShowProcessUptime ? $"up {ProcessUptime}" : "";
            return (version, uptime) switch
            {
                ("", "") => "",
                ("", _) => uptime,
                (_, "") => version,
                _ => $"{version} · {uptime}",
            };
        }
    }

    public bool HasProcessLine => !string.IsNullOrEmpty(ProcessLine);

    /// <summary>True when the connected daemon's version is the same release as the bundled build.</summary>
    public bool VersionMatches => DaemonVersion.SameRelease(BuiltAgainstVersion, _status.DaemonVersion);
    public bool ShowVersionMatch => _status.IsConnected && _status.HasDaemonVersion && VersionMatches;
    public bool ShowVersionMismatch => _status.IsConnected && _status.HasDaemonVersion && !VersionMatches;

    public void Dispose()
    {
        _pollTimer.Stop();
        _status.PropertyChanged -= OnStatusChanged;
    }
}
