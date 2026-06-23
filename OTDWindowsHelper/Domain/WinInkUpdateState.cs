using OpenTabletDriver.Desktop.Reflection.Metadata;

namespace OtdWindowsHelper.Domain;

/// <summary>
/// Pure plugin update/compatibility logic, extracted from <c>MainViewModel.RecomputeWinInkUpdate</c>
/// and <c>WindowsInkPluginService.GetLatestCompatibleAsync</c> so it can be unit-tested with fake
/// metadata (the network download and disk reads stay in the service/view model).
/// </summary>
public static class WinInkUpdateState
{
    /// <summary>True only when both versions are known and <paramref name="latest"/> is strictly newer.</summary>
    public static bool IsUpdateAvailable(Version? installed, Version? latest)
        => installed != null && latest != null && latest > installed;

    /// <summary>
    /// The newest release named <paramref name="pluginName"/> that <paramref name="otdVersion"/>
    /// supports (per the metadata's own <see cref="PluginMetadata.IsSupportedBy"/> rule), or null.
    /// </summary>
    public static PluginMetadata? SelectNewestCompatible(
        IEnumerable<PluginMetadata> all, Version otdVersion, string pluginName)
        => all
            .Where(m => m.Name == pluginName && m.IsSupportedBy(otdVersion))
            .OrderByDescending(m => m.PluginVersion)
            .FirstOrDefault();
}
