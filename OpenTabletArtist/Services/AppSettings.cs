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

    /// <summary>Non-null when the settings file existed but couldn't be read/parsed on load: a short,
    /// user-facing explanation (including where the unreadable file was preserved). The Home health check
    /// surfaces it so a corrupt settings file isn't silently swallowed — and, crucially, not silently
    /// overwritten and lost by the next save (#21). Null when settings loaded cleanly (or none existed).</summary>
    public static string? LoadError { get; private set; }

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
        catch (Exception ex)
        {
            // The file exists but couldn't be read/parsed. Move it aside (timestamped) BEFORE we continue,
            // so the next Set() doesn't overwrite it and permanently lose the user's settings — the whole
            // danger of the old silent `catch {}`. Record why so Home can surface it (#21).
            LoadError = PreserveUnreadable(ex);
        }
        _cache = new JObject();
        return _cache;
    }

    /// <summary>Move an unreadable settings file to a timestamped backup so it isn't clobbered by the next
    /// save. Returns a user-facing explanation; best-effort — if the move itself fails, still explains that
    /// the app fell back to defaults.</summary>
    private static string PreserveUnreadable(Exception ex)
    {
        try
        {
            var backup = $"{SettingsPath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(SettingsPath, backup, overwrite: true);
            return $"Your settings file couldn't be read ({ex.Message}). It was set aside as \"{Path.GetFileName(backup)}\" " +
                   "and the app started with defaults — restore that file to recover your settings.";
        }
        catch
        {
            return $"Your settings file couldn't be read ({ex.Message}), so the app started with defaults.";
        }
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
