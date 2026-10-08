using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// A recording through the real stream path: the daemon's report, <see cref="DaemonPenInputSource"/>'s parsing and
/// tap, the admission, and the view model. The UI dispatcher is never drained in these tests, which is the point: the
/// reports are in the recording whether or not the page has got round to them.
/// </summary>
public class StrokeRecordingStreamTests : IDisposable
{
    private static readonly long Ms = Stopwatch.Frequency / 1000;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"ota-stream-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    private sealed class Daemon : IDaemonDebugSession
    {
        public event Action<JObject>? DeviceReport;
        public Task SetTabletDebugAsync(bool enabled) => Task.CompletedTask;
        public void Emit(JObject report) => DeviceReport?.Invoke(report);
    }

    private static readonly StrokeRecordingContext Ptk470 = new(
        TestReports.Tablet, "OpenTabletDriver 0.6.7", 1023, new TabletSpace(15200, 9500, 152.0, 95.0));

    private static PenSample Pen(double x, double pressure) =>
        new(0, 0, x, x * 2, pressure / 1023, 4, -3, 0, pressure > 0, HoverDistance: pressure > 0 ? 0 : 9,
            RawPressure: pressure, HasTilt: true);

    private async Task<(Daemon Daemon, DaemonPenInputSource Source, StrokeRecordingViewModel Vm)> StartedAsync()
    {
        var daemon = new Daemon();
        var source = new DaemonPenInputSource(daemon);
        await source.StartAsync();

        var vm = new StrokeRecordingViewModel(() => Ptk470, source.SetTap, folder: () => _folder);
        return (daemon, source, vm);
    }

    [Fact]
    public async Task EveryReportReceivedBeforeStopIsInTheRecordingWithoutTheUiHavingSeenIt()
    {
        var (daemon, _, vm) = await StartedAsync();
        vm.StartCommand.Execute(null);

        for (var i = 0; i < 20; i++) daemon.Emit(TestReports.Json(Pen(100 + i, 100)));
        vm.StopCommand.Execute(null);   // the dispatcher has queued all twenty and run none

        Assert.Contains("1 stroke", vm.LedgerText);
        Assert.Equal(20, LedgerReader.Of(vm.LedgerText, "reports in total"));
        Assert.Equal(20, LedgerReader.Of(vm.LedgerText, "in strokes"));
        Assert.Equal(0, LedgerReader.Of(vm.LedgerText, "after the stop"));
    }

    [Fact]
    public async Task AReportAfterStopIsNotInTheRecording()
    {
        var (daemon, _, vm) = await StartedAsync();
        vm.StartCommand.Execute(null);
        daemon.Emit(TestReports.Json(Pen(100, 100)));
        vm.StopCommand.Execute(null);

        daemon.Emit(TestReports.Json(Pen(101, 100)));
        vm.KeepAirborne = true;   // re-reads the recording

        Assert.Equal(1, LedgerReader.Of(vm.LedgerText, "reports in total"));
    }

    [Fact]
    public async Task AReportReceivedBeforeRecordWasPressedDoesNotLeakIntoTheRun()
    {
        var (daemon, _, vm) = await StartedAsync();
        daemon.Emit(TestReports.Json(Pen(100, 100)));   // queued for the UI before Record

        vm.StartCommand.Execute(null);
        daemon.Emit(TestReports.Json(Pen(101, 100)));
        vm.StopCommand.Execute(null);

        Assert.Equal(1, LedgerReader.Of(vm.LedgerText, "reports in total"));
    }

    [Fact]
    public async Task AnotherTabletAndAMouseReportAreTurnedAwayAndCountedAtTheStream()
    {
        var (daemon, _, vm) = await StartedAsync();
        vm.StartCommand.Execute(null);

        daemon.Emit(TestReports.Json(Pen(100, 100)));
        daemon.Emit(TestReports.Json(Pen(101, 4096), tablet: "Wacom PTK-670", maxPressure: 8191));
        daemon.Emit(TestReports.Json(Pen(102, 0), pressure: false, tilt: false));   // position, no pressure: a mouse
        daemon.Emit(TestReports.Json(Pen(103, 120)));
        vm.StopCommand.Execute(null);
        vm.SaveCommand.Execute(null);

        var file = JsonNode.Parse(File.ReadAllText(vm.SavedPath))!;
        var notes = file["notes"]!.GetValue<string>();

        Assert.Equal(2, file["readingsHandedToTheRecorder"]!.GetValue<int>());
        Assert.Contains("1 reports from other devices were ignored", notes);
        Assert.Contains("1 reports from this tablet were not complete pen measurements", notes);
        Assert.Equal(["arrived", "x", "y", "pressure", "height", "lean", "azimuth"], file["columns"]!.AsArray().Select(c => c!.GetValue<string>()));
        // Nothing from the other tablet's 8191-level scale (its 4096) is in a file that says the scale is 1023.
        Assert.All(file["strokes"]![0]!["readings"]!.AsArray(), r => Assert.True(r![3]!.GetValue<double>() <= 1023));
    }

    [Fact]
    public async Task ADetachedTapReceivesNothingMore()
    {
        var (daemon, source, _) = await StartedAsync();
        var seen = 0;
        source.SetTap((_, _) => seen++);

        daemon.Emit(TestReports.Json(Pen(100, 100)));
        source.SetTap(null);
        daemon.Emit(TestReports.Json(Pen(101, 100)));

        Assert.Equal(1, seen);
    }

    [Fact]
    public async Task TheTapGetsTheArrivalStampTakenBeforeAnyQueueing()
    {
        var (daemon, source, _) = await StartedAsync();
        PenSample got = default;
        source.SetTap((_, s) => got = s);

        var before = Stopwatch.GetTimestamp();
        daemon.Emit(TestReports.Json(Pen(100, 100)));
        var after = Stopwatch.GetTimestamp();

        Assert.InRange(got.Timestamp, before, after);
        Assert.Equal(100, got.RawPressure);
    }
}
