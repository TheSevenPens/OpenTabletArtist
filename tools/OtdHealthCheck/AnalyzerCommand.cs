using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using OtdHealth;
using OtdHealth.Collector;
using OtdInterop;

namespace OtdHealthCheck;

public static class AnalyzerCommand
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
    };

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error,
        CancellationToken cancellationToken = default)
    {
        try
        {
            bool json = false;
            string? snapshotPath = null, reportPath = null;
            string pipe = "OpenTabletDriver.Daemon", expected = "0.6.7.0";
            double timeout = 5;
            IReadOnlyList<ProbeId> coverage = Enum.GetValues<ProbeId>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i++)
            {
                string option = args[i];
                if (!seen.Add(option)) throw new ArgumentException($"Repeated option: {option}");
                string Value() => ++i < args.Length ? args[i] : throw new ArgumentException($"Missing value for {option}");
                switch (option)
                {
                    case "--help":
                        await output.WriteLineAsync("OTD health analyzer (.NET 10)\n" +
                            "Usage: OtdHealthCheck [--snapshot FILE | --report FILE] [--json]\n" +
                            "Live options: --timeout SECONDS --expected-version VERSION --pipe NAME\n" +
                            "              --coverage Daemon,Profiles,... (defaults to all probes)\n" +
                            "Read-only: never starts OTD or applies fixes. Exit: 0 complete/no actionable findings;\n" +
                            "1 complete/actionable; 2 incomplete, unknown completeness, or invalid input; 130 cancelled.");
                        return 0;
                    case "--json": json = true; break;
                    case "--snapshot": snapshotPath = Value(); break;
                    case "--report": reportPath = Value(); break;
                    case "--pipe": pipe = Value(); break;
                    case "--expected-version": expected = Value(); break;
                    case "--timeout": timeout = double.Parse(Value(), CultureInfo.InvariantCulture); break;
                    case "--coverage":
                        coverage = Value().Split(',').Select(s => Enum.TryParse<ProbeId>(s, true, out var id) && Enum.IsDefined(id)
                            ? id : throw new ArgumentException($"Unknown probe: {s}")).ToArray();
                        break;
                    default: throw new ArgumentException($"Unknown option: {option}");
                }
            }
            if (snapshotPath != null && reportPath != null) throw new ArgumentException("Choose --snapshot or --report.");
            if ((snapshotPath != null || reportPath != null) && seen.Overlaps(["--pipe", "--expected-version", "--timeout", "--coverage"]))
                throw new ArgumentException("Live options cannot be combined with saved input.");
            if (!double.IsFinite(timeout) || timeout <= 0 || timeout > 300) throw new ArgumentException("Timeout must be between 0 and 300 seconds.");
            if (!Version.TryParse(expected, out _)) throw new ArgumentException("Expected version must be a numeric OTD release.");
            if (coverage.Count == 0 || string.IsNullOrWhiteSpace(pipe)) throw new ArgumentException("Coverage and pipe must be nonempty.");
            cancellationToken.ThrowIfCancellationRequested();
            HealthAnalysisReport? report = null;
            HealthSnapshot snapshot;
            if (snapshotPath != null)
                snapshot = JsonSerializer.Deserialize<HealthSnapshot>(await File.ReadAllTextAsync(snapshotPath, cancellationToken), Json)
                    ?? throw new InvalidDataException("Snapshot is null.");
            else if (reportPath != null)
            {
                report = JsonSerializer.Deserialize<HealthAnalysisReport>(await File.ReadAllTextAsync(reportPath, cancellationToken), Json)
                    ?? throw new InvalidDataException("Report is null.");
                if (report.Snapshot == null || report.Probes == null || report.RequestedCoverage == null
                    || report.Probes.Any(p => p == null || !Enum.IsDefined(p.Id) || !Enum.IsDefined(p.Outcome)))
                    throw new InvalidDataException("Invalid collection report.");
                snapshot = report.Snapshot;
            }
            else
            {
                using var connection = new DiagnosticsConnection(pipe);
                report = await HealthCollector.CollectAsync(LiveHealthSource.Create(connection),
                    new HealthPolicy { ExpectedOtdVersion = expected },
                    new HealthCollectionOptions { ProbeTimeout = TimeSpan.FromSeconds(timeout), Coverage = coverage }, cancellationToken);
                snapshot = report.Snapshot;
            }
            Validate(snapshot);
            var findings = HealthEvaluator.Evaluate(snapshot);
            bool? complete = report?.IsComplete;
            var probes = report?.Probes ?? [];
            bool cancelled = cancellationToken.IsCancellationRequested || probes.Any(p => p.Outcome == ProbeOutcome.Cancelled);
            int exit = cancelled ? 130 : complete != true ? 2 : findings.Any(f => f.Severity >= HealthSeverity.Recommendation) ? 1 : 0;
            if (json)
            {
                if (report != null) await output.WriteLineAsync(JsonSerializer.Serialize(report, Json));
                else await output.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    Snapshot = snapshot,
                    Findings = findings,
                    Probes = probes,
                    RequestedCoverage = Array.Empty<ProbeId>(),
                    IsComplete = (bool?)null,
                }, Json));
            }
            else
            {
                await output.WriteLineAsync(complete switch
                {
                    true => "Complete analysis for requested coverage.",
                    false => "Incomplete analysis; unobserved checks may contain additional problems.",
                    null => "Snapshot evaluation only; collection completeness is unavailable.",
                });
                if (report != null) await output.WriteLineAsync($"Requested coverage: {string.Join(", ", report.RequestedCoverage)}");
                foreach (var finding in findings)
                    await output.WriteLineAsync($"[{finding.Severity}] {finding.Code}" +
                        (finding.TabletName == null ? "" : $" — {finding.TabletName} (id: {finding.TabletId})") +
                        (finding.Evidence == null ? "" : $" {JsonSerializer.Serialize(finding.Evidence)}"));
                if (findings.Count == 0) await output.WriteLineAsync("No findings in the available evidence.");
                foreach (var probe in probes)
                    await output.WriteLineAsync($"{probe.Id}: {probe.Outcome}" +
                        (probe.Failure == null ? "" : $" ({probe.Failure.Code}: {probe.Failure.Message})"));
            }
            return exit;
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("Analysis cancelled."); return 130;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException or JsonException or FormatException or OverflowException)
        {
            await error.WriteLineAsync($"Analysis failed: {ex.Message}"); return 2;
        }
    }

    private static void Validate(HealthSnapshot snapshot)
    {
        if (!Enum.IsDefined(snapshot.Platform) || !Enum.IsDefined(snapshot.LinuxHidAccess)
            || snapshot.Tablets == null || snapshot.LinuxConflictingModulesLoaded == null
            || snapshot.Tablets.Any(t => t == null || !Enum.IsDefined(t.Mapping)))
            throw new InvalidDataException("Invalid snapshot fields.");
    }
}
