namespace OpenTabletArtist.Domain;

/// <summary>
/// Pure naming rules for saved profiles. Extracted from <c>MainViewModel.SavePreset</c>
/// so the collision/reuse behavior can be unit-tested without the filesystem.
/// </summary>
public static class PresetNaming
{
    /// <summary>The first profile is unnumbered; subsequent ones are "Preset 2", "Preset 3", ...</summary>
    public const string BaseName = "Preset";

    /// <summary>
    /// Returns the lowest available profile name not already present in
    /// <paramref name="existingNames"/>. "Preset" first, then "Preset 2", "Preset 3", ...
    /// The lowest gap is reused (e.g. if "Preset" and "Preset 3" exist but "Preset 2"
    /// is free, returns "Preset 2"). Comparison is case-insensitive.
    /// </summary>
    public static string NextSnapshotName(IEnumerable<string> existingNames)
    {
        var taken = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);

        if (!taken.Contains(BaseName))
            return BaseName;

        for (int n = 2; ; n++)
        {
            var candidate = $"{BaseName} {n}";
            if (!taken.Contains(candidate))
                return candidate;
        }
    }

    /// <summary>The characters Windows refuses in a file name. Fixed rather than read from
    /// <c>Path.GetInvalidFileNameChars</c>, which on Linux and macOS is only '/' and NUL: a profile named there is
    /// still a file that may be copied to Windows, and '\' would otherwise be a path on one and a name on the other.</summary>
    private static readonly char[] ForbiddenChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Why <paramref name="name"/> can't be a profile's name, or null if it can. A profile is a file in the
    /// preset folder, so the name has to be one file name: no separators (which would reach outside the
    /// folder), nothing Windows refuses, no reserved device name, and no trailing dot or space (which Windows
    /// quietly drops, so the file would not be called what was typed). Expects a trimmed name.
    /// </summary>
    public static string? InvalidNameReason(string name)
    {
        if (name.Length == 0) return "A profile needs a name.";
        if (name.IndexOfAny(ForbiddenChars) >= 0 || name.Any(char.IsControl))
            return "A profile name can't contain any of these characters: \\ / : * ? \" < > |";
        if (name.EndsWith('.') || name.EndsWith(' '))
            return "A profile name can't end with a dot.";
        if (ReservedNames.Contains(name.Split('.')[0].TrimEnd()))
            return $"\"{name}\" is a name Windows reserves for a device, so it can't be a profile's name.";
        return null;
    }

    /// <summary>
    /// Returns an available name for a duplicate of <paramref name="baseName"/>: "&lt;name&gt; copy",
    /// then "&lt;name&gt; copy 2", "&lt;name&gt; copy 3", ... — the lowest not present in
    /// <paramref name="existingNames"/>. Comparison is case-insensitive.
    /// </summary>
    public static string NextCopyName(string baseName, IEnumerable<string> existingNames)
    {
        var taken = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);

        var first = $"{baseName} copy";
        if (!taken.Contains(first))
            return first;

        for (int n = 2; ; n++)
        {
            var candidate = $"{baseName} copy {n}";
            if (!taken.Contains(candidate))
                return candidate;
        }
    }
}
