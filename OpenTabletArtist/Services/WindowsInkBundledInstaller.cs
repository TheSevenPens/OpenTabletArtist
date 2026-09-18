using System;
using System.IO;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop.Reflection.Metadata;

namespace OpenTabletArtist.Services;

/// <summary>
/// Installs the Windows Ink plugin from the copy bundled with the app (<c>BundledPlugins/WindowsInk/</c>)
/// as an offline fallback when the daemon can't download it from the plugin repository (#364). The
/// bundle holds the plugin DLLs plus the manifest as <c>metadata.json</c> — the same file the online
/// install writes — so a plain copy into the daemon's plugin directory is a complete, recognised
/// install (<see cref="WindowsInkPluginService.ReadInstalled"/> finds it). The caller then reloads plugins.
///
/// The bundle is checked against the same compatibility rule the online path uses before anything is
/// copied (#739). It previously went in unconditionally, so a bundle built against a different OTD
/// version — which the release workflow could pick up, since it took the newest manifest without
/// consulting the driver version — would be installed anyway and break the pen.
/// </summary>
public class WindowsInkBundledInstaller
{
    /// <summary>The bundle directory next to the app, or null when it isn't bundled (e.g. dev builds).</summary>
    public static string? BundleDirectory()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "BundledPlugins", "WindowsInk");
        return File.Exists(Path.Combine(dir, "metadata.json")) ? dir : null;
    }

    /// <summary>Copy the bundled plugin into <paramref name="pluginDirectory"/> if it's present and
    /// supports the OTD version this app ships.</summary>
    public PluginInstallOutcome EnsureInstalled(string pluginDirectory)
    {
        var bundle = BundleDirectory();
        return bundle == null
            ? PluginInstallOutcome.None
            : InstallIfCompatible(bundle, pluginDirectory, WindowsInkPluginService.OtdVersion);
    }

    /// <summary>
    /// Gate then copy (testable): refuse a bundle whose manifest is missing, unreadable, or declares no
    /// support for <paramref name="otdVersion"/>. Same predicate as the online lookup
    /// (<see cref="PluginMetadata.IsSupportedBy"/>), so offline and online agree on what is installable.
    /// </summary>
    public static PluginInstallOutcome InstallIfCompatible(string sourceDir, string pluginDirectory,
        Version otdVersion)
    {
        var metadata = ReadBundleMetadata(sourceDir);
        if (metadata == null)
            return PluginInstallOutcome.None;   // missing or corrupt — already logged

        // A manifest with no declared minimum can't be judged: upstream's IsSupportedBy dereferences
        // SupportedDriverVersion unconditionally and throws on it. Treat it as a broken bundle rather
        // than guessing, which would put the decision back where this fix took it from.
        if (metadata.SupportedDriverVersion == null)
        {
            AppLog.Warn("Not installing the bundled Windows Ink plugin: its manifest declares no " +
                        "supported OpenTabletDriver version, so compatibility can't be checked.");
            return PluginInstallOutcome.None;
        }

        if (!metadata.IsSupportedBy(otdVersion))
        {
            AppLog.Warn($"Not installing the bundled Windows Ink plugin v{metadata.PluginVersion}: it " +
                        $"supports OpenTabletDriver {metadata.SupportedDriverVersion} to " +
                        $"{(object?)metadata.MaxSupportedDriverVersion ?? "any"}, and this build ships {otdVersion}.");
            return PluginInstallOutcome.Incompatible;
        }

        return CopyIfNeeded(sourceDir, pluginDirectory);
    }

    /// <summary>The bundle's manifest, or null when it's absent or unparseable. A corrupt manifest is a
    /// broken bundle — the daemon reads the same file, so copying it in would produce an install that
    /// can't be recognised or updated.</summary>
    public static PluginMetadata? ReadBundleMetadata(string sourceDir)
    {
        var path = Path.Combine(sourceDir, "metadata.json");
        if (!File.Exists(path)) return null;
        try
        {
            var metadata = JsonConvert.DeserializeObject<PluginMetadata>(File.ReadAllText(path));
            if (metadata == null)
                AppLog.Warn($"The bundled Windows Ink manifest at {path} is empty.");
            return metadata;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Couldn't read the bundled Windows Ink manifest at {path}.", ex);
            return null;
        }
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
        catch (Exception ex)
        {
            AppLog.Warn($"Couldn't copy the bundled Windows Ink plugin into {pluginDirectory}.", ex);
            return PluginInstallOutcome.None;
        }
    }
}
