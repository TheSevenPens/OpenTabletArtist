using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Checks that a published build can actually find the things it ships with (#741).
///
/// This deliberately goes through the app's own resolution logic — <see cref="DaemonExePaths"/>,
/// <see cref="WindowsInkBundledInstaller"/>, <see cref="PressurePluginInstaller"/> — rather than
/// re-listing expected paths. A release can contain every file and still be broken if the app looks
/// somewhere else for them, and a check that re-implements the lookup would agree with itself while the
/// product failed. The release workflow runs this against the packaged output, under an isolated
/// user-data root, so a bundle that would greet users with a missing daemon fails the release instead.
/// </summary>
public static class BundleVerification
{
    /// <summary>One thing that was looked for, where, and whether it was there.</summary>
    public readonly record struct Check(string Name, string Path, bool Ok, string? Detail = null);

    /// <summary>
    /// Runs every check against a published layout rooted at <paramref name="baseDir"/>.
    /// Pure with respect to the app: it reads the filesystem but changes nothing and starts no daemon.
    /// </summary>
    public static IReadOnlyList<Check> Run(string baseDir)
    {
        var checks = new List<Check>();

        // The daemon, found the way the app finds it at startup.
        var daemon = DaemonExePaths.BundledPath(baseDir);
        checks.Add(new Check("Bundled daemon", daemon, File.Exists(daemon),
            File.Exists(daemon) ? null : "the app would fall back to an installed OTD, or none at all"));

        // The pen-dynamics plugin: the app's own, and the reason pressure curves work at all.
        var dynamics = Path.Combine(baseDir, "BundledPlugins", "OpenTabletArtistDynamics",
            "OpenTabletArtist.Dynamics.dll");
        checks.Add(new Check("Pen Dynamics plugin", dynamics, File.Exists(dynamics),
            File.Exists(dynamics) ? null : "pressure curves and smoothing would be unavailable"));

        // Windows Ink: present, readable, and compatible with the OTD we ship beside it. Presence alone
        // is not enough — the installer refuses an incompatible bundle, so shipping one means shipping a
        // dead offline path (#739).
        var winInkDir = Path.Combine(baseDir, "BundledPlugins", "WindowsInk");
        var winInkManifest = Path.Combine(winInkDir, "metadata.json");
        if (!File.Exists(winInkManifest))
        {
            checks.Add(new Check("Windows Ink plugin", winInkManifest, false,
                "offline install of Windows Ink would be unavailable"));
        }
        else
        {
            var metadata = WindowsInkBundledInstaller.ReadBundleMetadata(winInkDir);
            if (metadata?.SupportedDriverVersion == null)
            {
                checks.Add(new Check("Windows Ink plugin", winInkManifest, false,
                    "its manifest is unreadable or declares no supported driver version"));
            }
            else
            {
                var otd = WindowsInkPluginService.OtdVersion;
                var ok = metadata.IsSupportedBy(otd);
                checks.Add(new Check("Windows Ink plugin", winInkManifest, ok, ok
                    ? null
                    : $"v{metadata.PluginVersion} does not support the bundled OpenTabletDriver {otd}"));
            }
        }

        // VMulti, and the digest the installer verifies it against before elevating anything.
        var vmulti = Path.Combine(baseDir, "Bundled", "VMulti.Driver.zip");
        checks.Add(new Check("VMulti driver", vmulti, File.Exists(vmulti),
            File.Exists(vmulti) ? null : "Windows Ink would have no virtual device to inject through"));

        var digest = Path.Combine(baseDir, "Bundled", VMultiInstaller.PackageDigestFileName);
        if (File.Exists(vmulti))
        {
            if (!File.Exists(digest))
            {
                checks.Add(new Check("VMulti digest", digest, false,
                    "the archive would be installed without an integrity check"));
            }
            else
            {
                var recorded = File.ReadAllText(digest).Trim();
                var ok = VMultiInstaller.VerifyArchive(vmulti, recorded);
                checks.Add(new Check("VMulti digest", digest, ok, ok
                    ? null
                    : "the recorded digest does not match the bundled archive, so every install would be refused"));
            }
        }

        return checks;
    }

    /// <summary>Human-readable report. Names every problem, since a bundle is usually wrong in more than
    /// one way and fixing them one release at a time would be absurd.</summary>
    public static string Report(IReadOnlyList<Check> checks)
    {
        var text = new StringBuilder();
        text.AppendLine($"OpenTabletArtist bundle verification — {AppContext.BaseDirectory}");
        foreach (var c in checks)
        {
            text.AppendLine($"  [{(c.Ok ? "ok" : "MISSING")}] {c.Name}: {c.Path}");
            if (!c.Ok && c.Detail != null)
                text.AppendLine($"           → {c.Detail}");
        }

        var failed = 0;
        foreach (var c in checks) if (!c.Ok) failed++;
        text.AppendLine(failed == 0
            ? $"All {checks.Count} components present and consistent."
            : $"{failed} of {checks.Count} components missing or inconsistent.");
        return text.ToString();
    }
}
