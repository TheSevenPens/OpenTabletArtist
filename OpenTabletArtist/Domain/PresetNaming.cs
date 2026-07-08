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
}
