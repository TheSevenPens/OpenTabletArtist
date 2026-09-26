using System.Diagnostics;
using System.Text.Json;
using OtdHealthCheck;

namespace OtdHealth.Collector.Tests;

public class AnalyzerTests
{
    private static HealthAnalysisReport Report(bool complete, bool actionable)
    {
        var snapshot = new HealthSnapshot
        {
            DaemonConnected = true,
            Tablets = actionable ? [new("Same", true, true, PressureDisabled: true, Id: "usb:a"),
                new("Same", true, true, TiltDisabled: true, Id: "usb:b")] : [],
        };
        return new(snapshot, [ProbeId.Daemon, ProbeId.Profiles],
            [new(ProbeId.Daemon, true, true, ProbeOutcome.Completed),
             new(ProbeId.Profiles, true, true, complete ? ProbeOutcome.Completed : ProbeOutcome.Failed)]);
    }

    [Theory]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 1)]
    [InlineData(false, false, 2)]
    [InlineData(false, true, 2)]
    public async Task ExecutableOutputAndExitFollowReportCompleteness(bool complete, bool actionable, int exit)
    {
        using var files = new TemporaryDirectory();
        string path = Path.Combine(files.Path, "report.json");
        string saved = JsonSerializer.Serialize(Report(complete, actionable)); File.WriteAllText(path, saved);
        var result = await Execute("--report", path, "--json");
        Assert.Equal(exit, result.Exit);
        Assert.Equal("", result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(complete, json.RootElement.GetProperty("IsComplete").GetBoolean());
        if (actionable)
        {
            var findings = json.RootElement.GetProperty("Findings").EnumerateArray().ToArray();
            Assert.Equal(2, findings.Select(f => f.GetProperty("TabletId").GetString()).Distinct().Count());
            Assert.All(findings, f => Assert.Equal(JsonValueKind.String, f.GetProperty("Severity").ValueKind));
        }
        Assert.Equal(saved, File.ReadAllText(path));
    }

    [Fact]
    public async Task SnapshotWithZeroFindingsStillHasUnknownCompleteness()
    {
        using var files = new TemporaryDirectory(); var path = Path.Combine(files.Path, "snapshot.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new HealthSnapshot()));
        var result = await Execute("--snapshot", path, "--json");
        Assert.Equal(2, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("IsComplete").ValueKind);
        var text = await Execute("--snapshot", path);
        Assert.Contains("completeness is unavailable", text.Output);
    }

    [Fact]
    public async Task ReportFindingsAreRecomputedInsteadOfTrustingCachedFindings()
    {
        using var files = new TemporaryDirectory(); var path = Path.Combine(files.Path, "report.json");
        var report = JsonSerializer.Serialize(Report(true, true));
        using var document = JsonDocument.Parse(report);
        var json = System.Text.Json.Nodes.JsonNode.Parse(report)!; json["Findings"] = new System.Text.Json.Nodes.JsonArray();
        File.WriteAllText(path, json.ToJsonString());
        var result = await Execute("--report", path);
        Assert.Equal(1, result.Exit); Assert.Contains("tablet.pressureDisabled", result.Output);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("broken")]
    [InlineData("{\"Snapshot\":null,\"Probes\":[],\"RequestedCoverage\":[]}")]
    public async Task InvalidReportCannotSucceed(string contents)
    {
        using var files = new TemporaryDirectory(); var path = Path.Combine(files.Path, "bad.json");
        File.WriteAllText(path, contents);
        var result = await Execute("--report", path); Assert.Equal(2, result.Exit); Assert.Contains("Analysis failed", result.Error);
    }

    [Fact]
    public async Task CancelledRequestHasHighestExitPrecedence()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var output = new StringWriter(); var error = new StringWriter();
        Assert.Equal(130, await AnalyzerCommand.RunAsync([], output, error, cancelled.Token));
        Assert.Contains("cancelled", error.ToString());
    }

    [Fact]
    public async Task SuppliedReportCancellationOutranksActionableFindings()
    {
        using var files = new TemporaryDirectory(); var path = Path.Combine(files.Path, "report.json");
        var report = Report(false, true) with
        {
            Probes = [new(ProbeId.Daemon, true, true, ProbeOutcome.Completed), new(ProbeId.Profiles, true, true, ProbeOutcome.Cancelled)],
        };
        File.WriteAllText(path, JsonSerializer.Serialize(report));
        Assert.Equal(130, (await Execute("--report", path)).Exit);
    }

    [Fact]
    public async Task MissingDaemonProducesMachineReadableIncompleteOutput()
    {
        var result = await Execute("--pipe", "missing-" + Guid.NewGuid(), "--coverage", "Daemon,Profiles", "--timeout", "0.08", "--json");
        Assert.Equal(2, result.Exit);
        using var json = JsonDocument.Parse(result.Output); Assert.False(json.RootElement.GetProperty("IsComplete").GetBoolean());
    }

    [Fact]
    public void CollectorAndExecutableHaveNoUiDependencies()
    {
        foreach (var assembly in new[] { typeof(HealthCollector).Assembly, typeof(AnalyzerCommand).Assembly })
        {
            var file = Path.ChangeExtension(assembly.Location, ".deps.json");
            // The executable dependency manifest contains the full restored transitive graph.
            if (assembly == typeof(AnalyzerCommand).Assembly) Assert.True(File.Exists(file), "Missing analyzer dependency manifest.");
            if (!File.Exists(file)) continue;
            var deps = File.ReadAllText(file);
            foreach (var forbidden in new[] { "Avalonia", "OpenTabletArtist/", "SkiaSharp", "CommunityToolkit.Mvvm" })
                Assert.DoesNotContain(forbidden, deps);
        }
    }

    private static async Task<(int Exit, string Output, string Error)> Execute(params string[] arguments)
    {
        var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add(typeof(AnalyzerCommand).Assembly.Location);
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await stdout, await stderr);
    }
}
