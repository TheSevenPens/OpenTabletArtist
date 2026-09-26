using System;
using System.Linq;
using OpenTabletDriver.Plugin.Logging;

namespace OpenTabletArtist.Domain;

/// <summary>A manufacturer tablet driver the OTD daemon flagged as present — a conflict the user
/// should clean up. Parsed from the daemon's "Detect" warnings (#245).</summary>
public sealed record DetectedDriver(
    string Name, bool Blocking, bool Flaky, bool Uncertain,
    string Processes, string WikiUrl, string Detail)
{
    /// <summary>One-line plain-English impact, worst-first.</summary>
    public string Impact =>
        Blocking ? "Blocks OpenTabletDriver from detecting tablets"
        : Flaky ? "Can cause flaky tablet support"
        : Uncertain ? "Possible conflict — may be a false positive"
        : "Detected on this system";

    /// <summary>The offending processes/files the daemon found, or empty.</summary>
    public bool HasProcesses => !string.IsNullOrWhiteSpace(Processes);
    public string ProcessesText => $"Processes: {Processes}";
    public bool HasWiki => !string.IsNullOrWhiteSpace(WikiUrl);

    /// <summary>True when the only matched process is OpenTabletArtist itself — a false positive from
    /// OTD's "Pentablet" heuristic matching "O<b>penTablet</b>Artist" (its self-exclusion only covers
    /// "OpenTabletDriver"). We never surface these as a conflict. (#245)</summary>
    public bool IsSelfMatch =>
        HasProcesses &&
        Processes.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                 .All(p => p.Contains("OpenTabletArtist", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Recognizes the daemon's conflicting-driver warnings. The daemon logs one Warning per detected
/// manufacturer driver under the "Detect" group, e.g.
/// <c>'Wacom Tablet' driver is detected. It will block detection of tablets. Processes found: [...]</c>.
/// We parse those into <see cref="DetectedDriver"/>s rather than depend on a (non-existent) RPC.
/// </summary>
public static class ConflictingDriverParser
{
    public static DetectedDriver? TryParse(LogMessage? message)
    {
        var d = OtdHealth.Collector.ConflictingDriverParser.TryParse(message);
        return d == null ? null : new(d.Name, d.Blocking, d.Flaky, d.Uncertain, d.Processes, d.WikiUrl, d.Detail);
    }
}
