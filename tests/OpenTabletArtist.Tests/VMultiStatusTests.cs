using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OtdHealth;
using OtdHealth.Collector;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// VMulti is detected once, by the health collector's probe, and every reader shows that answer. These
/// pin the three readers (the status mapping, the VMulti page, Windows Ink auto-setup) to one report, and
/// the waiting that lets them act on a report no older than the event they are reacting to.
/// </summary>
public class VMultiStatusTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    // ---- the mapping: report -> what a reader may say -------------------------------------------------

    private static VMultiNode Node(uint problem = 0) => new(@"ROOT\HIDCLASS\0000", [@"pentablet\hid"],
        "Pentablet HID", "vmulti", Present: true, Enabled: problem != 0x16, problem, null, null, null);

    private static VMultiHidObservation Hid(bool visible, bool control, int count = 1, string? error = null) =>
        new(visible, control, count, error);

    private static VMultiDetails Details(string verdict, VMultiHidObservation? hid, params VMultiNode[] nodes) =>
        new(verdict, nodes.Length > 0 ? @"hardware-id:pentablet\hid" : null, nodes, [], 0, 0, null, hid, []);

    private static HealthAnalysisReport Report(bool? installed, VMultiDetails? details = null,
        ProbeOutcome outcome = ProbeOutcome.Completed, ProbeFailure? failure = null) =>
        new(new HealthSnapshot { Platform = HealthPlatform.Windows, VMultiInstalled = installed },
            [ProbeId.VMulti], [new(ProbeId.VMulti, true, true, outcome, failure) { Details = details }]);

    private static HealthAnalysisReport Working() => Report(true,
        Details("Installed & enabled (1 devices)", Hid(true, true), Node()));

    private static HealthAnalysisReport Missing() => Report(false,
        Details("Not installed", Hid(false, false, 0)));

    [Fact]
    public void NoReportYet_IsPending_NotNotInstalled()
    {
        var s = VMultiStatus.From(null);
        Assert.False(s.Known);
        Assert.Equal(VMultiCheck.Pending, s.Check);
        Assert.Equal("Checking...", s.Summary);
    }

    [Fact]
    public void WorkingNodeWithControlChannel_IsInstalledAndFunctional()
    {
        var s = VMultiStatus.From(Working());
        Assert.True(s.Known);
        Assert.True(s.Installed);
        Assert.True(s.Enabled);
        Assert.True(s.Functional);
        Assert.Equal("Installed & active", s.Summary);
        Assert.Equal("Installed & enabled (1 devices)", s.SetupApiText);
        Assert.Equal("Active (1 devices)", s.HidText);
    }

    [Fact]
    public void InstalledButAbsentFromHid_IsNotFunctional()
    {
        var s = VMultiStatus.From(Report(true, Details("Installed & enabled (1 devices)", Hid(false, false, 0), Node())));
        Assert.True(s.Installed);
        Assert.False(s.Functional);
        Assert.Equal("Installed (not active in HID)", s.Summary);
        Assert.Equal("Not visible to HID", s.HidText);
    }

    [Fact]
    public void VisibleToHidWithoutControlChannel_IsNotFunctional()
    {
        // Windows Ink writes through the control channel; a device HID can see but not talk to is no use.
        var s = VMultiStatus.From(Report(true, Details("Installed & enabled (1 devices)", Hid(true, false), Node())));
        Assert.True(s.Installed);
        Assert.False(s.Functional);
        Assert.Equal("Installed (not active in HID)", s.Summary);
        Assert.Equal("Visible but no control channel", s.HidText);
    }

    [Fact]
    public void DisabledNode_IsInstalledButNotEnabled()
    {
        var s = VMultiStatus.From(Report(true, Details("Installed but disabled (1 devices)", Hid(false, false, 0), Node(0x16))));
        Assert.True(s.Installed);
        Assert.False(s.Enabled);
        Assert.Equal("Installed but disabled", s.Summary);
    }

    [Fact]
    public void NotInstalled_IsKnownAndSaysSo()
    {
        var s = VMultiStatus.From(Missing());
        Assert.True(s.Known);
        Assert.False(s.Installed);
        Assert.False(s.Functional);
        Assert.Equal("Not installed", s.Summary);
    }

    [Fact]
    public void HidControlChannelAlone_IsFunctionalButNotInstalled()
    {
        // The deliberate split: "installed" is the node (what Home's finding reads), "functional" is the
        // HID control channel (what Windows Ink needs). A virtual pen in HID with no recognised node is
        // not installed here, exactly as on Home, and the summary says what else was seen.
        var s = VMultiStatus.From(Report(false, Details("Not installed", Hid(true, true))));
        Assert.False(s.Installed);
        Assert.True(s.Functional);
        Assert.Contains("still visible to HID", s.Summary);
    }

    [Fact]
    public void HidReadError_IsNotFunctionalAndIsShown()
    {
        var s = VMultiStatus.From(Report(true, Details("Installed & enabled (1 devices)", Hid(false, false, 0, "boom"), Node())));
        Assert.False(s.Functional);
        Assert.Equal("Error: boom", s.HidText);
    }

    [Fact]
    public void CompletedProbeWithoutDetails_StillAnswersFromTheSnapshot()
    {
        var s = VMultiStatus.From(Report(true, details: null));
        Assert.True(s.Known);
        Assert.True(s.Installed);
        Assert.False(s.Functional);
    }

    [Fact]
    public void FailedProbe_IsUnreadable_NotNotInstalled()
    {
        var s = VMultiStatus.From(Report(null, outcome: ProbeOutcome.Failed,
            failure: new ProbeFailure("Timeout", "The probe exceeded its time limit.")));
        Assert.False(s.Known);
        Assert.Equal(VMultiCheck.Unreadable, s.Check);
        Assert.Contains("time limit", s.Summary);
    }

    [Fact]
    public void CompletedProbeWithNoVerdict_IsUnreadable()
    {
        Assert.Equal(VMultiCheck.Unreadable, VMultiStatus.From(Report(null)).Check);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InstalledIsExactlyWhatHomeReads(bool installed)
    {
        // One answer: the page's "installed" and the health input behind Home's finding come from the
        // same snapshot value, whatever the HID view says.
        var report = Report(installed, Details(installed ? "Installed & enabled (1 devices)" : "Not installed",
            Hid(!installed, !installed), installed ? new[] { Node() } : Array.Empty<VMultiNode>()));
        Assert.Equal(HealthCollectionAdapter.ToInputs(report.Snapshot).VMultiInstalled, VMultiStatus.From(report).Installed);
    }

    // ---- HealthService: waiting for a report that is not older than the event ----------------------------

    private sealed class Collections
    {
        public List<TaskCompletionSource<HealthAnalysisReport>> Started { get; } = [];
        public Task<HealthAnalysisReport> Start(System.Threading.CancellationToken _)
        {
            var tcs = new TaskCompletionSource<HealthAnalysisReport>(TaskCreationOptions.RunContinuationsAsynchronously);
            Started.Add(tcs);
            return tcs.Task;
        }
    }

    private static HealthService NewService(Collections collections, FakeConnectionState? connection = null)
    {
        var monitor = new DriverConflictMonitor();
        return new HealthService(connection ?? new FakeConnectionState { IsConnected = true }, new FakeDeviceData(),
            monitor, collections.Start);
    }

    [Fact]
    public async Task LatestAnalysis_WaitsForTheCollectionInFlight()
    {
        var c = new Collections();
        using var service = NewService(c);

        var latest = service.LatestAnalysisAsync();
        Assert.False(latest.IsCompleted);
        var report = Working();
        c.Started[0].SetResult(report);

        Assert.Same(report, await latest.WaitAsync(Patience));
    }

    [Fact]
    public async Task LatestAnalysis_FollowsACollectionThatSupersedesTheFirst()
    {
        var c = new Collections();
        var connection = new FakeConnectionState { IsConnected = true };
        using var service = NewService(c, connection);

        var latest = service.LatestAnalysisAsync();
        connection.DaemonIsManagedButNotSelected = true;        // starts a second collection, cancels the first
        c.Started[0].SetResult(Missing());                       // the obsolete one finishes: must not count
        await Task.Delay(50);
        Assert.False(latest.IsCompleted);

        var newest = Working();
        c.Started[1].SetResult(newest);
        Assert.Same(newest, await latest.WaitAsync(Patience));
    }

    [Fact]
    public async Task RefreshAsync_ReturnsTheReportItsOwnCollectionProduced()
    {
        var c = new Collections();
        using var service = NewService(c);
        c.Started[0].SetResult(Working());
        await service.CurrentCollection.WaitAsync(Patience);

        var refresh = service.RefreshAsync();
        Assert.False(refresh.IsCompleted);                       // not satisfied by the report already held
        var after = Missing();
        c.Started[1].SetResult(after);

        Assert.Same(after, await refresh.WaitAsync(Patience));
    }

    [Fact]
    public async Task RefreshAsync_ReturnsNullWhenTheCollectionFails_NotTheStaleReport()
    {
        var calls = 0;
        var monitor = new DriverConflictMonitor();
        using var service = new HealthService(new FakeConnectionState { IsConnected = true }, new FakeDeviceData(), monitor,
            _ => ++calls == 1 ? Task.FromResult(Working()) : throw new InvalidOperationException("collector fell over"));
        await service.CurrentCollection.WaitAsync(Patience);
        Assert.NotNull(service.Analysis);

        Assert.Null(await service.RefreshAsync().WaitAsync(Patience));
    }

    // ---- the page ---------------------------------------------------------------------------------------

    private static VMultiViewModel NewPage(HealthService service) => new(new FakeDialogService(), service);

    [Fact]
    public void Page_BeforeAnyReport_ClaimsNothing()
    {
        var c = new Collections();
        using var service = NewService(c);
        using var page = NewPage(service);

        Assert.Equal("Checking...", page.VmultiMessage);
        Assert.False(page.ShowInstallButton);
        Assert.False(page.ShowUninstallButton);
        Assert.False(page.ShowNotInstalledNote);
    }

    [Fact]
    public async Task Page_ShowsTheCollectorsAnswer_NotItsOwn()
    {
        // This machine may well have VMulti installed. The page must say what the report says, so a
        // report of "not installed" must not be overruled by anything the page could enumerate itself.
        var c = new Collections();
        using var service = NewService(c);
        using var page = NewPage(service);

        c.Started[0].SetResult(Missing());
        await service.CurrentCollection.WaitAsync(Patience);

        Assert.Equal("Not installed", page.VmultiMessage);
        Assert.False(page.VmultiInstalled);
        Assert.True(page.ShowInstallButton);
        Assert.False(page.ShowUninstallButton);
        Assert.True(page.ShowNotInstalledNote);
    }

    [Fact]
    public async Task Page_InstalledReport_OffersUninstallOnly()
    {
        var c = new Collections();
        using var service = NewService(c);
        using var page = NewPage(service);

        c.Started[0].SetResult(Working());
        await service.CurrentCollection.WaitAsync(Patience);

        Assert.Equal("Installed & active", page.VmultiMessage);
        Assert.True(page.VmultiInstalled);
        Assert.False(page.ShowInstallButton);
        Assert.True(page.ShowUninstallButton);
        Assert.False(page.ShowNotInstalledNote);
    }

    [Fact]
    public async Task Page_UnreadableProbe_OffersNeitherAction()
    {
        var c = new Collections();
        using var service = NewService(c);
        using var page = NewPage(service);

        c.Started[0].SetResult(Report(null, outcome: ProbeOutcome.Failed, failure: new ProbeFailure("Timeout", "slow")));
        await service.CurrentCollection.WaitAsync(Patience);

        Assert.StartsWith("Couldn't check", page.VmultiMessage);
        Assert.False(page.ShowInstallButton);
        Assert.False(page.ShowUninstallButton);
    }

    [Fact]
    public async Task Page_Refresh_ShowsCheckingThenTheFreshAnswer()
    {
        var c = new Collections();
        using var service = NewService(c);
        using var page = NewPage(service);
        c.Started[0].SetResult(Working());
        await service.CurrentCollection.WaitAsync(Patience);
        Assert.True(page.VmultiInstalled);

        var refresh = page.RefreshVmultiDetectionCommand.ExecuteAsync(null);
        Assert.Equal("Checking...", page.VmultiMessage);         // not the old "installed" while the re-check runs
        Assert.False(page.ShowUninstallButton);

        c.Started[1].SetResult(Missing());                       // e.g. the uninstall finished
        await refresh.WaitAsync(Patience);

        Assert.Equal("Not installed", page.VmultiMessage);
        Assert.True(page.ShowInstallButton);
    }

    [Fact]
    public async Task Page_Refresh_WhenTheCollectionFails_SaysSoRatherThanKeepingTheOldAnswer()
    {
        var calls = 0;
        var monitor = new DriverConflictMonitor();
        using var service = new HealthService(new FakeConnectionState { IsConnected = true }, new FakeDeviceData(), monitor,
            _ => ++calls == 1 ? Task.FromResult(Working()) : throw new InvalidOperationException("collector fell over"));
        using var page = NewPage(service);
        await service.CurrentCollection.WaitAsync(Patience);
        Assert.True(page.VmultiInstalled);

        await page.RefreshVmultiDetectionCommand.ExecuteAsync(null).WaitAsync(Patience);

        Assert.StartsWith("Couldn't check", page.VmultiMessage);
        Assert.False(page.ShowInstallButton);
        Assert.False(page.ShowUninstallButton);
    }

    [Fact]
    public async Task Page_KeepsItsAnswerAcrossADisconnect()
    {
        // A disconnect empties the held report until the next collection lands. VMulti is not a daemon
        // fact, so the page must not drop to "Checking..." for a driver that has not changed.
        var c = new Collections();
        // An external daemon raises a Home card while connected, so disconnecting changes HasIssues: a
        // HealthService notification that arrives while the held report is empty.
        var connection = new FakeConnectionState { IsConnected = true, Ownership = DaemonOwnership.External };
        using var service = NewService(c, connection);
        using var page = NewPage(service);
        c.Started[0].SetResult(Working());
        await service.CurrentCollection.WaitAsync(Patience);
        var notifiedWhileEmpty = false;
        service.PropertyChanged += (_, _) => notifiedWhileEmpty |= service.Analysis == null;

        connection.IsConnected = false;
        Assert.Null(service.Analysis);
        Assert.True(notifiedWhileEmpty, "the scenario must deliver a notification while the report is empty");

        Assert.Equal("Installed & active", page.VmultiMessage);
        Assert.True(page.ShowUninstallButton);
    }

    [Fact]
    public async Task Page_StopsListeningWhenDisposed()
    {
        var c = new Collections();
        using var service = NewService(c);
        var page = NewPage(service);
        page.Dispose();

        c.Started[0].SetResult(Working());
        await service.CurrentCollection.WaitAsync(Patience);

        Assert.Equal("Checking...", page.VmultiMessage);
    }

    // ---- Windows Ink auto-setup's gate ----------------------------------------------------------------

    private sealed class NoLifecycle : IDaemonLifecycleService
    {
        public string? ExpectedExePath() => null;
        public bool IsAppManaged(string? path) => false;
        public bool HasBundledDaemon() => false;
        public string? FindExe() => null;
        public bool IsRunning() => false;
        public string? Launch(string? executablePath = null) => null;
        public bool Stop(int processId) => true;
        public void StopAll() { }
        public string? PathOf(int processId) => null;
        public string? SingleRunningDaemonPath() => null;
        public string ReadVersion(string executablePath) => HealthService.ExpectedOtdVersion;
    }

    private static AppSession NewSession() => new(FakeSession.Over(new FakeDaemonTransport()), new NoLifecycle());

    [Fact]
    public async Task AutoSetupGate_WaitsForTheReportThenRequiresAControlChannel()
    {
        var c = new Collections();
        using var service = NewService(c);
        using var session = NewSession();
        using var setup = new WindowsInkAutoSetup(session, service);

        var gate = setup.VMultiIsFunctionalAsync();
        Assert.False(gate.IsCompleted);                          // would be false-and-skipped if it read too early
        c.Started[0].SetResult(Working());

        Assert.True(await gate.WaitAsync(Patience));
    }

    [Theory]
    [InlineData(false)]   // node present, absent from HID
    [InlineData(true)]    // not installed at all
    public async Task AutoSetupGate_StaysShutWithoutAControlChannel(bool notInstalled)
    {
        var c = new Collections();
        using var service = NewService(c);
        using var session = NewSession();
        using var setup = new WindowsInkAutoSetup(session, service);

        c.Started[0].SetResult(notInstalled ? Missing()
            : Report(true, Details("Installed & enabled (1 devices)", Hid(false, false, 0), Node())));
        await service.CurrentCollection.WaitAsync(Patience);

        Assert.False(await setup.VMultiIsFunctionalAsync().WaitAsync(Patience));
    }
}
