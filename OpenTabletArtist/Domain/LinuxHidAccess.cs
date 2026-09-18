namespace OpenTabletArtist.Domain;

/// <summary>
/// Whether this process can open the HID device nodes a tablet arrives on (#779). Linux only; every other
/// platform reports <see cref="Ok"/>.
///
/// Domain rather than a service type because the health catalog reads it and the health catalog is pure —
/// same split as <see cref="SettingsLoadStatus"/> and <c>SettingsFile</c>. <c>LinuxInputEnvironment</c>
/// is what actually looks.
/// </summary>
public enum LinuxHidAccess
{
    /// <summary>Openable, or nothing present to open — nothing to report.</summary>
    Ok,
    /// <summary>Present but not openable, and the user is not in the <c>input</c> group.</summary>
    Blocked,
    /// <summary>Not openable yet, but the user <em>is</em> in <c>input</c> on disk — the grant exists and
    /// simply isn't active in this session. A much less alarming state than <see cref="Blocked"/>, and
    /// telling them apart is the difference between "reboot" and "run this command you already ran".</summary>
    PendingReboot,
}
