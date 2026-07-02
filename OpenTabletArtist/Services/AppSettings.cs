using System.IO;
using Newtonsoft.Json.Linq;

namespace OpenTabletArtist.Services;

/// <summary>
/// Simple JSON file-based settings persistence.
/// Stores key-value pairs in a settings.json next to the exe.
/// </summary>
public static class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenTabletArtist",
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
        Persist(obj);
    }

    /// <summary>Removes a key (no-op if absent). Used to clear a snapshot's hotkey mapping (#320).</summary>
    public static void Remove(string key)
    {
        var obj = Load();
        if (obj.Remove(key)) Persist(obj);
    }

    private static void Persist(Newtonsoft.Json.Linq.JObject obj)
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(SettingsPath, obj.ToString());
    }
}
