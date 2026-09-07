using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;

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

    /// <summary>How the settings file loaded on startup (#21). <see cref="SettingsLoadStatus.Ok"/> normally;
    /// otherwise the file was unreadable and either preserved to a backup (recoverable) or, worse, couldn't
    /// even be moved aside. The Home health check reads this so a corrupt settings file isn't silently
    /// swallowed — and the copy it shows matches what actually happened (backup name and all).</summary>
    public static SettingsLoadOutcome LoadOutcome { get; private set; } = SettingsLoadOutcome.Ok;

    private static JObject Load()
    {
        if (_cache != null) return _cache;
        // The read + corrupt-file preservation lives in the testable SettingsFile helper (#21).
        (_cache, LoadOutcome) = SettingsFile.Read(SettingsPath, DateTime.Now);
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

    /// <summary>
    /// Every stored key beginning with <paramref name="prefix"/>, as a materialised snapshot — so the
    /// caller can <see cref="Remove"/> as it walks the result. Added for the hotkey reconcile
    /// (#hotkey-orphans), which has to reason about what is PERSISTED rather than about what one process
    /// happened to register.
    /// </summary>
    public static IReadOnlyList<string> Keys(string prefix)
    {
        var keys = new List<string>();
        foreach (var p in Load().Properties())
            if (p.Name.StartsWith(prefix, StringComparison.Ordinal))
                keys.Add(p.Name);
        return keys;
    }

    private static void Persist(Newtonsoft.Json.Linq.JObject obj)
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(SettingsPath, obj.ToString());
    }
}
