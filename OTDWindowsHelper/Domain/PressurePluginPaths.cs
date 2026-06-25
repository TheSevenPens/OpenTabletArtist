using System.Collections.Generic;
using System.IO;

namespace OtdWindowsHelper.Domain;

/// <summary>
/// Locates the bundled pen-dynamics plugin DLL the app installs into the daemon's plugin
/// directory. Pure (no filesystem access) so the candidate ordering is unit-testable; the caller
/// picks the first that exists. Covers the published layout (bundled next to the app) and the dev
/// build tree (the plugin's own bin output).
/// </summary>
public static class PressurePluginPaths
{
    public const string DllName = "OtdWindowsHelper.Dynamics.dll";

    /// <summary>Subfolder name used both for the bundle next to the app and inside the OTD plugin dir.</summary>
    public const string PluginFolderName = "OtdWindowsHelperDynamics";

    public static IEnumerable<string> SourceCandidates(string baseDir)
    {
        // 1. Bundled next to the app — published release layout: <app>/BundledPlugins/<folder>/<dll>
        yield return Path.GetFullPath(Path.Combine(baseDir, "BundledPlugins", PluginFolderName, DllName));

        // 2/3. Dev build tree: <app>/bin/<cfg>/net10.0 → repo root → plugin build output (net8.0).
        foreach (var config in new[] { "Debug", "Release" })
            yield return Path.GetFullPath(Path.Combine(
                baseDir, "..", "..", "..", "..",
                "plugins", "OtdWindowsHelper.Dynamics", "bin", config, "net8.0", DllName));
    }
}
