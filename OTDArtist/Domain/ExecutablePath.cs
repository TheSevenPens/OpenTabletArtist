namespace OtdArtist.Domain;

/// <summary>
/// Pure path comparison used to decide whether the daemon we connected to is this
/// project's build. Extracted from <c>MainViewModel.UpdateDaemonSource</c> so the
/// normalization/comparison can be unit-tested.
/// </summary>
public static class ExecutablePath
{
    /// <summary>
    /// True when both paths refer to the same executable. Paths are normalized with
    /// <see cref="Path.GetFullPath(string)"/> and compared case-insensitively (Windows).
    /// Returns false if either path is null/empty or cannot be normalized.
    /// </summary>
    public static bool SameFile(string? actual, string? expected)
    {
        if (string.IsNullOrEmpty(actual) || string.IsNullOrEmpty(expected))
            return false;
        try
        {
            return string.Equals(
                Path.GetFullPath(actual),
                Path.GetFullPath(expected),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
