using System;
using System.IO;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// The read and write sides of <see cref="AppSettings"/>, extracted so they take an explicit path and are
/// unit-testable without touching the real <c>%LOCALAPPDATA%</c> file (#21). Reads and parses the JSON; on a
/// read/parse failure it moves the unreadable file aside to a timestamped backup <em>before</em> returning
/// defaults, and reports whether that preservation succeeded so the caller can describe recovery truthfully.
/// </summary>
public static class SettingsFile
{
    /// <summary>Write the settings JSON to <paramref name="path"/>, creating the directory if needed.
    /// Never throws — returns false when the write failed, so a preference write can't take down whatever
    /// the caller was actually doing (#735). The caller is responsible for reporting the failure.</summary>
    public static bool Write(string path, JObject data)
    {
        try
        {
            var dir = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, data.ToString());
            return true;
        }
        catch
        {
            return false;
        }
    }

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
