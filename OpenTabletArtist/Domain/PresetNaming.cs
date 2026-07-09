namespace OpenTabletArtist.Domain;

/// <summary>
/// Pure naming rules for saved profiles. Extracted from <c>MainViewModel.SavePreset</c>
/// so the collision/reuse behavior can be unit-tested without the filesystem.
/// </summary>
public static class PresetNaming
{
    /// <summary>The first profile is unnumbered; subsequent ones are "Profile 2", "Profile 3", ...</summary>
    public const string BaseName = "Preset";

    /// <summary>
    /// Returns the lowest available profile name not already present in
    /// <paramref name="existingNames"/>. "Profile" first, then "Profile 2", "Profile 3", ...
    /// The lowest gap is reused (e.g. if "Profile" and "Profile 3" exist but "Profile 2"
    /// is free, returns "Profile 2"). Comparison is case-insensitive.
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
