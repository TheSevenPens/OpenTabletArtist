using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Configurations;

namespace OpenTabletArtist.Services;

/// <summary>
/// Inspects tablet configurations to distinguish OTD's built-in <b>base</b> configs from user
/// <b>override</b> files in the daemon's config folder (#467/#480). The base set is the configs embedded
/// in the bundled daemon (<see cref="DeviceConfigurationProvider"/>); an override is a loose file whose
/// config <c>Name</c> matches a base config — the daemon then loads the file instead of the vetted
/// default. See <c>docs/design/tablet-configs.md</c>.
/// </summary>
public static class TabletConfigInspector
{
    // The base config Name set is fixed for a given build (embedded resources), so compute it once.
    private static IReadOnlyCollection<string>? _baseNames;

    /// <summary>Names of every tablet config built into the bundled daemon (the vetted base set).</summary>
    public static IReadOnlyCollection<string> BaseConfigNames => _baseNames ??= LoadBaseNames();

    private static IReadOnlyCollection<string> LoadBaseNames()
    {
        try
        {
            return new DeviceConfigurationProvider().TabletConfigurations
                .Select(c => c.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>The base-config names that a loose file in <paramref name="configDir"/> overrides — i.e.
    /// files whose config <c>Name</c> matches a built-in. Empty when the folder is unknown/absent or on
    /// any read error (best-effort; a warning must never break health evaluation).</summary>
    public static IReadOnlySet<string> OverriddenBaseNames(string? configDir)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(configDir) || !Directory.Exists(configDir)) return result;

        var baseNames = BaseConfigNames;
        if (baseNames.Count == 0) return result;

        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(configDir, "*.json", SearchOption.AllDirectories); }
        catch { return result; }

        foreach (var file in files)
        {
            var name = TryReadConfigName(file);
            if (name != null && baseNames.Contains(name)) result.Add(name);
        }
        return result;
    }

    /// <summary>Read a config file's <c>Name</c> property, or null if unreadable / not a config.</summary>
    public static string? TryReadConfigName(string file)
    {
        try
        {
            var name = (string?)JObject.Parse(File.ReadAllText(file))["Name"];
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch { return null; }
    }

    // --- Path-key matching (for the #480 browse-time diff, which can't afford to download every config) ---

    private static IReadOnlySet<string>? _baseKeys;

    /// <summary>A normalised key per base config, derived from its embedded resource path — used to tell
    /// whether a repo config path is already covered by the bundled daemon without downloading it.</summary>
    public static IReadOnlySet<string> BaseConfigKeys => _baseKeys ??= LoadBaseKeys();

    private static IReadOnlySet<string> LoadBaseKeys()
    {
        try
        {
            return typeof(DeviceConfigurationProvider).Assembly.GetManifestResourceNames()
                .Where(n => n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .Select(PathKey)
                .ToHashSet();
        }
        catch { return new HashSet<string>(); }
    }

    /// <summary>Normalise a config path (repo path or embedded resource name) to a comparison key: drop the
    /// <c>.json</c> extension, lower-case, and strip every non-alphanumeric character. This collapses the
    /// differences between a repo path (<c>…/Wacom/Intuos Pro/PTH-660.json</c>) and the mangled embedded
    /// resource name (<c>….Wacom.Intuos_Pro.PTH-660.json</c>) so the shared prefix and the leaf both line up.</summary>
    public static string PathKey(string pathOrResource)
    {
        var s = pathOrResource;
        if (s.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            s = s[..^5];
        return Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]", "");
    }
}
