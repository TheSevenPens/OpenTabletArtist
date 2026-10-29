namespace OtdHealth;

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
