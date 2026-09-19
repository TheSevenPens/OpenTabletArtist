using System;
using System.IO;
using OpenTabletDriver.Desktop;
using OtdInterop;

namespace OpenTabletArtist.Services;

/// <summary>Reading and writing preset files — named snapshots the user saves and loads by hand.</summary>
public interface IPresetStore
{
    /// <summary>Writes <paramref name="settings"/> to <paramref name="path"/>. Throws on failure.</summary>
    void Save(Settings settings, string path);

    /// <summary>Loads a preset. False when the file is missing or unreadable.</summary>
    bool TryLoad(string path, out Settings? settings);
}

/// <summary>
/// The app's authority over its own preset files (#807).
/// </summary>
///
/// <remarks>
/// <para>
/// Presets and the active settings file are two different authorities that happen to hold the same type.
/// They were one store taking a path, which meant the thing that writes the daemon's live settings and
/// the thing that writes a user's saved snapshot were the same object with the destination as an
/// argument — so any caller holding it could write either.
/// </para>
/// <para>
/// The active-settings writer now belongs to OtdInterop and is not handed out. This is the other half:
/// presets are the app's own concern, kept here, and no part of the app can reach the daemon's settings
/// file through it.
/// </para>
/// <para>
/// It still uses the library's <see cref="SettingsCodec"/> rather than serializing for itself. One
/// encoder means a preset and the live settings file cannot drift into different JSON for the same type
/// — and the encoding is not arbitrary: OpenTabletDriver's own interface reads these files.
/// </para>
/// </remarks>
public sealed class PresetStore : IPresetStore
{
    private readonly IOtdLog _log;

    /// <param name="log">Where a recovery from the backup copy is reported.</param>
    public PresetStore(IOtdLog log) => _log = log;

    /// <inheritdoc />
    public void Save(Settings settings, string path) =>
        AtomicFile.Write(path, stream => SettingsCodec.Encode(settings, stream));

    /// <inheritdoc />
    public bool TryLoad(string path, out Settings? settings)
    {
        if (TryLoadFile(path, out settings)) return true;

        // A backup exists only where a previous write completed, so what it holds is the last preset
        // known to have been saved successfully.
        if (TryLoadFile(path + AtomicFile.BackupSuffix, out settings))
        {
            _log.Warn($"The preset at {path} was unreadable; recovered the last good copy from " +
                      $"{Path.GetFileName(path) + AtomicFile.BackupSuffix}.");
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
            // Existence is checked here rather than in the codec: a stream cannot distinguish "not there"
            // from "not readable", and only the second is worth falling back for.
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
