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
    public const string BackupSuffix = ".bak";

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
    /// only usable settings file. Here the new content is written to a temporary file in the same
    /// directory, flushed to disk, and only then swapped in — so a failure at any point leaves the
    /// previous file exactly where it was, still loadable. The swap keeps the file it replaced as a
    /// <see cref="BackupSuffix"/> copy, which <see cref="TryLoad"/> falls back to.
    ///
    /// Throws on failure; <see cref="TrySave"/> is the catching flavour.
    /// </summary>
    private static void WriteAtomic(Settings settings, string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);

        // Same directory as the destination: a cross-volume temp file would make the swap a copy, which
        // is neither atomic nor guaranteed to succeed.
        var temp = Path.Combine(dir, $".{Path.GetFileName(path)}.tmp-{Guid.NewGuid():N}");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            using (var json = new JsonTextWriter(writer))
            {
                Serializer.Serialize(json, settings);
                json.Flush();
                writer.Flush();
                // Force the bytes out of the OS cache before the swap. Without this a crash right after
                // the rename can leave a correctly-named file with no content in it.
                stream.Flush(flushToDisk: true);
            }

            Replace(temp, path);
        }
        catch
        {
            // The destination was never touched — drop the partial temp file and let the caller hear why.
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>Swaps <paramref name="temp"/> into <paramref name="path"/>, retaining the previous
    /// contents as the last-known-good backup.</summary>
    private static void Replace(string temp, string path)
    {
        if (!File.Exists(path))
        {
            // Nothing to preserve or replace — first write, or the file was removed behind us.
            File.Move(temp, path);
            return;
        }

        var backup = path + BackupSuffix;
        try
        {
            // Atomic where the platform supports it, and it writes the backup as part of the same
            // operation — the previous settings survive even a crash mid-swap.
            File.Replace(temp, path, backup, ignoreMetadataErrors: true);
        }
        catch (PlatformNotSupportedException)
        {
            ReplaceByMove(temp, path, backup);
        }
        catch (IOException)
        {
            // Some filesystems (notably a few network and FUSE mounts) reject Replace outright. Fall back
            // to a copy-aside + move, which is still strictly safer than delete-then-create.
            ReplaceByMove(temp, path, backup);
        }
    }

    private static void ReplaceByMove(string temp, string path, string backup)
    {
        File.Copy(path, backup, overwrite: true);
        File.Move(temp, path, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort cleanup; the temp name is unique so a leftover can't corrupt anything */ }
    }

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
