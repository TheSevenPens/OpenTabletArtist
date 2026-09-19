namespace OtdInterop;

/// <summary>Comparing filesystem paths for identity.</summary>
public static class SettingsPath
{
    /// <summary>
    /// True when both paths name the same file.
    ///
    /// Compared after full-path resolution, so a relative path and an absolute one to the same file
    /// match. The comparison is case-insensitive, which is correct on Windows — the supported platform,
    /// and the behaviour this preserves.
    ///
    /// <b>That is an assumption, not a safety margin.</b> On a case-sensitive filesystem two paths
    /// differing only in case name different files, and this would call them the same. Its caller treats
    /// "same" as permission to write, so a false "same" is the dangerous direction: it would allow a
    /// write to a file that was not the intended one. Recorded as a limitation to revisit if another
    /// platform becomes supported, rather than defended as a trade-off.
    ///
    /// It compares paths, not files. Two paths that reach one file by different routes — a symlink, a
    /// junction, a mapped drive — are not recognised as the same.
    ///
    /// Null or empty is never the same as anything, including another null: nowhere is not a place.
    /// </summary>
    /// <param name="a">One path.</param>
    /// <param name="b">The other.</param>
    public static bool Same(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // An unusable path is not the same as anything; the caller treats that as "different".
            return false;
        }
    }
}
