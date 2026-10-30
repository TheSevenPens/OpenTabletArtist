using OtdHealth;

namespace OpenTabletArtist.Domain;

/// <summary>
/// What is said about a daemon whose version is not exactly the one this build ships with (#1042).
/// One place, because the same sentences appear in the health card, on the Daemon page, and in the
/// message a refused connection leaves behind, and they must not drift apart.
/// </summary>
internal static class DaemonVersionCopy
{
    /// <summary>What the artist can do about a refused daemon. The app never stops another process on its
    /// own: this names the button that does it, and the button asks first.</summary>
    public const string HowToFix =
        "Stop that daemon, then start the one OpenTabletArtist ships with.";

    /// <summary>Why the app is not attached, or null when it is free to attach.</summary>
    public static string? Refusal(DaemonVersionGate gate, string actual, string expected) => gate switch
    {
        DaemonVersionGate.Mismatch =>
            $"OpenTabletDriver {actual} is running, but this version of OpenTabletArtist needs {expected}.",
        DaemonVersionGate.Unreadable =>
            "OpenTabletArtist couldn't read the running daemon's version, so it can't tell whether it is the "
            + (string.IsNullOrEmpty(expected) ? "one it needs." : $"{expected} it needs."),
        _ => null,
    };

    /// <summary>The same release but another revision: it works, and the app says so. An older revision
    /// is said more strongly, since the newer one may carry a fix it lacks. Null for an exact match, and
    /// for the states that refuse rather than note.</summary>
    public static string? Note(DaemonVersionGate gate, string actual, string expected) => gate switch
    {
        DaemonVersionGate.OlderRevision =>
            $"This daemon is {actual}, older than the {expected} this version of OpenTabletArtist ships with. "
            + "It may be missing fixes.",
        DaemonVersionGate.NewerRevision =>
            $"This daemon is {actual}, newer than the {expected} this version of OpenTabletArtist ships with.",
        _ => null,
    };
}
