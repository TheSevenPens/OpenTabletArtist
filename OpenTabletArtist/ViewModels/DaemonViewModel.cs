using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;
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
public sealed partial class DaemonViewModel : ObservableObject, IDisposable
{
    public DaemonViewModel(DaemonStatusViewModel status)
    {
        Status = status;
        Connection = new DaemonConnectionViewModel(status);
        Process = new DaemonProcessViewModel(status);
        Status.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DaemonStatusViewModel.IsDaemonExeMissing))
            {
                OnPropertyChanged(nameof(ShowLocateCard));
                OnPropertyChanged(nameof(ShowInstallCard));
            }
        };
    }

    /// <summary>Shared daemon status + controls (the same instance the Home problem card uses).</summary>
    public DaemonStatusViewModel Status { get; }

    /// <summary>The DAEMON CONNECTION card: whether OTA is connected and for how long.</summary>
    public DaemonConnectionViewModel Connection { get; }

    /// <summary>The DAEMON PROCESS card: running state, which daemon + path, version-match, process uptime.</summary>
    public DaemonProcessViewModel Process { get; }

    // --- "It's already on my system": pointing OTA at an OpenTabletDriver it didn't find ------------
    // The chosen path is tier 0 of the search ladder, so it takes effect on the next connect with no
    // other state to keep in step. See docs/design/official-otd-release.md.

    /// <summary>The daemon location the user chose, or "" when they haven't chosen one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUserDaemonPath))]
    private string _userDaemonPath = AppSettings.Get(DaemonExePaths.UserPathSettingKey) ?? "";

    public bool HasUserDaemonPath => !string.IsNullOrEmpty(UserDaemonPath);

    /// <summary>Show the locate card when there's nothing to connect to (the case it solves), whenever a
    /// location has been chosen (so the choice stays visible and reversible), and on any build that
    /// doesn't ship its own daemon — there, "which OpenTabletDriver?" is a standing question rather than
    /// an error state, and a card that only appeared once nothing worked would be undiscoverable to
    /// someone with two installs. (docs/design/official-otd-release.md)</summary>
    public bool ShowLocateCard =>
        Status.IsDaemonExeMissing || HasUserDaemonPath || !Status.HasBundledDaemon;

    /// <summary>Why the last chosen path was refused, or "" — shown next to the picker so a rejection
    /// explains itself instead of appearing to do nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUserDaemonPathProblem))]
    private string _userDaemonPathProblem = "";

    public bool HasUserDaemonPathProblem => !string.IsNullOrEmpty(UserDaemonPathProblem);

    partial void OnUserDaemonPathChanged(string value) => OnPropertyChanged(nameof(ShowLocateCard));

    /// <summary>Vet a path the user picked and, if it resolves to a daemon, remember it and reconnect
    /// through it. Rejections are reported rather than stored.</summary>
    public async Task ChooseDaemonPathAsync(string? rawPath)
    {
        var result = DaemonExePaths.ValidateUserPath(rawPath, File.Exists);
        if (!result.Accepted)
        {
            UserDaemonPathProblem = result.Problem ?? "";
            return;
        }

        UserDaemonPathProblem = "";
        UserDaemonPath = result.Path!;
        AppSettings.Set(DaemonExePaths.UserPathSettingKey, result.Path!);
        await Status.RefreshCommand.ExecuteAsync(null);
    }

    // --- "Install it for me": fetch the pinned official release -----------------------------------

    private readonly OtdInstaller _installer = new();

    /// <summary>Offer the install only where there is something to install and nothing to install it over:
    /// macOS, with no OpenTabletDriver found. Anywhere else the answer is Locate, not Install.</summary>
    public bool ShowInstallCard => OperatingSystem.IsMacOS() && Status.IsDaemonExeMissing;

    /// <summary>Progress text while installing ("Downloading…", "Extracting…"), or "".</summary>
    [ObservableProperty] private string _installStatus = "";

    [ObservableProperty] private int _installProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstallProblem))]
    private string _installProblem = "";

    public bool HasInstallProblem => !string.IsNullOrEmpty(InstallProblem);

    /// <summary>Shown after a successful install: macOS may need the user to approve the unsigned app
    /// once, by hand. OTA does not strip the quarantine attribute on their behalf.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstallGuidance))]
    private string _installGuidance = "";

    public bool HasInstallGuidance => !string.IsNullOrEmpty(InstallGuidance);

    [ObservableProperty] private bool _isInstalling;

    /// <summary>The version this would install, for the card's copy — pinned, so it matches what OTA was
    /// built against and the install raises no version-mismatch warning.</summary>
    public string InstallVersion => OtdRelease.AssetVersion;

    /// <summary>The card's copy. Names the version and says plainly where the download comes from —
    /// this installs an unsigned third-party binary, so the user should know that before pressing it,
    /// not after.</summary>
    public string InstallDescription =>
        $"OpenTabletArtist needs OpenTabletDriver to talk to your tablet. It can download the official "
        + $"release (v{OtdRelease.AssetVersion}) from OpenTabletDriver's GitHub and install it to "
        + $"{OtdRelease.InstallDirectory}.";

    [RelayCommand]
    private async Task InstallOtd()
    {
        if (IsInstalling) return;
        IsInstalling = true;
        InstallProblem = "";
        InstallGuidance = "";
        InstallProgress = 0;
        try
        {
            _installer.StatusChanged += OnInstallStatus;
            _installer.ProgressChanged += OnInstallProgress;

            var result = await _installer.InstallAsync();

            if (!result.Installed)
            {
                InstallProblem = result.Problem ?? "The install didn't complete.";
                return;
            }

            // Installed into /Applications, the ladder's first entry — so connecting is all that's left,
            // and nothing has to be remembered.
            InstallGuidance = OtdInstaller.GatekeeperGuidance;
            await Status.RefreshCommand.ExecuteAsync(null);
        }
        finally
        {
            _installer.StatusChanged -= OnInstallStatus;
            _installer.ProgressChanged -= OnInstallProgress;
            InstallStatus = "";
            IsInstalling = false;
        }
    }

    private void OnInstallStatus(string status) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => InstallStatus = status);

    private void OnInstallProgress(int percent) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => InstallProgress = percent);

    /// <summary>Forget the chosen location and fall back to the rest of the ladder (bundled copy, an
    /// installed OpenTabletDriver, the dev tree).</summary>
    [RelayCommand]
    private async Task ClearDaemonPath()
    {
        AppSettings.Remove(DaemonExePaths.UserPathSettingKey);
        UserDaemonPath = "";
        UserDaemonPathProblem = "";
        await Status.RefreshCommand.ExecuteAsync(null);
    }

    /// <summary>Reveal a daemon executable's folder in the OS file manager.
    ///
    /// Takes the FILE path the page shows and opens its DIRECTORY. Handing explorer.exe an .exe would run
    /// it — which here would start a second daemon — so the directory is what gets passed, never the file.
    /// Guarded like the other OpenFolder commands: nothing happens when the folder is gone, which is the
    /// normal case for a daemon path recorded on a machine the build no longer exists on.</summary>
    [RelayCommand]
    private void OpenContainingFolder(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        var folder = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            PlatformShell.RevealInFileManager(folder);
    }

    /// <summary>OTA's own version, for the "this app" end of the topology (#daemon-topology). Read the
    /// same way About reads it — from the assembly, which the release workflow stamps with the tag — so
    /// the two can never disagree.</summary>
    public string AppVersion { get; } = AppVersionInfo.Format(
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

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

    // Path to the packaged daemon binary (from rpm -ql); shown on the SYSTEM-INSTALLED DAEMON card.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOtdDaemonPath))]
    private string _otdDaemonPath = "";

    public bool HasOtdDaemonPath => !string.IsNullOrEmpty(OtdDaemonPath);

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
        OtdDaemonPath = result.DaemonPath ?? "";
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

    public void Dispose()
    {
        Connection.Dispose();
        Process.Dispose();
    }
}
