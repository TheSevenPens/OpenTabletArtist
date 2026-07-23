using System;
using System.Diagnostics;

namespace OpenTabletArtist.Services;

/// <summary>
/// Detects whether OpenTabletDriver is installed as a system RPM package — the normal Fedora/RHEL install —
/// versus run from source or a tarball. Linux + RPM specific (the daemon page's Linux card). It queries the
/// local RPM database with <c>rpm -q</c> (~10ms, no network); any failure — off Linux, rpm absent, timeout —
/// reports "not installed" rather than throwing. Checked on-demand (daemon-tab entry + a Refresh button), so
/// it never runs on the status-poll path.
/// </summary>
public static class OtdPackageInstall
{
    /// <summary>The package name the OTD RPM spec ships under (OTD_LNAME).</summary>
    public const string PackageName = "opentabletdriver";

    /// <summary>Whether the package is installed, and its <c>version-release</c> string when it is.</summary>
    public sealed record Result(bool Installed, string? Version);

    /// <summary>Query the RPM database for the OTD package. See the type summary for failure behavior.</summary>
    public static Result Query()
    {
        if (!OperatingSystem.IsLinux()) return new Result(false, null);
        try
        {
            var psi = new ProcessStartInfo("rpm")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("-q");
            psi.ArgumentList.Add("--queryformat");
            psi.ArgumentList.Add("%{VERSION}-%{RELEASE}"); // just the version-release, not the full NEVRA
            psi.ArgumentList.Add(PackageName);

            using var proc = Process.Start(psi);
            if (proc == null) return new Result(false, null);
            // WaitForExit before reading (output is tiny, no full-pipe deadlock) so a hung rpm can't block.
            if (!proc.WaitForExit(2000)) { try { proc.Kill(); } catch { /* best effort */ } return new Result(false, null); }
            return Interpret(proc.ExitCode, proc.StandardOutput.ReadToEnd());
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
}
