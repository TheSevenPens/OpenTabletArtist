namespace OtdInterop;

/// <summary>Comparing filesystem paths for identity.</summary>
public static class SettingsPath
{
    /// <summary>
    /// True when both paths name the same file.
    ///
    /// Compared after full-path resolution, so a relative path and an absolute one to the same file
    /// match, and case-insensitively, which is right for Windows and wrong for a case-sensitive
    /// filesystem — accepted deliberately, because the failure it guards is a settings write landing in
    /// the wrong daemon's file, and a false "same" there is far worse than a false "different".
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
