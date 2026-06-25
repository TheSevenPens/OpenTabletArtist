using System.IO;
using System.Linq;
using OtdWindowsHelper.Domain;

namespace OtdWindowsHelper.Services;

/// <summary>
/// Ensures our bundled pressure-curve plugin is present in the daemon's plugin directory so the
/// app-owned daemon loads it. The source DLL is resolved via <see cref="PressurePluginPaths"/>
/// (bundled next to the app in a release, or the plugin's build output in dev). Everything is
/// best-effort — failures are swallowed (the curve feature simply won't be available).
/// </summary>
public class PressurePluginInstaller
{
    /// <summary>Resolve the bundled DLL and copy it into <paramref name="pluginDirectory"/> if
    /// missing/out of date. Returns true if a copy happened (caller should LoadPlugins).</summary>
    public bool EnsureInstalled(string pluginDirectory)
    {
        var source = PressurePluginPaths.SourceCandidates(AppContext.BaseDirectory).FirstOrDefault(File.Exists);
        return source != null && CopyIfNeeded(source, pluginDirectory);
    }

    /// <summary>Pure copy step (testable): copy <paramref name="sourceDll"/> into the plugin
    /// directory's subfolder when missing or stale. Returns true if it copied.</summary>
    public static bool CopyIfNeeded(string sourceDll, string pluginDirectory)
    {
        if (string.IsNullOrEmpty(pluginDirectory) || !File.Exists(sourceDll)) return false;
        try
        {
            var destDir = Path.Combine(pluginDirectory, PressurePluginPaths.PluginFolderName);
            var dest = Path.Combine(destDir, PressurePluginPaths.DllName);
            if (!IsCopyNeeded(sourceDll, dest)) return false;
            Directory.CreateDirectory(destDir);
            File.Copy(sourceDll, dest, overwrite: true);
            return true;
        }
        catch { return false; }
    }

    private static bool IsCopyNeeded(string source, string dest)
    {
        if (!File.Exists(dest)) return true;
        var s = new FileInfo(source);
        var d = new FileInfo(dest);
        return s.Length != d.Length || s.LastWriteTimeUtc > d.LastWriteTimeUtc;
    }
}
