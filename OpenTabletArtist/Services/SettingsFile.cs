using System;
using System.IO;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// The read and write sides of <see cref="AppSettings"/>, extracted so they take an explicit path and are
/// unit-testable without touching the real <c>%LOCALAPPDATA%</c> file (#21). Writes go through
/// <see cref="AtomicFile"/> so a failure can't truncate the previous preferences, and reads fall back to
/// the last-known-good copy that leaves behind (#768).
/// </summary>
public static class SettingsFile
{
    /// <summary>Write the settings JSON to <paramref name="path"/>, creating the directory if needed.
    /// Never throws — returns false when the write failed, so a preference write can't take down whatever
    /// the caller was actually doing (#735). The caller is responsible for reporting the failure.
    ///
    /// Written through <see cref="AtomicFile"/> (#768): a failed or interrupted write leaves the previous
    /// preferences intact rather than truncating them, and the file it replaces is kept as the
    /// last-known-good copy that <see cref="Read"/> recovers from.</summary>
    public static bool Write(string path, JObject data)
    {
        try
        {
            AtomicFile.WriteText(path, data.ToString());
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Read the settings JSON at <paramref name="path"/>. Never throws. A missing or clean file
    /// yields <see cref="SettingsLoadStatus.Ok"/>. An unreadable one is moved aside to a backup named with
    /// <paramref name="timestamp"/> — so the next save can't overwrite it and it stays diagnosable — and
    /// then the last-known-good copy left by <see cref="Write"/> is tried, giving
    /// <see cref="SettingsLoadStatus.Recovered"/> if it loads. Only when there is nothing to recover does
    /// this fall back to defaults, reported as <see cref="SettingsLoadStatus.Preserved"/> or
    /// <see cref="SettingsLoadStatus.NotPreserved"/> depending on whether the move aside succeeded.</summary>
    public static (JObject Data, SettingsLoadOutcome Outcome) Read(string path, DateTime timestamp)
    {
        if (TryParse(path, out var data))
            return (data, SettingsLoadOutcome.Ok);

        var backup = path + AtomicFile.BackupSuffix;

        if (!File.Exists(path))
        {
            // No settings file at all. Normally first run — but a backup with no main file means a write
            // was interrupted part-way through the swap, and the backup is still the user's preferences.
            if (TryParse(backup, out var orphaned))
                return (orphaned, Recovered(backup));
            return (new JObject(), SettingsLoadOutcome.Ok);
        }

        // The file is there but unreadable. Move it aside first: that has to happen whether or not the
        // recovery below works, because otherwise the next save overwrites the evidence.
        var preserved = Preserve(path, timestamp);
        if (TryParse(backup, out var recovered))
            return (recovered, Recovered(backup));

        return (new JObject(), preserved);
    }

    private static SettingsLoadOutcome Recovered(string backup) =>
        new(SettingsLoadStatus.Recovered, Path.GetFileName(backup));

    private static bool TryParse(string path, out JObject data)
    {
        try
        {
            if (File.Exists(path))
            {
                data = JObject.Parse(File.ReadAllText(path));
                return true;
            }
        }
        catch
        {
            // Unreadable or not JSON; the caller decides what to fall back to.
        }

        data = new JObject();
        return false;
    }

    private static SettingsLoadOutcome Preserve(string path, DateTime timestamp)
    {
        try
        {
            var backup = $"{path}.corrupt-{timestamp:yyyyMMdd-HHmmss}";
            File.Move(path, backup, overwrite: true);
            return new SettingsLoadOutcome(SettingsLoadStatus.Preserved, Path.GetFileName(backup));
        }
        catch
        {
            // The unreadable file is still at `path` — a later save could overwrite it. Report honestly.
            return new SettingsLoadOutcome(SettingsLoadStatus.NotPreserved, null);
        }
    }
}
