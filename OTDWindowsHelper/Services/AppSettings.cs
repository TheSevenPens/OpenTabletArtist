using System.IO;
using Newtonsoft.Json.Linq;

namespace OtdWindowsHelper.Services;

/// <summary>
/// Simple JSON file-based settings persistence.
/// Stores key-value pairs in a settings.json next to the exe.
/// </summary>
public static class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OtdWindowsHelper",
        "settings.json"
    );

    private static JObject? _cache;

    private static JObject Load()
    {
        if (_cache != null) return _cache;
        try
        {
            if (File.Exists(SettingsPath))
            {
                _cache = JObject.Parse(File.ReadAllText(SettingsPath));
                return _cache;
            }
        }
        catch { }
        _cache = new JObject();
        return _cache;
    }

    public static string? Get(string key)
    {
        return Load()[key]?.ToString();
    }

    public static void Set(string key, string value)
    {
        var obj = Load();
        obj[key] = value;
        var dir = Path.GetDirectoryName(SettingsPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(SettingsPath, obj.ToString());
    }
}
