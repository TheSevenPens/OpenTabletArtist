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

    /// <summary>True when the most recent preference write failed to reach disk (#735). Preference writes
    /// are best-effort by design — they must never abort the caller — so this is how a failure becomes
    /// visible instead of silent. Cleared by the next successful write.</summary>
    public static bool LastWriteFailed { get; private set; }

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

    /// <summary>
    /// Writes several keys in one load/serialize/write instead of one full file rewrite per key (#735).
    /// Returns false if the write failed. The in-memory cache is updated either way, so the running app
    /// stays consistent with itself even when the file is unwritable.
    /// </summary>
    public static bool SetMany(IReadOnlyDictionary<string, string> values)
    {
        if (values.Count == 0) return true;
        var obj = Load();
        foreach (var (key, value) in values)
            obj[key] = value;
        return Persist(obj);
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

    /// <summary>
    /// Best-effort write of the whole preference file. Never throws (#735): these are conveniences —
    /// a theme tint, a tray hint, a last-seen timestamp — and none of them is worth aborting the caller
    /// for. The outcome is recorded in <see cref="LastWriteFailed"/> and logged rather than swallowed.
    /// </summary>
    private static bool Persist(JObject obj)
    {
        bool ok = SettingsFile.Write(SettingsPath, obj);
        if (!ok && !LastWriteFailed)
            AppLog.Warn($"Couldn't write app preferences to {SettingsPath}; changes will be lost on restart.");
        LastWriteFailed = !ok;
        return ok;
    }
}
