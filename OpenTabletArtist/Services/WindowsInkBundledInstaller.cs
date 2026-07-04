using System.IO;

namespace OpenTabletArtist.Services;

/// <summary>
/// Installs the Windows Ink plugin from the copy bundled with the app (<c>BundledPlugins/WindowsInk/</c>)
/// as an offline fallback when the daemon can't download it from the plugin repository (#364). The
/// bundle holds the plugin DLLs plus the manifest as <c>metadata.json</c> — the same file the online
/// install writes — so a plain copy into the daemon's plugin directory is a complete, recognised
/// install (<see cref="WindowsInkPluginService.ReadInstalled"/> finds it). Pure copy; the caller then
/// reloads plugins. Best-effort — failures return <see cref="PluginInstallOutcome.None"/>.
/// </summary>
public class WindowsInkBundledInstaller
{
    /// <summary>The bundle directory next to the app, or null when it isn't bundled (e.g. dev builds).</summary>
    public static string? BundleDirectory()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "BundledPlugins", "WindowsInk");
        return File.Exists(Path.Combine(dir, "metadata.json")) ? dir : null;
    }

    /// <summary>Copy the bundled plugin into <paramref name="pluginDirectory"/> if it's present.</summary>
    public PluginInstallOutcome EnsureInstalled(string pluginDirectory)
    {
        var bundle = BundleDirectory();
        return bundle == null ? PluginInstallOutcome.None : CopyIfNeeded(bundle, pluginDirectory);
    }

    /// <summary>Pure copy step (testable): copy every file in <paramref name="sourceDir"/> into the
    /// plugin directory's "Windows Ink" subfolder, reporting a fresh install vs. an update.</summary>
    public static PluginInstallOutcome CopyIfNeeded(string sourceDir, string pluginDirectory)
    {
        if (string.IsNullOrEmpty(pluginDirectory) || !Directory.Exists(sourceDir))
            return PluginInstallOutcome.None;
        try
        {
            var destDir = Path.Combine(pluginDirectory, WindowsInkPluginService.PluginName);
            // "Update" if the daemon already had this plugin loaded (a restart is needed to swap the DLLs);
            // "Install" if the folder is new (a LoadPlugins imports it).
            var existed = File.Exists(Path.Combine(destDir, "metadata.json"));
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
            return existed ? PluginInstallOutcome.Updated : PluginInstallOutcome.Installed;
        }
        catch
        {
            return PluginInstallOutcome.None;
        }
    }
}
