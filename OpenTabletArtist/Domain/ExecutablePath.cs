using System;
namespace OpenTabletArtist.Domain;

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
    /// <summary>
    /// The macOS <c>.app</c> bundle a binary lives in, or the path unchanged when it isn't inside one.
    /// What identifies an install to a person is "OpenTabletDriver.app in Applications", not the four
    /// path segments below it that are the same for every copy.
    /// </summary>
    public static string BundleOrSelf(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var marker = path.IndexOf(".app/", StringComparison.OrdinalIgnoreCase);
        return marker >= 0 ? path[..(marker + 4)] : path;
    }

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
