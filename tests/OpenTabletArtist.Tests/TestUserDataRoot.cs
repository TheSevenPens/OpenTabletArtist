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

    [ModuleInitializer]
    internal static void Redirect()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ota-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);

        // Windows: AppDataDirectory falls back to the literal "$LOCALAPPDATA\OpenTabletDriver".
        Environment.SetEnvironmentVariable("LOCALAPPDATA", Path);

        // macOS: "~/Library/Application Support/OpenTabletDriver", with ~ taken from HOME.
        // Linux: the XDG roots below, each defaulting to a path under HOME.
        Environment.SetEnvironmentVariable("HOME", Path);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", System.IO.Path.Combine(Path, "data"));
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", System.IO.Path.Combine(Path, "config"));
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", System.IO.Path.Combine(Path, "cache"));
        Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", System.IO.Path.Combine(Path, "run"));

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { /* best-effort: a leftover temp dir is harmless, a failed teardown is not */ }
        };
    }
}
