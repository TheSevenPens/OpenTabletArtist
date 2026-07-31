namespace OpenTabletArtist.Domain;

/// <summary>How reading the app's settings file resolved on startup (#21).</summary>
public enum SettingsLoadStatus
{
    /// <summary>Loaded cleanly, or no settings file existed yet — nothing to report.</summary>
    Ok,
    /// <summary>The file couldn't be read/parsed, but it was moved aside to a backup first, so the user's
    /// data is recoverable and the next save can't overwrite it.</summary>
    Preserved,
    /// <summary>The file couldn't be read/parsed <em>and</em> couldn't be moved to a backup, so the original
    /// is still at its normal path and a later save may overwrite it. The more dangerous case.</summary>
    NotPreserved,
}

/// <summary>The outcome of loading the settings file: a status plus, when the unreadable file was preserved,
/// the <see cref="BackupName"/> it was moved to (so the UI can name it truthfully rather than claiming a
/// backup exists when it doesn't). Pure data so both <c>AppSettings</c> and the health check share it (#21).</summary>
public sealed record SettingsLoadOutcome(SettingsLoadStatus Status, string? BackupName)
{
    public static readonly SettingsLoadOutcome Ok = new(SettingsLoadStatus.Ok, null);

    /// <summary>True when the settings file existed but couldn't be read (either preserved or not).</summary>
    public bool Unreadable => Status is SettingsLoadStatus.Preserved or SettingsLoadStatus.NotPreserved;
}
