using System.Threading.Tasks;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OtdHealth;
using OtdHealth.Collector;
using Xunit;

namespace OpenTabletArtist.Tests;

public class HealthCollectionTests
{
    [Fact]
    public async Task ObsoleteCollectionCannotOverwriteTheLatestReport()
    {
        var first = new TaskCompletionSource<HealthAnalysisReport>();
        var second = new TaskCompletionSource<HealthAnalysisReport>();
        int calls = 0;
        var connection = new FakeConnectionState { IsConnected = true, Ownership = DaemonOwnership.External };
        using var monitor = new DriverConflictMonitor();
        using var service = new HealthService(connection, new FakeDeviceData(), monitor,
            _ => ++calls == 1 ? first.Task : second.Task);
        var oldCollection = service.CurrentCollection;
        connection.DaemonIsManagedButNotSelected = true;
        var currentCollection = service.CurrentCollection;
        var newest = Report("new", pressure: true);
        second.SetResult(newest); await currentCollection;
        Assert.Same(newest, service.Analysis);
        first.SetResult(Report("old", pressure: false)); await oldCollection;
        Assert.Same(newest, service.Analysis);
    }

    [Fact]
    public async Task DisposedServiceDoesNotPublishLateResults()
    {
        var completion = new TaskCompletionSource<HealthAnalysisReport>();
        using var monitor = new DriverConflictMonitor();
        var service = new HealthService(new FakeConnectionState { IsConnected = true }, new FakeDeviceData(), monitor, _ => completion.Task);
        var pending = service.CurrentCollection;
        service.Dispose();
        completion.SetResult(Report("late", true)); await pending;
        Assert.Null(service.Analysis);
    }

    [Fact]
    public void AppAdapterPreservesUnknownInstallationAndDistinctSubjectIds()
    {
        var snapshot = new HealthSnapshot
        {
            Platform = HealthPlatform.Windows,
            Tablets = [new("same", true, true, PressureDisabled: true, Id: "a"), new("same", true, true, TiltDisabled: true, Id: "b")],
        };
        var inputs = HealthCollectionAdapter.ToInputs(snapshot);
        Assert.Null(inputs.WinInkInstalled);
        Assert.Null(inputs.VMultiInstalled);
        Assert.Equal("a", inputs.Tablets[0].Id);
        Assert.Equal("b", inputs.Tablets[1].Id);
    }

    private static HealthAnalysisReport Report(string id, bool pressure) => new(new HealthSnapshot
    {
        DaemonConnected = true,
        Tablets = [new(id, true, true, PressureDisabled: pressure, TiltDisabled: !pressure, Id: id)],
    }, [ProbeId.Daemon, ProbeId.Profiles],
        [new(ProbeId.Daemon, true, true, ProbeOutcome.Completed), new(ProbeId.Profiles, true, true, ProbeOutcome.Completed)]);
}
