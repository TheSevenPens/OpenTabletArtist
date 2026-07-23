using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for the Daemon page (Advanced → OpenTabletDriver → Daemon): the full daemon status +
/// controls (via the shared <see cref="DaemonStatusViewModel"/>), the embedded OpenTabletDriver version,
/// and a launcher for OTD's own UX. The status card moved here off the Home dashboard, which now shows
/// the daemon only when there's a problem.
///
/// On Linux it also surfaces whether OTD is installed as a system RPM package (the normal Fedora/RHEL
/// install) versus run from source — checked on-demand so it never touches the status-poll path.
/// </summary>
public sealed partial class DaemonViewModel : ObservableObject
{
    public DaemonViewModel(DaemonStatusViewModel status) => Status = status;

    /// <summary>Shared daemon status + controls (the same instance the Home problem card uses).</summary>
    public DaemonStatusViewModel Status { get; }

    /// <summary>The version of the bundled OpenTabletDriver (read from its Desktop assembly).</summary>
    public string CurrentOtdVersion { get; } = typeof(Settings).Assembly.GetName().Version?.ToString() ?? "Unknown";

    /// <summary>The RPM-package check only applies on Linux; the card is hidden on every other OS.</summary>
    public bool IsLinux { get; } = OperatingSystem.IsLinux();

    // OTD system-package state (Linux/RPM). Re-checked on-demand when the Daemon tab is shown and via the
    // card's Refresh button, so it reflects a package install/removal without an app restart.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOtdPackageMissing))]
    private bool _otdPackageChecked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOtdPackageMissing))]
    [NotifyPropertyChangedFor(nameof(ShowStartOtdService))]
    private bool _otdPackageInstalled;

    [ObservableProperty] private string _otdPackageVersion = "";

    // Whether the packaged systemd user service is running. Checked alongside the package query so the
    // card can offer "Start" only when installed-but-not-running, and show a "running" note otherwise.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStartOtdService))]
    private bool _otdServiceActive;

    // Result of the last Start action (empty until the button is used).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOtdServiceStatus))]
    private string _otdServiceStatus = "";

    /// <summary>Show the "not a system package" line only once the check has actually run (so the card
    /// doesn't flash "not installed" before the first query completes).</summary>
    public bool ShowOtdPackageMissing => OtdPackageChecked && !OtdPackageInstalled;

    /// <summary>Offer the Start button when the package is installed but its service isn't running yet.</summary>
    public bool ShowStartOtdService => OtdPackageInstalled && !OtdServiceActive;

    public bool HasOtdServiceStatus => !string.IsNullOrEmpty(OtdServiceStatus);

    /// <summary>Run the RPM package query off the UI thread (it spawns rpm, ~10ms) and publish the result.
    /// No-op off Linux. Bound to the card's Refresh button and invoked when the Daemon tab is shown.</summary>
    [RelayCommand]
    private async Task CheckOtdPackageAsync()
    {
        if (!IsLinux) return;
        var result = await Task.Run(OtdPackageInstall.Query);
        OtdPackageInstalled = result.Installed;
        OtdPackageVersion = result.Version ?? "";
        // Only probe the service state when the package is present (the unit only exists then).
        OtdServiceActive = result.Installed && await Task.Run(OtdSystemdService.IsActive);
        OtdPackageChecked = true;
    }

    /// <summary>Start the packaged OTD systemd user service (no root needed), then refresh its state so the
    /// button gives way to the "running" note on success.</summary>
    [RelayCommand]
    private async Task StartOtdServiceAsync()
    {
        OtdServiceStatus = "Starting the OpenTabletDriver service…";
        var (ok, error) = await OtdSystemdService.StartAsync();
        OtdServiceActive = await Task.Run(OtdSystemdService.IsActive);
        OtdServiceStatus = ok
            ? "OpenTabletDriver service started."
            : $"Couldn't start the service: {error}";
    }
}
