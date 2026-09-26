using System.Collections.Concurrent;
using System.IO.Pipes;
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
        string pipe = "health-test-" + Guid.NewGuid();
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
        using var client = new DiagnosticsConnection("no-daemon-" + Guid.NewGuid());
        var report = await HealthCollector.CollectAsync(LiveHealthSource.Create(client), options: new()
        {
            Coverage = [ProbeId.Daemon],
            ProbeTimeout = TimeSpan.FromMilliseconds(80),
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(report.IsComplete);
        Assert.False(report.Snapshot.DaemonConnected);
        Assert.Equal("Timeout", Assert.Single(report.Probes).Failure!.Code);
    }

    public sealed class ReadOnlyServer
    {
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
            return [new LogMessage { Group = "Detect", Message = "'Wacom' driver is detected. It will block detection of tablets." }];
        }
    }
}
