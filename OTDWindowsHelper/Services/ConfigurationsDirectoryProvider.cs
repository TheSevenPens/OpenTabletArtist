using System.IO;

namespace OtdWindowsHelper.Services;

/// <summary>
/// Resolves the OpenTabletDriver tablet-configurations directory. Extracted so the Custom
/// Tablet Configs page can be tested against a temp directory instead of the real
/// <c>%AppData%\OpenTabletDriver\Configurations</c> folder (which isn't enumerable in
/// sandboxed/restricted profiles — the long-standing source of environment-sensitive tests).
/// </summary>
public interface IConfigurationsDirectoryProvider
{
    /// <summary>Returns the configurations directory, creating it if possible; empty string if unavailable.</summary>
    string GetOrCreate();
}

/// <inheritdoc />
public class ConfigurationsDirectoryProvider : IConfigurationsDirectoryProvider
{
    public string GetOrCreate()
    {
        // OTD reads tablet configs from %AppData%\OpenTabletDriver\Configurations.
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData)) return "";
        var dir = Path.Combine(appData, "OpenTabletDriver", "Configurations");
        try
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
        catch { }
        return dir;
    }
}
