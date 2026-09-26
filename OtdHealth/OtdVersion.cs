namespace OtdHealth;

/// <summary>Release comparison used by diagnostics and their consumers.</summary>
public static class OtdVersion
{
    /// <summary>True if two version strings denote the same release, comparing numeric major.minor.patch and
    /// ignoring a 4th component or any suffix — the daemon binary reports e.g. "0.6.7" while OTA's bundled
    /// assembly version is "0.6.7.0". False if either can't be parsed to at least a major version.</summary>
    public static bool SameRelease(string a, string b)
    {
        var pa = ParseTriple(a);
        var pb = ParseTriple(b);
        return pa is not null && pb is not null && pa.Value == pb.Value;
    }

    // Parse the leading numeric major.minor.patch; minor/patch default to 0. Null if there's no major.
    private static (int major, int minor, int patch)? ParseTriple(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var parts = version.Trim().Split('.');
        if (!TryLeadingInt(parts.ElementAtOrDefault(0), out var major)) return null;
        TryLeadingInt(parts.ElementAtOrDefault(1), out var minor);
        TryLeadingInt(parts.ElementAtOrDefault(2), out var patch);
        return (major, minor, patch);
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
