using System;
using System.Diagnostics;

namespace OpenTabletArtist.Services;

/// <summary>
/// Detects whether OpenTabletDriver is installed as a system RPM package — the normal Fedora/RHEL install —
/// versus run from source or a tarball, and where the packaged daemon binary lives. Linux + RPM specific
/// (the daemon page's Linux card). Queries the local RPM database with <c>rpm</c> (~10ms, no network); any
/// failure — off Linux, rpm absent, timeout — reports "not installed" rather than throwing. Checked
/// on-demand (daemon-tab entry + a Refresh button), so it never runs on the status-poll path.
/// </summary>
public static class OtdPackageInstall
{
    /// <summary>The package name the OTD RPM spec ships under (OTD_LNAME).</summary>
    public const string PackageName = "opentabletdriver";

    /// <summary>Whether the package is installed, its <c>version-release</c>, and the packaged daemon binary
    /// path (all null/false when not installed).</summary>
    public sealed record Result(bool Installed, string? Version, string? DaemonPath = null);

    /// <summary>Query the RPM database for the OTD package (version + daemon path). See the type summary for
    /// failure behavior.</summary>
    public static Result Query()
    {
        if (!OperatingSystem.IsLinux()) return new Result(false, null);
        try
        {
            var (exit, output) = RunRpm("-q", "--queryformat", "%{VERSION}-%{RELEASE}", PackageName);
            var result = Interpret(exit, output);
            return result.Installed ? result with { DaemonPath = QueryDaemonPath() } : result;
        }
        catch
        {
            return new Result(false, null); // rpm not present (non-RPM distro) or spawn failure
        }
    }

    /// <summary>Pure classifier for an <c>rpm -q --queryformat</c> result, split out so it's unit-testable
    /// without spawning rpm. Exit 0 with a non-empty version = installed; anything else (exit 1 prints
    /// "package … is not installed") = not installed.</summary>
    public static Result Interpret(int exitCode, string output)
    {
        var version = output.Trim();
        return exitCode == 0 && version.Length > 0
            ? new Result(true, version)
            : new Result(false, null);
    }

    // The daemon executable the package installs — the real assembly (…/OpenTabletDriver.Daemon), falling
    // back to the /usr/bin/otd-daemon launcher. Null if the file list can't be read.
    private static string? QueryDaemonPath()
    {
        var (exit, output) = RunRpm("-ql", PackageName);
        if (exit != 0) return null;
        string? launcher = null;
        foreach (var line in output.Split('\n'))
        {
            var path = line.Trim();
            if (path.EndsWith("/OpenTabletDriver.Daemon", StringComparison.Ordinal)) return path;
            if (path.EndsWith("/otd-daemon", StringComparison.Ordinal)) launcher = path;
        }
        return launcher;
    }

    // Run `rpm <args>` with a timeout guard; returns the exit code and stdout (tiny, so reading after
    // WaitForExit can't deadlock the pipe). Throws on spawn failure — callers wrap in try/catch.
    private static (int exit, string output) RunRpm(params string[] args)
    {
        var psi = new ProcessStartInfo("rpm")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to launch rpm.");
        if (!proc.WaitForExit(2000))
        {
            try { proc.Kill(); } catch { /* best effort */ }
            throw new TimeoutException("rpm did not respond in time.");
        }
        return (proc.ExitCode, proc.StandardOutput.ReadToEnd());
    }
}
