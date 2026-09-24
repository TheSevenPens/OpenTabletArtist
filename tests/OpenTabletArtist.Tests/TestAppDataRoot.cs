using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Points <b>OTA's own</b> settings and logs at a throwaway directory for the whole test run (#947).
/// </summary>
///
/// <remarks>
/// <para>
/// <see cref="TestUserDataRoot"/> redirects <em>OpenTabletDriver's</em> roots, and does it through
/// <c>LOCALAPPDATA</c>, <c>HOME</c> and the XDG variables, because that is how OTD resolves its
/// defaults. OTA does not: <c>AppPaths.LocalAppData</c> reads its own <c>OTA_APPDATA</c> and otherwise
/// falls back to <c>Environment.GetFolderPath(LocalApplicationData)</c> — the <b>OS known folder</b>,
/// which on Windows ignores the environment variable entirely.
/// </para>
/// <para>
/// So for a while the suites redirected OTD and left OTA pointed at the developer's real
/// <c>%LOCALAPPDATA%\OpenTabletArtist\settings.json</c>, and a test that wrote a preference wrote it
/// there. One did: it cleared <c>daemon.userPath</c> on dispose, which is the rollback information
/// #930 deliberately preserves. A comment claimed the isolation existed; nothing checked it, and the
/// claim was simply wrong. <see cref="TestAppDataRootTests"/> is the check.
/// </para>
/// <para>
/// <b>Two things here are load-bearing and must not be tidied.</b>
/// </para>
/// <para>
/// The variable name is written out as a literal rather than read from <c>AppPaths.OverrideVariable</c>.
/// That field is a static on the type whose <c>LocalAppData</c> is resolved once, in a static
/// initializer — reading the name would run that initializer and cache the real path before this could
/// set anything.
/// </para>
/// <para>
/// And <see cref="Ensure"/> is called explicitly by the log redirect rather than left to module
/// initializer order, which is unspecified within an assembly. <c>AppLog</c> holds its log directory in
/// a static built from <c>AppPaths.LocalAppData</c>, so merely touching <c>AppLog</c> resolves the path:
/// if the log redirect ran first, this would be too late to matter.
/// </para>
/// </remarks>
internal static class TestAppDataRoot
{
    /// <summary>The literal name of the override. See the remarks for why it is not read from the app.</summary>
    private const string OverrideVariable = "OTA_APPDATA";

    /// <summary>Where OTA's settings and logs go during this test run ("" until <see cref="Ensure"/>).</summary>
    internal static string Path { get; private set; } = "";

    /// <summary>
    /// Establishes the redirect. Idempotent, so it does not matter how many callers reach it first.
    /// </summary>
    [ModuleInitializer]
    internal static void Ensure()
    {
        if (Path.Length > 0) return;

        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ota-app-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
        Environment.SetEnvironmentVariable(OverrideVariable, Path);

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { /* best-effort: a leftover temp dir is harmless, a failed teardown is not */ }
        };
    }
}
