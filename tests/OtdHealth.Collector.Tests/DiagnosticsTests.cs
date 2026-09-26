using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Plugin.Logging;
using OtdInterop;
using StreamJsonRpc;

namespace OtdHealth.Collector.Tests;

public class DiagnosticsTests
{
    [Fact]
    public async Task LiveAdapterReadsExistingStateAndNeverCallsAMutation()
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        // The test server exposes only the narrow read protocol. Any mutation fails this test.
        await ReadOnlyRoundTrip(limit.Token);
    }

    private static async Task ReadOnlyRoundTrip(CancellationToken ct)
    {
        string pipe = TestPipeName.Create();
        using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var target = new ReadOnlyServer();
        var accept = server.WaitForConnectionAsync(ct);
        using var client = new DiagnosticsConnection(pipe);
        using var rpc = new JsonRpc(server);
        rpc.AddLocalRpcTarget(target);
        var reportTask = HealthCollector.CollectAsync(LiveHealthSource.Create(client), options: new()
        {
            Coverage = [ProbeId.Daemon, ProbeId.Profiles, ProbeId.DriverConflicts],
        }, cancellationToken: ct);
        await accept; rpc.StartListening();
        var report = await reportTask;
        Assert.True(report.IsComplete, string.Join(", ", report.Probes.Select(p => p.Failure?.Message)));
        Assert.True(Assert.Single(report.Snapshot.Tablets).Detected);
        Assert.True(report.Snapshot.HasDriverConflict);
        Assert.Equal(new[] { "GetSettings", "GetTablets", "GetCurrentLog" }, target.Calls);
    }

    [Fact]
    public async Task MissingPipeDoesNotStartADaemonAndIsBounded()
    {
        using var client = new DiagnosticsConnection(TestPipeName.Create());
        var report = await HealthCollector.CollectAsync(LiveHealthSource.Create(client), options: new()
        {
            Coverage = [ProbeId.Daemon],
            ProbeTimeout = TimeSpan.FromMilliseconds(80),
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(report.IsComplete);
        Assert.False(report.Snapshot.DaemonConnected);
        Assert.Equal("Timeout", Assert.Single(report.Probes).Failure!.Code);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task ExplicitLiveCoverageCanCompleteOnEveryPlatform(bool conflict, int exit)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(10));
        string pipe = TestPipeName.Create();
        using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var target = new ReadOnlyServer { ReportConflict = conflict };
        var accept = server.WaitForConnectionAsync(limit.Token);
        using var rpc = new JsonRpc(server);
        rpc.AddLocalRpcTarget(target);
        var process = AnalyzerTests.Execute("--pipe", pipe, "--coverage", "Daemon,Profiles,DriverConflicts", "--json");
        await accept;
        rpc.StartListening();
        var result = await process;
        Assert.Equal(exit, result.Exit);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("IsComplete").GetBoolean());
        Assert.Equal(HostProbes.Platform.ToString(), json.RootElement.GetProperty("Snapshot").GetProperty("Platform").GetString());
        Assert.Equal(new[] { "Daemon", "Profiles", "DriverConflicts" },
            json.RootElement.GetProperty("RequestedCoverage").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(conflict ? 1 : 0, json.RootElement.GetProperty("Findings").GetArrayLength());
        Assert.Equal(new[] { "GetSettings", "GetTablets", "GetCurrentLog" }, target.Calls);
    }

    public sealed class ReadOnlyServer
    {
        public bool ReportConflict { get; init; } = true;
        public ConcurrentQueue<string> Calls { get; } = new();
        public Settings GetSettings()
        {
            Calls.Enqueue(nameof(GetSettings));
            var settings = new Settings(); settings.Profiles.Add(CollectorTests.Profile()); return settings;
        }
        public JArray GetTablets()
        {
            Calls.Enqueue(nameof(GetTablets));
            return JArray.Parse("""[{"Properties":{"Name":"Tablet"}}]""");
        }
        public List<LogMessage> GetCurrentLog()
        {
            Calls.Enqueue(nameof(GetCurrentLog));
            return ReportConflict
                ? [new LogMessage { Group = "Detect", Message = "'Wacom' driver is detected. It will block detection of tablets." }]
                : [];
        }
    }
}
