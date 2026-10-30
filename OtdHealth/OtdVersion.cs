namespace OtdHealth;

/// <summary>
/// How a daemon's version stands against the release the app ships with (#1042).
/// </summary>
/// <remarks>
/// A daemon must be the same release as the bundled one, though not literally the same binary. The
/// first three numbers are the release; the fourth is a revision of it, and a revision is close enough
/// to work with but not close enough to say nothing about.
/// </remarks>
public enum DaemonVersionGate
{
    /// <summary>The same release, down to the revision.</summary>
    Match,
    /// <summary>The same release but a later revision (a 0.6.8.1 daemon, 0.6.8 expected). Works; noted.</summary>
    NewerRevision,
    /// <summary>The same release but an earlier revision (a 0.6.8 daemon, 0.6.8.1 expected). Works; noted
    /// more strongly, because the older one may lack a fix the newer carries.</summary>
    OlderRevision,
    /// <summary>The major, minor or patch number differs. The app does not attach.</summary>
    Mismatch,
    /// <summary>A version could not be read, so nothing can be said to match. The app does not attach.</summary>
    Unreadable,
}

/// <summary>Release comparison used by diagnostics and their consumers.</summary>
public static class OtdVersion
{
    /// <summary>True if two version strings denote the same release, comparing the numeric
    /// major.minor.patch.revision and ignoring any suffix. A missing part counts as 0, so the daemon binary
    /// reporting "0.6.7" matches OTA's bundled assembly version "0.6.7.0". The fourth part is a real
    /// difference: OpenTabletDriver's 0.6.8.1 is a Windows crash fix over 0.6.8, and a driver on the older
    /// one has to read as the older one. False if either can't be parsed to at least a major version.</summary>
    public static bool SameRelease(string a, string b)
    {
        var pa = ParseParts(a);
        var pb = ParseParts(b);
        return pa is not null && pb is not null && pa.Value == pb.Value;
    }

    /// <summary>Where <paramref name="daemonVersion"/> stands against <paramref name="expectedVersion"/>.
    /// Either one that cannot be parsed to at least a major number is <see cref="DaemonVersionGate.Unreadable"/>:
    /// refusing on a version nobody can read is the safe side of not knowing.</summary>
    public static DaemonVersionGate Gate(string daemonVersion, string expectedVersion)
    {
        var actual = ParseParts(daemonVersion);
        var expected = ParseParts(expectedVersion);
        if (actual is null || expected is null) return DaemonVersionGate.Unreadable;

        var (a, e) = (actual.Value, expected.Value);
        if (a.major != e.major || a.minor != e.minor || a.patch != e.patch) return DaemonVersionGate.Mismatch;
        if (a.revision == e.revision) return DaemonVersionGate.Match;
        return a.revision > e.revision ? DaemonVersionGate.NewerRevision : DaemonVersionGate.OlderRevision;
    }

    /// <summary>Whether the app refuses to attach to a daemon in this state.</summary>
    public static bool Blocks(DaemonVersionGate gate) =>
        gate is DaemonVersionGate.Mismatch or DaemonVersionGate.Unreadable;

    // Parse the leading numeric major.minor.patch.revision; every part after the major defaults to 0. Null if
    // there's no major.
    private static (int major, int minor, int patch, int revision)? ParseParts(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var parts = version.Trim().Split('.');
        if (!TryLeadingInt(parts.ElementAtOrDefault(0), out var major)) return null;
        TryLeadingInt(parts.ElementAtOrDefault(1), out var minor);
        TryLeadingInt(parts.ElementAtOrDefault(2), out var patch);
        TryLeadingInt(parts.ElementAtOrDefault(3), out var revision);
        return (major, minor, patch, revision);
    }

    private static bool TryLeadingInt(string? s, out int value)
    {
        value = 0;
        if (string.IsNullOrEmpty(s)) return false;
        int i = 0;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        return i > 0 && int.TryParse(s.AsSpan(0, i), out value);
    }
}
