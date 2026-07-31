using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Reflection.Metadata;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Reads the locally installed "Windows Ink" plugin metadata and queries the
/// official OTD Plugin-Repository for the latest compatible release. The actual
/// install/uninstall is performed by the daemon (see <see cref="DaemonClient"/>);
/// this service only deals with metadata.
/// </summary>
public class WindowsInkPluginService
{
    /// <summary>Folder/metadata name used by the plugin (matches metadata.json "Name").</summary>
    public const string PluginName = "Windows Ink";

    /// <summary>
    /// The OpenTabletDriver version this app is built against. The daemon is built
    /// from the same submodule, so this equals the running daemon's version.
    /// </summary>
    public static Version OtdVersion =>
        typeof(AppInfo).Assembly.GetName().Version ?? new Version(0, 0);

    /// <summary>
    /// Reads the installed plugin's metadata.json from the daemon's plugin directory.
    /// Returns null if the plugin isn't installed or the metadata can't be read.
    /// </summary>
    public PluginMetadata? ReadInstalled(string? pluginDirectory)
    {
        if (string.IsNullOrEmpty(pluginDirectory))
            return null;

        var path = Path.Combine(pluginDirectory, PluginName, "metadata.json");
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonConvert.DeserializeObject<PluginMetadata>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Full path to the installed plugin's directory (used for uninstall).</summary>
    public string GetPluginDirectoryPath(string pluginDirectory) =>
        Path.Combine(pluginDirectory, PluginName);

    /// <summary>
    /// Downloads the official Plugin-Repository and returns the newest "Windows Ink" release compatible
    /// with the current OTD version. Returns a typed <see cref="PluginLookupResult"/> so callers can tell
    /// a genuine "reached it, nothing compatible" from a network or parse failure, rather than every case
    /// collapsing into null (#21).
    /// </summary>
    public async Task<PluginLookupResult> GetLatestCompatibleAsync()
    {
        try
        {
            var all = await PluginMetadataCollection.DownloadAsync();
            var metadata = WinInkUpdateState.SelectNewestCompatible(all, OtdVersion, PluginName);
            return metadata != null ? PluginLookupResult.Found(metadata) : PluginLookupResult.NoCompatibleRelease;
        }
        catch (Exception ex)
        {
            // Distinguish "couldn't reach the repo" from "reached it but couldn't read it" (#21).
            return PluginLookupResult.FromException(ex);
        }
    }

    /// <summary>True if the installed plugin declares support for the current OTD version.</summary>
    public static bool IsCompatible(PluginMetadata metadata) =>
        metadata.IsSupportedBy(OtdVersion);
}
