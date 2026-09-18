namespace OpenTabletArtist.Domain;

/// <summary>
/// Whose daemon OTA is connected to (#742).
///
/// Provenance is three-valued, not two: ours, theirs, or unread. The distinction matters because
/// "not theirs" covers both "ours" and "we couldn't tell", and those two want opposite behaviour —
/// the code already said so at the Stop/Restart confirmation, but every other guard was still written
/// as <c>!IsForeignDaemon</c> and so treated an unidentifiable daemon as if OTA owned it.
///
/// The rule: anything that <b>writes</b> to, or automatically modifies, the daemon's settings requires
/// <see cref="Owned"/>. Asking "do we know it is ours" is not the same as "do we know it is theirs",
/// and only the first is safe to act on.
/// </summary>
public enum DaemonOwnership
{
    /// <summary>Not connected, or the process behind the pipe couldn't be identified — an elevated
    /// daemon, one running as another user, or (off Windows) more than one to choose between. OTA knows
    /// least here, so it must assume least.</summary>
    Unknown,

    /// <summary>OTA built this daemon. The only state in which it may rewrite settings unasked.</summary>
    Owned,

    /// <summary>Positively identified as someone else's — including an OTD the user installed themselves
    /// and OTA merely launched. Adoption is supported, but an adopted install is still the user's.</summary>
    External,
}
