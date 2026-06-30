namespace OpenTabletArtist.Domain;

/// <summary>
/// Pure naming rules for snapshot presets. Extracted from <c>MainViewModel.SavePreset</c>
/// so the collision/reuse behavior can be unit-tested without the filesystem.
/// </summary>
public static class PresetNaming
{
    /// <summary>The first snapshot is unnumbered; subsequent ones are "Snapshot 2", "Snapshot 3", ...</summary>
    public const string BaseName = "Snapshot";

    /// <summary>
    /// Returns the lowest available snapshot name not already present in
    /// <paramref name="existingNames"/>. "Snapshot" first, then "Snapshot 2", "Snapshot 3", ...
    /// The lowest gap is reused (e.g. if "Snapshot" and "Snapshot 3" exist but "Snapshot 2"
    /// is free, returns "Snapshot 2"). Comparison is case-insensitive.
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
