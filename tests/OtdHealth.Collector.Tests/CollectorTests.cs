using System.Diagnostics;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdHealth.Collector;

namespace OtdHealth.Collector.Tests;

public class CollectorTests
{
    private static HealthCollectionOptions Coverage(params ProbeId[] ids) => new() { Coverage = ids };
    internal static Profile Profile(string name = "Tablet") => new()
    {
        Tablet = name,
        OutputMode = new PluginSettingStore((Type)null!) { Path = "WinInk.AbsoluteMode" },
        BindingSettings = new() { TipButton = new PluginSettingStore((Type)null!) { Path = "tip" } },
    };
    private static Task<IReadOnlyList<ProfileObservation>> Profiles(Profile profile) =>
        Task.FromResult(ProfileInspector.Identify([(profile, true)]));

    [Fact]
    public async Task DisconnectedDaemonNeverCompletesEvenWithNoFindings()
    {
        bool read = false;
        var report = await HealthCollector.CollectAsync(new()
        {
            Platform = HealthPlatform.Windows,
            Connect = _ => Task.FromResult(false),
            Profiles = _ => { read = true; return Profiles(Profile()); },
        }, options: Coverage(ProbeId.Daemon, ProbeId.Profiles, ProbeId.WindowsInk), cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(report.IsComplete);
        Assert.False(read);
        Assert.Null(report.Snapshot.WinInkInstalled);
        Assert.Empty(report.Findings);
        Assert.All(report.Probes, p => Assert.Equal(ProbeOutcome.Unavailable, p.Outcome));
    }

    [Fact]
    public async Task UnsupportedMappingRetainsActionableProfileFindings()
    {
        var profile = Profile(); profile.BindingSettings.DisablePressure = true;
        var before = JsonConvert.SerializeObject(profile);
        var report = await HealthCollector.CollectAsync(new()
        {
            Connect = _ => Task.FromResult(true),
            Profiles = _ => Profiles(profile),
            Displays = _ => throw new ProbeUnavailableException("No desktop session", true),
        }, options: Coverage(ProbeId.Daemon, ProbeId.Profiles, ProbeId.Displays), cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(report.IsComplete);
        Assert.Equal(ProbeOutcome.Unsupported, report.Probes.Single(p => p.Id == ProbeId.Displays).Outcome);
        Assert.Contains(report.Findings, f => f.Code == "tablet.pressureDisabled");
        Assert.Equal(before, JsonConvert.SerializeObject(profile));
    }

    [Fact]
    public async Task NativeProbeThatIgnoresCancellationIsBoundedAndCannotChangeReportLater()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watch = Stopwatch.StartNew();
        var report = await HealthCollector.CollectAsync(new()
        {
            Platform = HealthPlatform.Windows,
            VMulti = _ => release.Task,
        }, options: new() { Coverage = [ProbeId.VMulti], ProbeTimeout = TimeSpan.FromMilliseconds(80) }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3));
        Assert.False(report.IsComplete);
        Assert.Null(report.Snapshot.VMultiInstalled);
        Assert.Equal("Timeout", Assert.Single(report.Probes).Failure!.Code);
        release.SetResult(false);
        await Task.Yield();
        Assert.Null(report.Snapshot.VMultiInstalled);
    }

    [Fact]
    public async Task CancellationRetainsEarlierObservations()
    {
        using var cancellation = new CancellationTokenSource();
        var profile = Profile(); profile.BindingSettings.DisableTilt = true;
        var report = await HealthCollector.CollectAsync(new()
        {
            Connect = _ => Task.FromResult(true),
            Profiles = _ => Profiles(profile),
            Displays = ct => { cancellation.Cancel(); return Task.FromCanceled<IReadOnlyList<DisplayBounds>>(ct); },
        }, options: Coverage(ProbeId.Daemon, ProbeId.Profiles, ProbeId.Displays), cancellationToken: cancellation.Token);
        Assert.False(report.IsComplete);
        Assert.Contains(report.Findings, f => f.Code == "tablet.tiltDisabled");
        Assert.Equal(ProbeOutcome.Cancelled, report.Probes.Single(p => p.Id == ProbeId.Displays).Outcome);
    }

