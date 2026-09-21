using System;
using System.IO;
using OpenTabletArtist.Domain.Health;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    private readonly HealthService? _health;

    public DaemonViewModel(DaemonStatusViewModel status, HealthService? health = null)
    {
        Status = status;
        _health = health;
        if (_health is not null)
        {
            _health.Issues.CollectionChanged += (_, _) => RefreshDaemonIssues();
            RefreshDaemonIssues();
        }
        Connection = new DaemonConnectionViewModel(status);
        Process = new DaemonProcessViewModel(status);
        Status.PropertyChanged += (_, e) =>
        {
            // ShowInstallRuntime also reads IsConnected, so connecting or dropping has to re-ask it —
            // otherwise the offer to install a runtime lingers after the daemon comes up, or fails to
            // appear when it goes away.
            if (e.PropertyName is nameof(DaemonStatusViewModel.IsConnected))
                OnPropertyChanged(nameof(ShowInstallRuntime));

            if (e.PropertyName is nameof(DaemonStatusViewModel.IsDaemonExeMissing))
            {
                OnPropertyChanged(nameof(ShowLocateCard));
                OnPropertyChanged(nameof(ShowInstallCard));
                OnPropertyChanged(nameof(ShowDriverCard));
                OnPropertyChanged(nameof(ShowInstallRuntime));
            }

            // Both read it, and it is what decides whether there is anything to switch away from.
            if (e.PropertyName is nameof(DaemonStatusViewModel.ShowForeignDaemonWarning))
            {
                OnPropertyChanged(nameof(CanSwitchToBundledDaemon));
                OnPropertyChanged(nameof(BundledIsBehindAChosenLocation));

                // The card itself now depends on the offer, so it has to move with it.
                OnPropertyChanged(nameof(ShowDriverCard));
            }
        };
    }

    /// <summary>Shared daemon status + controls (the same instance the Home problem card uses).</summary>
    public DaemonStatusViewModel Status { get; }

    /// <summary>The health issues whose fix lives on this page, repeated here from Home — the same issue
    /// showing in both places is the point of the remediation model (#317), so someone who came here
    /// directly sees what Home would have told them. Rendered without the Review button that Home shows:
    /// on this page it would only navigate back to where the reader already is.</summary>
    public ObservableCollection<HealthIssue> DaemonIssues { get; } = new();

    public bool HasDaemonIssues => DaemonIssues.Count > 0;

    private void RefreshDaemonIssues()
    {
        var next = _health?.IssuesFor(RemediationArea.Daemon).ToList() ?? [];
        if (next.SequenceEqual(DaemonIssues)) return;   // records compare by value — skip a no-op rebuild

        DaemonIssues.Clear();
        foreach (var issue in next) DaemonIssues.Add(issue);
        OnPropertyChanged(nameof(HasDaemonIssues));
    }

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

    /// <summary>
    /// Offer the way back to the bundled daemon only when pressing it would actually get there (#725).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The switch is a restart, and a restart launches <c>ExpectedExePath()</c>, whose ladder is
    /// user-chosen → bundled → an installed OTD → the dev tree. So the bundled copy is what comes next
    /// only when no location has been chosen; with one stored, a restart relaunches <em>that</em>, and a
    /// button saying otherwise would be telling the user something untrue.
    /// </para>
    /// <para>
    /// The button this replaces got that wrong. It asked only whether a foreign daemon was connected and
    /// whether a bundled copy existed, so with a chosen location it offered to switch and then restarted
    /// the very daemon the user was trying to leave. It was removed in 660b49a for being duplication;
    /// this is the same offer, made only when it is true. The blocked case is <see cref="BundledIsBehindAChosenLocation"/>.
    /// </para>
    /// </remarks>
    public bool CanSwitchToBundledDaemon =>
        Offer == DaemonExePaths.BundledOffer.Switch;

    /// <summary>
    /// The bundled copy exists and is not what a restart would reach, because a location is chosen.
    /// </summary>
    /// <remarks>
    /// Said rather than hidden: a user looking for the daemon their app ships should not be left with a
    /// card that silently omits it. Clear is already on this card, and is the one action that puts the
    /// bundled copy back at the front of the ladder.
    /// </remarks>
    public bool BundledIsBehindAChosenLocation =>
        Offer == DaemonExePaths.BundledOffer.ClearTheChosenLocationFirst;

    /// <summary>The decision itself, kept pure and tested in <c>DaemonExePathsTests</c>.</summary>
    private DaemonExePaths.BundledOffer Offer => DaemonExePaths.OfferBundled(
        onForeignDaemon: Status.ShowForeignDaemonWarning,
        hasBundled: Status.HasBundledDaemon,
        hasChosenLocation: HasUserDaemonPath);

    /// <summary>Show the locate card when there's nothing to connect to (the case it solves), whenever a
    /// location has been chosen (so the choice stays visible and reversible), and on any build that
    /// doesn't ship its own daemon — there, "which OpenTabletDriver?" is a standing question rather than
    /// an error state, and a card that only appeared once nothing worked would be undiscoverable to
    /// someone with two installs. (docs/design/official-otd-release.md)</summary>
    public bool ShowLocateCard =>
        Status.IsDaemonExeMissing || HasUserDaemonPath || !Status.HasBundledDaemon;

    /// <summary>The driver block covers the answers to "which OpenTabletDriver?" — install one, point at
    /// one you have, or go back to the bundled copy — so it shows when any of them is on offer.</summary>
    /// <remarks>
    /// The third one had to be added here, and it was found by looking at the page rather than by any
    /// test (#725). <see cref="CanSwitchToBundledDaemon"/> is true exactly when a bundled copy exists, no
    /// location is chosen and the exe is not missing — which is the one combination that makes
    /// <see cref="ShowLocateCard"/> false. So the offer lived inside a card that was hidden whenever the
    /// offer applied, and visible only in the state that tells the user they cannot take it yet.
    /// </remarks>
    public bool ShowDriverCard => ShowLocateCard || ShowInstallCard || CanSwitchToBundledDaemon;

    /// <summary>Why the last chosen path was refused, or "" — shown next to the picker so a rejection
    /// explains itself instead of appearing to do nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUserDaemonPathProblem))]
    private string _userDaemonPathProblem = "";

    public bool HasUserDaemonPathProblem => !string.IsNullOrEmpty(UserDaemonPathProblem);

    partial void OnUserDaemonPathChanged(string value)
    {
        OnPropertyChanged(nameof(ShowLocateCard));
        OnPropertyChanged(nameof(ShowDriverCard));

        // Choosing or clearing a location moves the bundled copy's place in the ladder, which is the
        // whole of what these two say (#725).
        OnPropertyChanged(nameof(CanSwitchToBundledDaemon));
        OnPropertyChanged(nameof(BundledIsBehindAChosenLocation));
    }

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

    private readonly DotnetRuntimeInstaller _runtimeInstaller = new();

    /// <summary>
    /// Offer the .NET runtime when the daemon isn't connected and this machine has no runtime that could
    /// run OpenTabletDriver's official build (#786, D2).
    ///
    /// Asked of the disk rather than waited for from a launch: the point is to say something *before* the
    /// user sits through a daemon that exits instantly. The launch failure remains the authoritative
    /// signal and produces its own message; this is the offer that goes with it.
    ///
    /// Windows only — macOS gets a self-contained OTD and Linux gets a packaged one, so neither has the
    /// prerequisite.
    /// </summary>
    public bool ShowInstallRuntime =>
        OperatingSystem.IsWindows()
        && !Status.IsConnected
        && !DotnetRuntime.Satisfies(DotnetRuntime.Installed(), DotnetRuntime.DaemonMajor);

    /// <summary>What pressing it does, said before it is pressed.</summary>
    public string InstallRuntimeDescription => DotnetRuntimeInstaller.Description;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRuntimeInstallProblem))]
    private string _runtimeInstallProblem = "";

    public bool HasRuntimeInstallProblem => !string.IsNullOrEmpty(RuntimeInstallProblem);

    /// <summary>
    /// What the install did, shown <b>outside</b> the offer. A successful install makes the offer
    /// disappear — that is the point of it — so anything rendered inside the offer vanishes with it,
    /// including "you need to reboot", which is exactly when the user needs to read it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRuntimeInstallOutcome))]
    private string _runtimeInstallOutcome = "";

    public bool HasRuntimeInstallOutcome => !string.IsNullOrEmpty(RuntimeInstallOutcome);

    [ObservableProperty] private bool _isInstallingRuntime;

    [RelayCommand]
    private async Task InstallRuntime()
    {
        if (IsInstallingRuntime) return;
        IsInstallingRuntime = true;
        RuntimeInstallProblem = "";
        RuntimeInstallOutcome = "";
        InstallProgress = 0;
        try
        {
            _runtimeInstaller.StatusChanged += OnInstallStatus;
            _runtimeInstaller.ProgressChanged += OnInstallProgress;

            var result = await _runtimeInstaller.InstallAsync();

            // Declining the elevation prompt is an answer, not a fault (#786 review). Say nothing and
            // leave the offer standing — retrying it automatically would be arguing with the user.
            if (result.Cancelled) return;

            if (!result.Installed)
            {
                RuntimeInstallProblem = result.Problem ?? "The .NET runtime didn't install.";
                return;
            }

            RuntimeInstallOutcome = result.RebootRequired
                ? "The .NET runtime is installed, but Windows needs a restart to finish. The tablet will "
                  + "work after you reboot."
                : "The .NET runtime is installed.";

            // The offer is derived from what's on disk, so re-ask now that the answer has changed.
            OnPropertyChanged(nameof(ShowInstallRuntime));

            // Refresh alone only retries the pipe — it never launches anything. On this path the daemon
            // exited the moment it was started, because there was no runtime, so there is nothing to
            // reconnect to and refreshing would sit at "not connected" having apparently done nothing.
            //
            // Starting it explicitly is right *here* and nowhere else: the user asked for this recovery.
            // Ordinary reconnects must still never auto-launch a daemon, which is what keeps OTA from
            // fighting someone who stopped one deliberately (#787).
            if (!Status.IsConnected && Status.StartDaemonCommand.CanExecute(null))
                await Status.StartDaemonCommand.ExecuteAsync(null);
            else
                await Status.RefreshCommand.ExecuteAsync(null);
        }
        finally
        {
            _runtimeInstaller.StatusChanged -= OnInstallStatus;
            _runtimeInstaller.ProgressChanged -= OnInstallProgress;
            InstallStatus = "";
            IsInstallingRuntime = false;
        }
    }

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
        $"Downloads the official release from OpenTabletDriver's GitHub into {OtdRelease.InstallDirectory}.";

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

        // The derivation stays here -- the page shows a file, and its folder is what to open. Whether
        // that folder is still there, or was derivable at all, is the helper's business (#887).
        PlatformShell.RevealInFileManager(Path.GetDirectoryName(filePath));
    }

    /// <summary>OTA's own version, for the "this app" end of the topology (#daemon-topology). Read the
    /// same way About reads it — from the assembly, which the release workflow stamps with the tag — so
    /// the two can never disagree.</summary>
    public string AppVersion { get; } = AppVersionInfo.Format(
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>The version of the bundled OpenTabletDriver (read from its Desktop assembly).</summary>
    public string CurrentOtdVersion { get; } = OtdRelease.Version.ToString();

    /// <summary>OTA's own build and the OTD release behind it, on one line — they are read together and
    /// never separately. "Bundles" only where a daemon really ships with the app; elsewhere the number is
    /// the release OTA was compiled against, which is a different claim.</summary>
    public string BuildLine => Status.HasBundledDaemon
        ? $"{AppVersion} · bundles OTD {CurrentOtdVersion}"
        : $"{AppVersion} · built against OTD {CurrentOtdVersion}";

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
