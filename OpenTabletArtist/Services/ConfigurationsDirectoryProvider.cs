using System;
using System.IO;

namespace OpenTabletArtist.Services;

/// <summary>
/// Resolves the OpenTabletDriver tablet-configurations directory. Extracted so the Custom
/// Tablet Configs page can be tested against a temp directory instead of the real folder
/// (which isn't enumerable in sandboxed/restricted profiles — the long-standing source of
/// environment-sensitive tests).
/// </summary>
public interface IConfigurationsDirectoryProvider
{
    /// <summary>Returns the configurations directory, creating it if possible; empty string if unavailable.</summary>
    string GetOrCreate();
}

/// <summary>
/// Resolves the config folder the daemon actually reads. The authoritative source is the daemon's
/// <c>AppInfo.ConfigurationDirectory</c> (Windows: portable <c>userdata\Configurations</c> or
/// <c>%LOCALAPPDATA%\OpenTabletDriver\Configurations</c>), supplied via <paramref name="daemonDirectory"/>.
/// When the daemon isn't connected yet we fall back to the <b>Local</b>-AppData heuristic — matching OTD's
/// own default, not the Roaming folder the old code guessed (which the daemon never reads).
/// </summary>
public class ConfigurationsDirectoryProvider : IConfigurationsDirectoryProvider
{
    private readonly Func<string?>? _daemonDirectory;

    public ConfigurationsDirectoryProvider(Func<string?>? daemonDirectory = null)
        => _daemonDirectory = daemonDirectory;

    public string GetOrCreate()
    {
        // Prefer the daemon's real path when known.
        var dir = _daemonDirectory?.Invoke();
        if (string.IsNullOrEmpty(dir))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(appData)) return "";
            dir = Path.Combine(appData, "OpenTabletDriver", "Configurations");
        }
        try
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
        catch { }
        return dir;
    }
}
