using Newtonsoft.Json.Linq;

namespace OpenTabletArtist.Domain;

/// <summary>
/// Pure friendly-name derivation for tablet configuration JSON files. Extracted from
/// <c>MainViewModel.LoadConfigurations</c> so the fallback chain can be unit-tested
/// without touching the filesystem. The caller reads the file; this only interprets it.
/// </summary>
public static class TabletConfigNaming
{
    // UTF-8 BOM, sometimes present at the start of config files.
    private const char Bom = '﻿';

    /// <summary>
    /// Derives a display name for a tablet config file, preferring (in order):
    /// the JSON <c>Name</c> field, then <c>Manufacturer</c>/<c>Vendor</c> + <c>Model</c>,
    /// then a "&lt;parent folder&gt; &lt;filename&gt;" combo (so files in manufacturer
    /// subfolders keep a vendor prefix), and finally the bare filename.
    /// </summary>
    /// <param name="filePath">Path to the config file (used for the filename/folder fallbacks).</param>
    /// <param name="jsonContent">Raw file contents, or null if it could not be read.</param>
    /// <remarks>
    /// If <paramref name="jsonContent"/> is null or fails to parse, the bare filename is
    /// returned (the parent-folder fallback only applies when the JSON parsed but had no
    /// usable name fields) — preserving the original inline behavior.
    /// </remarks>
    public static string FriendlyName(string filePath, string? jsonContent)
    {
        var stem = Path.GetFileNameWithoutExtension(filePath);
        var displayName = stem;

        if (!string.IsNullOrEmpty(jsonContent))
        {
            try
            {
                var token = JToken.Parse(jsonContent.TrimStart(Bom));
                var jsonName = token["Name"]?.ToString();
                var manufacturer = token["Manufacturer"]?.ToString() ?? token["Vendor"]?.ToString();
                var model = token["Model"]?.ToString();

                if (!string.IsNullOrWhiteSpace(jsonName))
                    displayName = jsonName!;
                else if (!string.IsNullOrWhiteSpace(manufacturer) && !string.IsNullOrWhiteSpace(model))
                    displayName = $"{manufacturer} {model}";
                else
                    displayName = ParentPrefixed(filePath, stem);
            }
            catch
            {
                // Keep the bare filename on parse failure.
            }
        }

        return displayName;
    }

    private static string ParentPrefixed(string filePath, string stem)
    {
        var parent = Path.GetFileName(Path.GetDirectoryName(filePath));
        if (!string.IsNullOrEmpty(parent) &&
            !string.Equals(parent, "Configurations", StringComparison.OrdinalIgnoreCase))
            return $"{parent} {stem}";
        return stem;
    }
}
