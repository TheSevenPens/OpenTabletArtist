using System;
using System.IO;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// The read side of <see cref="AppSettings"/>, extracted so it takes an explicit path + timestamp and is
/// unit-testable without touching the real <c>%LOCALAPPDATA%</c> file (#21). Reads and parses the JSON; on a
/// read/parse failure it moves the unreadable file aside to a timestamped backup <em>before</em> returning
/// defaults, and reports whether that preservation succeeded so the caller can describe recovery truthfully.
/// </summary>
public static class SettingsFile
{
    /// <summary>Read the settings JSON at <paramref name="path"/>. Never throws: a missing or clean file
    /// yields <see cref="SettingsLoadStatus.Ok"/>; an unreadable one is moved to a backup named with
    /// <paramref name="timestamp"/> and reported as <see cref="SettingsLoadStatus.Preserved"/>, or
    /// <see cref="SettingsLoadStatus.NotPreserved"/> if even the move failed.</summary>
    public static (JObject Data, SettingsLoadOutcome Outcome) Read(string path, DateTime timestamp)
    {
        try
        {
            if (File.Exists(path))
                return (JObject.Parse(File.ReadAllText(path)), SettingsLoadOutcome.Ok);
            return (new JObject(), SettingsLoadOutcome.Ok);
        }
        catch
        {
            return (new JObject(), Preserve(path, timestamp));
        }
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