    [Fact]
    public async Task TotalDeadlineIsAFailureRatherThanUserCancellation()
    {
        var report = await HealthCollector.CollectAsync(new() { Connect = ct => Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => false) },
            options: new() { Coverage = [ProbeId.Daemon, ProbeId.Profiles], TotalTimeout = TimeSpan.FromMilliseconds(80) }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(report.IsComplete);
        Assert.All(report.Probes, p => Assert.Equal("DeadlineExceeded", p.Failure!.Code));
    }

    [Fact]
    public async Task NotApplicableDoesNotCallNativeSourcesOrMakeReportIncomplete()
    {
        var report = await HealthCollector.CollectAsync(new()
        {
            Platform = HealthPlatform.MacOS,
            VMulti = _ => throw new Exception("Must not run"),
        }, options: Coverage(ProbeId.VMulti, ProbeId.ProcessElevation), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(report.IsComplete);
        Assert.All(report.Probes, p => Assert.Equal(ProbeOutcome.NotApplicable, p.Outcome));
    }

    [Theory]
    [InlineData(HealthPlatform.Linux)]
    [InlineData(HealthPlatform.MacOS)]
    public async Task UnsupportedVersionKeepsDefaultCoverageIncomplete(HealthPlatform platform)
    {
        using var files = new TemporaryDirectory();
        var report = await HealthCollector.CollectAsync(new()
        {
            Platform = platform,
            Connect = _ => Task.FromResult(true),
            Version = _ => throw new ProbeUnavailableException("Server binary identity is unsupported.", true),
            Profiles = _ => Profiles(Profile()),
            Displays = _ => Task.FromResult<IReadOnlyList<DisplayBounds>>([new(0, 0, 1920, 1080)]),
            ConfigurationDirectory = _ => Task.FromResult(files.Path),
            Conflicts = _ => Task.FromResult(new ConflictObservation(false, false)),
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(report.IsComplete);
        Assert.Empty(report.Findings);
        Assert.Contains(ProbeId.DaemonVersion, report.RequestedCoverage);
        var incomplete = Assert.Single(report.Probes, p => p.Outcome is not (ProbeOutcome.Completed or ProbeOutcome.NotApplicable));
        Assert.Equal(ProbeId.DaemonVersion, incomplete.Id);
        Assert.Equal(ProbeOutcome.Unsupported, incomplete.Outcome);
    }

    [Theory]
    [InlineData(HealthPlatform.Windows)]
    [InlineData(HealthPlatform.Linux)]
    [InlineData(HealthPlatform.MacOS)]
    public void DaemonVersionCannotBePassedOffAsNotApplicable(HealthPlatform platform)
    {
        var report = new HealthAnalysisReport(new() { Platform = platform, DaemonConnected = true },
            [ProbeId.Daemon, ProbeId.DaemonVersion],
            [new(ProbeId.Daemon, true, true, ProbeOutcome.Completed),
             new(ProbeId.DaemonVersion, true, false, ProbeOutcome.NotApplicable)]);
        Assert.False(report.IsComplete);
    }

    [Fact]
    public async Task FailedProfilesCannotBecomeAMacPermissionFinding()
    {
        var report = await HealthCollector.CollectAsync(new()
        {
            Platform = HealthPlatform.MacOS,
            Connect = _ => Task.FromResult(true),
            Profiles = _ => throw new IOException("settings failure"),
            MacOSAccess = _ => Task.FromResult(true),
        }, options: Coverage(ProbeId.Daemon, ProbeId.Profiles, ProbeId.MacOSAccess), cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(report.IsComplete);
        Assert.False(report.Snapshot.DaemonCannotOpenTablet);
        Assert.Equal(ProbeOutcome.Unavailable, report.Probes.Single(p => p.Id == ProbeId.MacOSAccess).Outcome);
    }

    [Fact]
    public async Task LinuxEvidenceAppliesOnlyWhenNoTabletWasDetected()
    {
        var report = await HealthCollector.CollectAsync(new()
        {
            Platform = HealthPlatform.Linux,
            Connect = _ => Task.FromResult(true),
            Profiles = _ => Profiles(Profile()),
            LinuxUdev = _ => throw new Exception("Should not run"),
        }, options: Coverage(ProbeId.Daemon, ProbeId.Profiles, ProbeId.LinuxUdev), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(report.IsComplete);
        Assert.Equal(ProbeOutcome.NotApplicable, report.Probes.Single(p => p.Id == ProbeId.LinuxUdev).Outcome);
    }

    [Fact]
    public async Task DuplicateNamesHaveDistinctStableIdsAndDoNotMutateProfiles()
    {
        var first = Profile("Same"); var second = Profile("Same");
        first.BindingSettings.DisablePressure = true; second.BindingSettings.DisableTilt = true;
        var subjects = ProfileInspector.Identify([(first, true), (Profile("Other"), false), (second, true)]);
        Assert.Equal(subjects.Select(p => p.Id).Where(id => id.Contains("Same")),
            ProfileInspector.Identify([(Profile("Other"), false), (first, true), (second, true)])
                .Select(p => p.Id).Where(id => id.Contains("Same")));
        var report = await HealthCollector.CollectAsync(new()
        {
            Connect = _ => Task.FromResult(true),
            Profiles = _ => Task.FromResult(subjects),
        }, options: Coverage(ProbeId.Daemon, ProbeId.Profiles), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(report.IsComplete);
        Assert.Equal(2, report.Findings.Select(f => f.TabletId).Distinct().Count());
    }

    [Fact]
    public async Task DuplicateIdsFailTheProbeRatherThanCrashingEvaluation()
    {
        IReadOnlyList<ProfileObservation> subjects = [new("id", "one", Profile(), true), new("id", "two", Profile(), true)];
        var report = await HealthCollector.CollectAsync(new()
        {
            Connect = _ => Task.FromResult(true),
            Profiles = _ => Task.FromResult(subjects),
        }, options: Coverage(ProbeId.Daemon, ProbeId.Profiles), cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(report.IsComplete);
        Assert.Equal(ProbeOutcome.Failed, report.Probes.Single(p => p.Id == ProbeId.Profiles).Outcome);
        Assert.Empty(report.Findings);
    }

    [Fact]
    public async Task LinuxPartialReportPreservesObservedPrerequisites()
    {
        var report = await HealthCollector.CollectAsync(new()
        {
            Platform = HealthPlatform.Linux,
            LinuxUdev = _ => Task.FromResult(false),
            LinuxModules = _ => throw new UnauthorizedAccessException("Cannot read modules"),
            LinuxHidAccess = _ => Task.FromResult(new LinuxAccessObservation(HidAccessStatus.PendingRestart, true)),
        }, options: Coverage(ProbeId.LinuxUdev, ProbeId.LinuxModules, ProbeId.LinuxHidAccess), cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(report.IsComplete);
        Assert.True(report.Snapshot.LinuxUdevRulesMissing);
        Assert.Equal(HidAccessStatus.PendingRestart, report.Snapshot.LinuxHidAccess);
        Assert.True(report.Snapshot.LinuxUserManagerRunning);
        Assert.Empty(report.Snapshot.LinuxConflictingModulesLoaded);
        Assert.Equal(ProbeOutcome.Failed, report.Probes.Single(p => p.Id == ProbeId.LinuxModules).Outcome);
        Assert.Equal(2, report.Findings.Count);
    }

    [Fact]
    public async Task MacInferenceRequiresSuccessfulDaemonAndProfileObservations()
    {
        var report = await HealthCollector.CollectAsync(new()
        {
            Platform = HealthPlatform.MacOS,
            Connect = _ => Task.FromResult(true),
            Profiles = _ => Task.FromResult<IReadOnlyList<ProfileObservation>>([]),
            MacOSAccess = _ => Task.FromResult(true),
        }, options: Coverage(ProbeId.Daemon, ProbeId.Profiles, ProbeId.MacOSAccess), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(report.IsComplete);
        Assert.True(report.Snapshot.DaemonCannotOpenTablet);
        Assert.Single(report.Findings);
    }

    [Fact]
    public void ConsumerPolicyControlsDynamicsAndInkOptOut()
    {
        var p = Profile(); p.Filters.Add(new PluginSettingStore((Type)null!) { Path = "consumer.filter", Enable = false });
        var raw = new ProfileObservation("device-a", "Tablet", p, true);
        var baseline = ProfileInspector.Read(raw, [], new HashSet<string>(), new());
        Assert.False(baseline.DynamicsWarningRequired);
        var chosen = ProfileInspector.Read(raw, [], new HashSet<string>(), new()
        {
            RequiredDynamicsFilter = "consumer.filter",
            WinInkOptedOutProfileIds = new HashSet<string> { "device-a" },
        });
        Assert.True(chosen.DynamicsWarningRequired);
        Assert.True(chosen.WinInkOptedOut);
    }

    [Theory]
    [InlineData(ProbeOutcome.Completed, true)]
    [InlineData(ProbeOutcome.Unavailable, false)]
    [InlineData(ProbeOutcome.Unsupported, false)]
    [InlineData(ProbeOutcome.Failed, false)]
    [InlineData(ProbeOutcome.Cancelled, false)]
    [InlineData(ProbeOutcome.NotApplicable, false)]
    public void CompletenessRequiresEveryRequestedApplicableProbe(ProbeOutcome outcome, bool complete)
    {
        var report = new HealthAnalysisReport(new(), [ProbeId.VMulti], [new(ProbeId.VMulti, true, true, outcome)]);
        Assert.Equal(complete, report.IsComplete);
        Assert.False((report with { Probes = [] }).IsComplete);
        Assert.False((report with { Probes = [report.Probes[0], report.Probes[0]] }).IsComplete);
    }

    [Fact]
    public async Task CoverageCannotOmitDependencies() =>
        await Assert.ThrowsAsync<ArgumentException>(() => HealthCollector.CollectAsync(new(), options: Coverage(ProbeId.Profiles), cancellationToken: TestContext.Current.CancellationToken));

    [Fact]
    public void FileProbeDoesNotConfuseMalformedInkWithMissingInk()
    {
        using var files = new TemporaryDirectory();
        Assert.False(FileProbes.WindowsInk(files.Path, "0.6.7.0").Installed);
        var directory = System.IO.Path.Combine(files.Path, "Windows Ink"); Directory.CreateDirectory(directory);
        var file = System.IO.Path.Combine(directory, "metadata.json");
        File.WriteAllText(file, "broken");
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => FileProbes.WindowsInk(files.Path, "0.6.7.0"));
        File.WriteAllText(file, """{"Name":"Windows Ink","SupportedDriverVersion":"0.6.7.0"}""");
        Assert.Equal(new InkObservation(true, false), FileProbes.WindowsInk(files.Path, "0.6.7.0"));
        Assert.Throws<ProbeUnavailableException>(() => FileProbes.WindowsInk("", "0.6.7.0"));
    }

    [Fact]
    public async Task BadOverrideCannotEraseOtherFindingsOrBeReportedAsCompleted()
    {
        using var files = new TemporaryDirectory(); File.WriteAllText(System.IO.Path.Combine(files.Path, "bad.json"), "invalid");
        var p = Profile(); p.BindingSettings.DisablePressure = true;
        var report = await HealthCollector.CollectAsync(new()
        {
            Connect = _ => Task.FromResult(true),
            Profiles = _ => Profiles(p),
            ConfigurationDirectory = _ => Task.FromResult(files.Path),
        }, options: Coverage(ProbeId.Daemon, ProbeId.Profiles, ProbeId.ConfigurationOverrides), cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(report.IsComplete);
        Assert.Contains(report.Findings, f => f.Code == "tablet.pressureDisabled");
        Assert.Equal("invalid", File.ReadAllText(System.IO.Path.Combine(files.Path, "bad.json")));
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "otd-health-tests-" + Guid.NewGuid());
    public TemporaryDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
