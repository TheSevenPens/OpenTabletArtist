using System.IO;
using OpenTabletDriver.Desktop;

namespace OtdWindowsHelper.Services;

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
    public void Save(Settings settings, string path) =>
        settings.Serialize(new FileInfo(path));

    public bool TrySave(Settings settings, string path)
    {
        try
        {
            settings.Serialize(new FileInfo(path));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryLoad(string path, out Settings? settings)
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
