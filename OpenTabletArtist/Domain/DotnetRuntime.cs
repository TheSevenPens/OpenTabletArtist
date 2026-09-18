using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTabletArtist.Domain;

/// <summary>
/// Whether this machine has a .NET runtime that can run a framework-dependent daemon (#786, D2).
///
/// OTA itself is self-contained and needs none. OpenTabletDriver's <b>official</b> Windows release is
/// framework-dependent, so adopting it — or shipping it, as Phase D intends — makes a .NET runtime a
/// prerequisite where there wasn't one. Someone with no .NET gets a daemon that exits before it prints
/// anything, and without this all they see is "not connected".
///
/// Pure helpers are separated from the filesystem probe so the rules are testable on any platform.
/// </summary>
public static class DotnetRuntime
{
    /// <summary>The major version OpenTabletDriver's daemon targets (<c>net8.0</c>).</summary>
    public const int DaemonMajor = 8;

    /// <summary>The shared framework a console app needs. (Desktop apps also need WindowsDesktop; the
    /// daemon does not, which is the difference between a 27 MB download and a 55 MB one.)</summary>
    public const string SharedFramework = "Microsoft.NETCore.App";

    /// <summary>
    /// Host exit codes meaning "I could not find a runtime to run this". Verified on Windows by pointing
    /// a framework-dependent daemon at an empty <c>DOTNET_ROOT</c>: it prints "You must install .NET to
    /// run this application" and exits <c>0x80008083</c>.
    ///
    /// <list type="bullet">
    /// <item><c>0x80008083</c> — the host library itself is missing: no .NET at all.</item>
    /// <item><c>0x80008096</c> — the host ran but no matching framework: some .NET, wrong version.</item>
    /// </list>
    ///
    /// Unix masks exit status to the low byte, so the same failures surface as 131 and 150 there.
    /// </summary>
    private static readonly int[] MissingRuntimeExitCodes =
    [
        unchecked((int)0x80008083), unchecked((int)0x80008096),
        0x83, 0x96,   // 131, 150 — the same codes after Unix truncates them
    ];

    /// <summary>
    /// True when <paramref name="exitCode"/> is the .NET host reporting that it had no runtime to use.
    ///
    /// This is the <b>authoritative</b> signal. Looking for installed runtimes on disk is a useful hint
    /// for saying something before the user waits — but the host is the thing that actually decides, and
    /// it can disagree with a directory listing (a broken install, an architecture mismatch, a
    /// <c>DOTNET_ROOT</c> pointing somewhere unexpected).
    /// </summary>
    public static bool IsMissingRuntimeExit(int exitCode) => MissingRuntimeExitCodes.Contains(exitCode);

    /// <summary>
    /// Whether any installed runtime can run an app targeting <paramref name="requiredMajor"/>.
    ///
    /// Same major only. .NET's default roll-forward moves <em>forward within a major version</em>, so a
    /// net8.0 app is not satisfied by .NET 9 or 10 — "some .NET is installed" is the wrong question, and
    /// a machine kept current can fail this while looking perfectly healthy.
    /// </summary>
    public static bool Satisfies(IEnumerable<Version> installed, int requiredMajor) =>
        installed.Any(v => v.Major == requiredMajor);

    /// <summary>
    /// Reads shared-framework versions out of directory names, the way the host resolves them — each
    /// installed runtime is a folder named for its version. Unparseable names are skipped rather than
    /// throwing: a stray file in that directory is not a reason to claim the machine has no .NET.
    /// </summary>
    public static IReadOnlyList<Version> ParseVersions(IEnumerable<string> directoryNames)
    {
        var versions = new List<Version>();
        foreach (var name in directoryNames)
        {
            // Preview/RC folders carry a suffix ("8.0.0-rc.1"); the numeric head is what matters.
            var head = new string(name.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).TrimEnd('.');
            if (Version.TryParse(head, out var v)) versions.Add(v);
        }
        return versions;
    }

    /// <summary>
    /// The runtimes installed where the host would look: <c>DOTNET_ROOT</c> if set, otherwise the
    /// standard per-machine location. Empty when nothing is found — including when the probe simply
    /// cannot see, which is why <see cref="IsMissingRuntimeExit"/> and not this is what decides.
    /// </summary>
    public static IReadOnlyList<Version> Installed() => Installed(DefaultRoots());

    /// <summary>Testable core: reads the shared-framework folder under each candidate root.</summary>
    public static IReadOnlyList<Version> Installed(IEnumerable<string> roots)
    {
        var found = new List<Version>();
        foreach (var root in roots)
        {
            try
            {
                var dir = Path.Combine(root, "shared", SharedFramework);
                if (!Directory.Exists(dir)) continue;
                found.AddRange(ParseVersions(Directory.GetDirectories(dir).Select(Path.GetFileName)!));
            }
            catch
            {
                // Unreadable root; try the next. "Couldn't look" is not "isn't there".
            }
        }
        return found.Distinct().ToList();
    }

    private static IEnumerable<string> DefaultRoots()
    {
        var explicitRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(explicitRoot)) yield return explicitRoot;

        if (OperatingSystem.IsWindows())
        {
            // ProgramFiles resolves per-architecture, which is what we want: an x64 app needs the x64
            // runtime, and the x86 install lives elsewhere and would not satisfy it.
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(programFiles)) yield return Path.Combine(programFiles, "dotnet");
        }
        else
        {
            yield return "/usr/share/dotnet";
            yield return "/usr/local/share/dotnet";
        }
    }
}
