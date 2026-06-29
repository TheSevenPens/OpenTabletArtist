using System.IO;
using System.Linq;
using OtdArtist.Domain;

namespace OtdArtist.Services;

/// <summary>What an ensure/copy did — the caller reacts differently to each.</summary>
public enum PluginInstallOutcome
{
    /// <summary>Nothing copied (already up to date, or source missing).</summary>
    None,
    /// <summary>Copied into a directory that didn't exist — the daemon hadn't loaded it, so a
    /// <c>LoadPlugins</c> imports it.</summary>
    Installed,
    /// <summary>Overwrote a plugin the daemon already loaded at startup. <c>LoadPlugins</c> won't
    /// replace an already-loaded directory, so the daemon must restart to pick up the new DLL.</summary>
    Updated,
}

/// <summary>
/// Ensures our bundled pressure-curve plugin is present in the daemon's plugin directory so the
/// app-owned daemon loads it. The source DLL is resolved via <see cref="PressurePluginPaths"/>
/// (bundled next to the app in a release, or the plugin's build output in dev). Everything is
/// best-effort — failures are swallowed (the curve feature simply won't be available).
/// </summary>
public class PressurePluginInstaller
{
    /// <summary>Resolve the bundled DLL and copy it into <paramref name="pluginDirectory"/> if
    /// missing/out of date, reporting whether it was a fresh install or an update.</summary>
    public PluginInstallOutcome EnsureInstalled(string pluginDirectory)
    {
        var source = PressurePluginPaths.SourceCandidates(AppContext.BaseDirectory).FirstOrDefault(File.Exists);
        return source == null ? PluginInstallOutcome.None : CopyIfNeeded(source, pluginDirectory);
    }

    /// <summary>Pure copy step (testable): copy <paramref name="sourceDll"/> into the plugin
    /// directory's subfolder when missing or stale.</summary>
    public static PluginInstallOutcome CopyIfNeeded(string sourceDll, string pluginDirectory)
    {
        if (string.IsNullOrEmpty(pluginDirectory) || !File.Exists(sourceDll)) return PluginInstallOutcome.None;
        try
        {
            var destDir = Path.Combine(pluginDirectory, PressurePluginPaths.PluginFolderName);
            var dest = Path.Combine(destDir, PressurePluginPaths.DllName);
            var existed = File.Exists(dest);
            if (existed && !IsStale(sourceDll, dest)) return PluginInstallOutcome.None;
            Directory.CreateDirectory(destDir);
            File.Copy(sourceDll, dest, overwrite: true);
            return existed ? PluginInstallOutcome.Updated : PluginInstallOutcome.Installed;
        }
        catch { return PluginInstallOutcome.None; }
    }

    private static bool IsStale(string source, string dest)
    {
        var s = new FileInfo(source);
        var d = new FileInfo(dest);
        return s.Length != d.Length || s.LastWriteTimeUtc > d.LastWriteTimeUtc;
    }
}
