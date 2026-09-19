using System;
using System.IO;
using OpenTabletDriver.Desktop;
using OtdInterop;

namespace OtdInterop;

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
    private readonly IOtdLog _log;

    /// <param name="log">
    /// Where recovery and write failures are reported. Required rather than optional: this type's one
    /// unique message is that it fell back to the last-known-good copy, and a store that can silently
    /// stop saying so is the failure it exists to prevent.
    /// </param>
    public SettingsFileStore(IOtdLog log) => _log = log;

    // Serialization itself lives in OtdInterop's SettingsCodec now (#807), so this and the preset writer
    // cannot drift into producing different JSON for the same type. What stays here is the authority:
    // which path, whether a failure throws or is reported, and the last-known-good fallback.
    //
    // Still not upstream's Settings.Serialize, for the original reason (#732): that method swallows
    // UnauthorizedAccessException and only logs it, so a caller cannot tell a completed save from a
    // refused one. It also deletes the file before writing the replacement.

    /// <summary>Suffix of the last-known-good copy kept beside the settings file (#733).</summary>
    public const string BackupSuffix = AtomicFile.BackupSuffix;

    /// <inheritdoc />
    public void Save(Settings settings, string path) => WriteAtomic(settings, path);

    /// <inheritdoc />
    public bool TrySave(Settings settings, string path)
    {
        try
        {
            WriteAtomic(settings, path);
            return true;
        }
        catch (Exception ex)
        {
            _log.Warn($"Couldn't save settings to {path}.", ex);
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
        AtomicFile.Write(path, stream => SettingsCodec.Encode(settings, stream));

    /// <inheritdoc />
    public bool TryLoad(string path, out Settings? settings)
    {
        if (TryLoadFile(path, out settings)) return true;

        // The main file is missing or unreadable. A backup exists only when a previous write completed,
        // so what it holds is the last settings OTA is known to have saved successfully (#733).
        if (TryLoadFile(path + BackupSuffix, out settings))
        {
            _log.Warn($"Settings at {path} were unreadable; recovered the last good copy from " +
                        $"{Path.GetFileName(path) + BackupSuffix}.");
            return true;
        }

        settings = null;
        return false;
    }

    private bool TryLoadFile(string path, out Settings? settings)
    {
        settings = null;
        try
        {
            // The codec takes a stream and so cannot tell "not there" from "not readable"; that is this
            // method's job, and it matters because only the second case is worth falling back for.
            if (!File.Exists(path)) return false;
            using var stream = File.OpenRead(path);
            return SettingsCodec.TryDecode(stream, out settings);
        }
        catch
        {
            return false;
        }
    }
}
