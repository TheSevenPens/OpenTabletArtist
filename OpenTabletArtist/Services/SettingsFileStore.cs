using System;
using System.IO;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop;

namespace OpenTabletArtist.Services;

/// <summary>
/// Reads and writes OpenTabletDriver <see cref="Settings"/> to disk. Centralizes the
/// serialize/deserialize calls that were duplicated across <c>MainViewModel</c>
/// (settings write-back and snapshot save/load) behind a filesystem seam.
///
/// Two save flavours match the existing call-site behavior: <see cref="Save"/> propagates
/// failures (snapshot writes), while <see cref="TrySave"/> is best-effort and reports
/// success via its return value (settings write-back) — the return value is the seam that
/// #21 will use to surface persistence failures instead of swallowing them.
/// </summary>
public interface ISettingsFileStore
{
    /// <summary>Serializes <paramref name="settings"/> to <paramref name="path"/>. Throws on failure.</summary>
    void Save(Settings settings, string path);

    /// <summary>Best-effort serialize. Returns true on success, false if the write failed.</summary>
    bool TrySave(Settings settings, string path);

    /// <summary>Loads settings from <paramref name="path"/>. Returns false if the file is missing or invalid.</summary>
    bool TryLoad(string path, out Settings? settings);
}

/// <inheritdoc />
public class SettingsFileStore : ISettingsFileStore
{
    /// <summary>
    /// Matches OpenTabletDriver's own serializer exactly (<c>Formatting.Indented</c>, otherwise stock) so
    /// the file we write stays byte-compatible with what OTD's UX reads and writes. OTA serializes itself
    /// rather than calling <c>Settings.Serialize</c> because that method swallows
    /// <see cref="UnauthorizedAccessException"/> internally and only logs it — leaving OTA unable to tell
    /// a completed save from a refused one (#732).
    /// </summary>
    private static readonly JsonSerializer Serializer = new() { Formatting = Formatting.Indented };

    /// <summary>Suffix of the last-known-good copy kept beside the settings file (#733).</summary>
    public const string BackupSuffix = AtomicFile.BackupSuffix;

    public void Save(Settings settings, string path) => WriteAtomic(settings, path);

    public bool TrySave(Settings settings, string path)
    {
        try
        {
            WriteAtomic(settings, path);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Couldn't save settings to {path}.", ex);
            return false;
        }
    }

    /// <summary>
    /// Writes settings without ever leaving the destination in a half-written or missing state (#733).
    ///
    /// Upstream's <c>Settings.Serialize</c> deletes the existing file and then creates the replacement,
    /// so an interruption, a serialization failure, or a directory that denies creation can destroy the
    /// only usable settings file. <see cref="AtomicFile"/> writes aside and swaps instead, keeping what
    /// it replaced as the <see cref="BackupSuffix"/> copy that <see cref="TryLoad"/> falls back to.
    ///
    /// Throws on failure; <see cref="TrySave"/> is the catching flavour.
    /// </summary>
    private static void WriteAtomic(Settings settings, string path) =>
        AtomicFile.Write(path, stream =>
        {
            // leaveOpen, because AtomicFile owns the stream and still has to flush it to disk.
            using var writer = new StreamWriter(stream, leaveOpen: true);
            using var json = new JsonTextWriter(writer);
            Serializer.Serialize(json, settings);
            json.Flush();
            writer.Flush();
        });

    public bool TryLoad(string path, out Settings? settings)
    {
        if (TryLoadFile(path, out settings)) return true;

        // The main file is missing or unreadable. A backup exists only when a previous write completed,
        // so what it holds is the last settings OTA is known to have saved successfully (#733).
        if (TryLoadFile(path + BackupSuffix, out settings))
        {
            AppLog.Warn($"Settings at {path} were unreadable; recovered the last good copy from " +
                        $"{Path.GetFileName(path) + BackupSuffix}.");
            return true;
        }

        settings = null;
        return false;
    }

    private static bool TryLoadFile(string path, out Settings? settings)
    {
        settings = null;
        try
        {
            var file = new FileInfo(path);
            // Settings.TryDeserialize only guards JsonException — a missing file would throw,
            // so check existence first and wrap the rest defensively.
            if (!file.Exists) return false;
            if (Settings.TryDeserialize(file, out var loaded) && loaded != null)
            {
                settings = loaded;
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
