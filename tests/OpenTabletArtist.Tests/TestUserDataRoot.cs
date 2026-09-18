using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Redirects OpenTabletDriver's user-data roots into a throwaway directory for the whole test run (#738).
///
/// Merely constructing a <c>PluginSettingStore</c> reaches <c>AppInfo.PluginManager</c>, whose eager
/// static initializer builds a <c>DesktopPluginManager</c> — and that constructor <em>creates</em> the
/// plugin directory. So tests that only meant to check a binding's type name were reaching into (and
/// adding folders to) the developer's installed OTD. In a locked-down environment they failed outright;
/// on a normal machine they quietly mutated a real artist setup. Neither is acceptable from a test run.
///
/// The redirect has to happen before anything touches <c>AppInfo</c>, because assigning
/// <c>AppInfo.Current</c> is itself what triggers the static initializer that constructs the plugin
/// manager — by then the real directory already exists. A module initializer runs before any test does,
/// and OTD resolves its default roots through <c>FileUtilities.InjectEnvironmentVariables</c>, which
/// expands <c>$LOCALAPPDATA</c> and <c>~</c> from this process's own environment. Setting those first
/// makes OTD's own defaults land under the temp root with no upstream change.
/// </summary>
internal static class TestUserDataRoot
{
    /// <summary>The throwaway root every OTD path resolves under during the test run.</summary>
    internal static string Path { get; private set; } = "";

    /// <summary>The runtime directory, kept separate and short — see the socket-length note below.</summary>
    internal static string RuntimePath { get; private set; } = "";

    [ModuleInitializer]
    internal static void Redirect()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ota-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);

        // Windows: AppDataDirectory falls back to the literal "$LOCALAPPDATA\OpenTabletDriver".
        Environment.SetEnvironmentVariable("LOCALAPPDATA", Path);

        // macOS: "~/Library/Application Support/OpenTabletDriver", with ~ taken from HOME.
        // Linux: the XDG roots below, each defaulting to a path under HOME.
        //
        // Each is CREATED, not merely named. These are real roots that other code opens rather than
        // just joins onto: SingleInstance puts its Linux activation socket straight into
        // XDG_RUNTIME_DIR, and pointing that at a directory which doesn't exist made its test hang
        // until it timed out — on the Linux lane only, so it looked like the flakiness that test has a
        // history of rather than something this redirect did.
        Environment.SetEnvironmentVariable("HOME", Path);
        foreach (var (variable, name) in new[]
                 {
                     ("XDG_DATA_HOME", "data"),
                     ("XDG_CONFIG_HOME", "config"),
                     ("XDG_CACHE_HOME", "cache"),
                 })
        {
            var dir = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(dir);
            Environment.SetEnvironmentVariable(variable, dir);
        }

        // XDG_RUNTIME_DIR gets its own DELIBERATELY SHORT directory, outside the root above.
        //
        // A Unix domain socket path is limited to 108 bytes, and SingleInstance puts its Linux
        // activation socket directly in this directory under a ~78-character name. Nested under the
        // descriptive root the full path came to ~130 bytes and the bind silently failed, so the
        // activation test waited out its 30-second timeout — on the Linux lane only. The real runtime
        // directory is short (/run/user/1000), which is why the product has never hit this.
        //
        // Do not "tidy" this back under Path: the length is the point.
        RuntimePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ota-rt-{Guid.NewGuid():N}"[..14]);
        Directory.CreateDirectory(RuntimePath);
        Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", RuntimePath);

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            foreach (var dir in new[] { Path, RuntimePath })
            {
                try { Directory.Delete(dir, recursive: true); }
                catch { /* best-effort: a leftover temp dir is harmless, a failed teardown is not */ }
            }
        };
    }
}
