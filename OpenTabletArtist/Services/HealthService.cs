using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OtdHealth.Collector;
using OtdInterop;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Domain;
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
    /// <summary>The OpenTabletDriver release OTA was compiled against — the version of the linked OTD
    /// assembly, which is the pinned submodule tag. Under adoption the connected daemon can be a
    /// different release, so this is the number a mismatch is measured against.</summary>
    internal static string ExpectedOtdVersion { get; } =
        OtdRelease.Version.ToString();

    /// <summary>The settings key that held a daemon location the artist chose, before #930 made OTA
    /// start only the copy it ships.</summary>
    /// <remarks>
    /// Nothing writes this any more and nothing removes it: an unknown key survives a settings write, so
    /// leaving it costs nothing and keeps a downgrade working. It is read here so the artist can be told
    /// their choice is no longer being acted on, which is otherwise invisible until their settings look
    /// wrong (see <see cref="Domain.Health.HealthInputs.IgnoredDaemonPath"/>).
    /// </remarks>
    private const string LegacyUserPathKey = "daemon.userPath";

    private static string LegacyDaemonPath() => AppSettings.Get(LegacyUserPathKey) ?? "";

    /// <summary>Set when the artist says they have read the row about the daemon location OTA no longer
    /// starts from. Deliberately a notice preference rather than a second launch setting, and named to
    /// say so: <see cref="LegacyUserPathKey"/> is kept for rollback, this only remembers that the
    /// explanation landed. It can be retired when the notice is; what happens to the old path is a
    /// separate decision (#941).</summary>
    internal const string LegacyPathNoticeAcknowledgedKey = "daemon.legacyPathNoticeAcknowledged";

    private readonly IConnectionState _connection;
    private readonly IDeviceData _device;
    private CancellationTokenSource? _collectionCancellation;
    private bool _disposed;
    private bool _dataLoaded;
    private readonly Func<CancellationToken, Task<HealthAnalysisReport>>? _collectForTest;
    internal Task CurrentCollection { get; private set; } = Task.CompletedTask;
    /// <summary>The last real collection report, before developer overrides or presentation.</summary>
    public HealthAnalysisReport? Analysis { get; private set; }
    private readonly DriverConflictMonitor _conflicts;

    // Avoid duplicate refreshes from the VMulti page. The collector owns the authoritative probe.
    private bool? _vmultiInstalled;

    /// <summary>Active issues, worst severity first (see <see cref="HealthEvaluator"/>).</summary>
    public ObservableCollection<HealthIssue> Issues { get; } = new();

    [ObservableProperty] private bool _hasIssues;

    public HealthService(IConnectionState connection, IDeviceData device, DriverConflictMonitor conflicts)
        : this(connection, device, conflicts, null) { }

    internal HealthService(IConnectionState connection, IDeviceData device, DriverConflictMonitor conflicts,
        Func<CancellationToken, Task<HealthAnalysisReport>>? collectForTest)
    {
        _collectForTest = collectForTest;
        _connection = connection;
        _device = device;
        _conflicts = conflicts;

        _device.DataLoaded += OnDataLoaded;
        _connection.PropertyChanged += OnConnectionChanged;
        _conflicts.PropertyChanged += OnConflictsChanged;
        _conflicts.Drivers.CollectionChanged += OnConflictDriversChanged;
        DeveloperSettings.Instance.PropertyChanged += OnDeveloperSettingsChanged;
        Reevaluate();
    }

    // Inducing/clearing a synthetic warning or forcing a real one from the Developer tab re-runs the
    // catalog immediately (everything except the tab-visibility toggles affects health).
    private void OnDeveloperSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DeveloperSettings.AffectsHealth(e.PropertyName)) Reevaluate();
    }

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IConnectionState.IsConnected)
            or nameof(IConnectionState.ConnectionStatus)
            or nameof(IConnectionState.IsDaemonExeMissing)
            or nameof(IConnectionState.IsForeignDaemon)
            // Which of two sentences the driver card prints (#882), and it is not implied by ownership:
            // an External -> External change moves this and not that, so without it the card would keep
            // the previous wording until some later refresh happened to correct it.
            or nameof(IConnectionState.DaemonIsManagedButNotSelected))
            Reevaluate();
    }

    private void OnConflictsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DriverConflictMonitor.HasConflicts)) Reevaluate();
    }

    private void OnConflictDriversChanged(object? sender, NotifyCollectionChangedEventArgs e) => Reevaluate();

    /// <summary>Feed the latest VMulti detection result (from the VMulti page) and re-evaluate.</summary>
    public void SetVMultiInstalled(bool installed)
    {
        if (_vmultiInstalled == installed) return;
        _vmultiInstalled = installed;
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

    private void OnDataLoaded() { _dataLoaded = true; Reevaluate(); }

    private void Reevaluate()
    {
        if (_disposed) return;
        if (!_connection.IsConnected) { _dataLoaded = false; Analysis = null; }
        // Ownership and app notices are already known and should react immediately. Collected facts
        // arrive asynchronously; keep all collection off the UI thread and discard obsolete results.
        PublishIssues();
        _collectionCancellation?.Cancel();
        _collectionCancellation = new CancellationTokenSource();
        CurrentCollection = CollectAsync(_collectionCancellation);
    }

    private async Task CollectAsync(CancellationTokenSource cancellation)
    {
        using (cancellation)
        using (var diagnostics = new DiagnosticsConnection())
        {
            try
            {
                HealthAnalysisReport report;
                if (_collectForTest != null) report = await _collectForTest(cancellation.Token);
                else
                {
                    var (sources, policy) = HealthCollectionAdapter.Capture(_connection, _device, _dataLoaded, diagnostics);
                    report = await HealthCollector.CollectAsync(sources, policy, cancellationToken: cancellation.Token);
                }
                if (_disposed || cancellation.IsCancellationRequested) return;
                Analysis = report;
                OnPropertyChanged(nameof(Analysis));
                PublishIssues();
            }
            catch (Exception ex) { AppLog.Warn("Health collection failed.", ex); }
            finally
            {
                if (ReferenceEquals(_collectionCancellation, cancellation)) _collectionCancellation = null;
            }
        }
    }

    private void PublishIssues()
    {
        var shown = HealthEvaluator.Evaluate(GatherInputs(applyDeveloper: true));

        IReadOnlyList<HealthIssue> next;
        if (!DeveloperSettings.Instance.HasActiveHealthOverride)
        {
            next = shown; // no developer overrides in play — nothing is synthetic
        }
        else
        {
            // Tag issues that are ONLY present because a Developer toggle forced them (i.e. not in the
            // catalog computed from real state) so Home can offer the hidden right-click dismiss. An issue
            // that's both real and forced stays untagged — dismissing couldn't remove it anyway.
            var realIds = HealthEvaluator.Evaluate(GatherInputs(applyDeveloper: false))
                .Select(i => i.Id).ToHashSet();
            next = shown
                .Select(i => realIds.Contains(i.Id) ? i : i with { IsDeveloperInduced = true })
                .ToList();
        }

        if (next.SequenceEqual(Issues)) return; // records compare by value — skip a no-op rebuild
        Issues.Clear();
        foreach (var issue in next) Issues.Add(issue);
        HasIssues = Issues.Count > 0;
    }

    // applyDeveloper=false yields the catalog from real state only (used to tell which shown issues are
    // synthetic); true layers the Developer-tab induce/force overrides on top.
    private HealthInputs GatherInputs(bool applyDeveloper)
    {
        var collected = HealthCollectionAdapter.ToInputs(Analysis?.Snapshot ?? new OtdHealth.HealthSnapshot
        {
            Platform = HostProbes.Platform,
        });
        var tablets = collected.Tablets.ToList();
        var inputs = collected with
        {
            DaemonConnected = _connection.IsConnected,
            ForeignDaemon = _connection.IsForeignDaemon,
            DaemonIsManagedButNotSelected = _connection.DaemonIsManagedButNotSelected,
            DaemonSourceUnknown = _connection.ShowDaemonSourceUnknown,
            DaemonVersion = _connection.DaemonVersion,
            ExpectedOtdVersion = ExpectedOtdVersion,
            IgnoredDaemonPath = LegacyDaemonPath(),
            LegacyPathNoticeAcknowledged = AppSettings.Get(LegacyPathNoticeAcknowledgedKey) == "true",
            ConnectedDaemonPath = _connection.DaemonSourcePath,
            TrayHostUnavailable = DesktopTrayEnvironment.TrayHostUnavailable,
            SettingsLoad = AppSettings.LoadOutcome.Status,
            SettingsBackupName = AppSettings.LoadOutcome.BackupName,
        };
        if (!applyDeveloper) return inputs;

        var dev = DeveloperSettings.Instance;
        var induced = new List<HealthSeverity>();
        if (dev.InduceBroken) induced.Add(HealthSeverity.Broken);
        if (dev.InduceMisconfigured) induced.Add(HealthSeverity.Misconfigured);
        if (dev.InduceRecommendation) induced.Add(HealthSeverity.Recommendation);

        // Force actual per-tablet warnings by adding a synthetic detected tablet so the real issues emit
        // with their true copy (their Fix deep-links to "Sample Tablet", a harmless no-op when absent).
        if (dev.ForceTabletNotWinInk)
            tablets.Add(new TabletHealthInput("Sample Tablet", Detected: true, OutputModeIsWinInk: false));
        if (dev.ForceTabletMappingOffScreen)
            tablets.Add(new TabletHealthInput("Sample Tablet", Detected: true, OutputModeIsWinInk: true,
                DisplayMappingValidity.OffScreen));
        if (dev.ForceTabletMappingCustom)
            tablets.Add(new TabletHealthInput("Sample Tablet", Detected: true, OutputModeIsWinInk: true,
                DisplayMappingValidity.Custom));
        if (dev.ForceTabletConfigOverride)
            tablets.Add(new TabletHealthInput("Sample Tablet", Detected: true, OutputModeIsWinInk: true,
                ConfigIsOverride: true));
        // All four artist-pen-behavior offenders on one sample tablet, so the multi-link card shows in full.
        if (dev.ForceArtistPenBehavior)
            tablets.Add(new TabletHealthInput("Sample Tablet", Detected: true, OutputModeIsWinInk: false,
                WinInkOptedOut: true, PenTipDisabled: true, PressureDisabled: true, TiltDisabled: true));

        // OR-in the scalar force flags so each real catalog entry appears with its exact UX.
        return inputs with
        {
            DaemonConnected = inputs.DaemonConnected || dev.ForceForeignDaemon,
            ForeignDaemon = inputs.ForeignDaemon || dev.ForceForeignDaemon,
            WinInkInstalled = dev.ForceWinInkNotInstalled ? false : inputs.WinInkInstalled,
            WinInkVersionMismatch = inputs.WinInkVersionMismatch || dev.ForceWinInkVersionMismatch,
            VMultiInstalled = dev.ForceVMultiNotInstalled ? false : inputs.VMultiInstalled,
            HasDriverConflict = inputs.HasDriverConflict || dev.ForceDriverConflict,
            RunningElevated = inputs.RunningElevated || dev.ForceRunningElevated,
            TrayHostUnavailable = inputs.TrayHostUnavailable || dev.ForceTrayHostUnavailable,
            Tablets = tablets,
            InducedSeverities = induced,
        };
    }

    public void Dispose()
    {
        _disposed = true;
        _collectionCancellation?.Cancel();
        _device.DataLoaded -= OnDataLoaded;
        _connection.PropertyChanged -= OnConnectionChanged;
        _conflicts.PropertyChanged -= OnConflictsChanged;
        _conflicts.Drivers.CollectionChanged -= OnConflictDriversChanged;
        DeveloperSettings.Instance.PropertyChanged -= OnDeveloperSettingsChanged;
    }
}
