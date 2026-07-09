using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OpenTabletArtist.Services;

/// <summary>One tablet config available from OpenTabletDriver's repo but not yet in the user's install.</summary>
public sealed record ApprovedConfig(string Path, string FileName, string Manufacturer, string DisplayName);

/// <summary>
/// Fetches "approved" tablet configs from OpenTabletDriver's GitHub repo and installs the ones the user's
/// bundled daemon doesn't already have (#480). The repo branch is pinned to the bundled daemon's branch
/// (<see cref="Branch"/> = the OTD submodule version), so a fetched config won't reference a report parser
/// the daemon lacks. Browse-time diffing is path-based (one tree request, no per-file downloads); install
/// re-checks the downloaded config's <c>Name</c> against the base set authoritatively. See
/// <c>docs/design/tablet-configs.md</c>.
/// </summary>
public sealed class ApprovedConfigsService
{
    /// <summary>The OTD branch to fetch from — matches the bundled daemon (submodule v0.6.7 on 0.6.x).</summary>
    public const string Branch = "0.6.x";

    private const string ConfigPrefix = "OpenTabletDriver.Configurations/Configurations/";
    private const string TreeUrl =
        "https://api.github.com/repos/OpenTabletDriver/OpenTabletDriver/git/trees/" + Branch + "?recursive=1";
    private const string RawBase =
        "https://raw.githubusercontent.com/OpenTabletDriver/OpenTabletDriver/" + Branch + "/";

    // Injectable so tests can supply canned responses without touching the network.
    private readonly Func<string, Task<string>> _getString;

    public ApprovedConfigsService(Func<string, Task<string>>? getString = null)
        => _getString = getString ?? DefaultGetAsync;

    /// <summary>Configs available from the repo that aren't already covered by the bundled base set or a
    /// file already in <paramref name="installedDir"/>. Throws on a network/parse failure (the caller shows
    /// a status).</summary>
    public async Task<IReadOnlyList<ApprovedConfig>> ListAvailableAsync(string? installedDir)
    {
        var paths = ParseConfigPaths(await _getString(TreeUrl));
        var installed = InstalledFileNames(installedDir);

        return paths
            .Where(p => !TabletConfigInspector.BaseConfigKeys.Contains(TabletConfigInspector.PathKey(p)))
            .Where(p => !installed.Contains(Path.GetFileName(p)))
            .Select(ToApprovedConfig)
            .OrderBy(c => c.Manufacturer, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Download and install one config into <paramref name="targetDir"/>. Returns null on success,
    /// else a short reason. Authoritatively skips a config whose <c>Name</c> is already in the base set
    /// (closes any browse-time false "new").</summary>
    public async Task<string?> InstallAsync(ApprovedConfig config, string targetDir)
    {
        if (string.IsNullOrEmpty(targetDir)) return "The configuration folder isn't known yet.";

        string raw;
        try { raw = await _getString(RawBase + config.Path); }
        catch (Exception ex) { return $"Download failed: {ex.Message}"; }

        string? name;
        try { name = (string?)JObject.Parse(raw)["Name"]; }
        catch { return "That file isn't a valid tablet configuration."; }
        if (string.IsNullOrWhiteSpace(name)) return "That file isn't a valid tablet configuration.";

        if (TabletConfigInspector.BaseConfigNames.Contains(name))
            return $"\"{name}\" is already supported by your OpenTabletDriver.";

        try
        {
            Directory.CreateDirectory(targetDir);
            await File.WriteAllTextAsync(Path.Combine(targetDir, config.FileName), raw);
        }
        catch (Exception ex) { return $"Couldn't save the config: {ex.Message}"; }

        return null;
    }

    /// <summary>Extract the config file paths (under the Configurations folder) from a git-trees response.</summary>
    public static IReadOnlyList<string> ParseConfigPaths(string treeJson)
    {
        var tree = JObject.Parse(treeJson)["tree"] as JArray;
        if (tree == null) return Array.Empty<string>();
        return tree
            .Where(n => (string?)n["type"] == "blob")
            .Select(n => (string?)n["path"] ?? "")
            .Where(p => p.StartsWith(ConfigPrefix, StringComparison.Ordinal)
                        && p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Turn a repo path into a display record (manufacturer = first folder under Configurations).</summary>
    public static ApprovedConfig ToApprovedConfig(string path)
    {
        var rel = path.Substring(ConfigPrefix.Length);              // e.g. "Wacom/Intuos Pro/PTH-660.json"
        var parts = rel.Split('/');
        var manufacturer = parts.Length > 1 ? parts[0] : "Other";
        var fileName = Path.GetFileName(path);
        var model = Path.GetFileNameWithoutExtension(fileName);
        var display = model.StartsWith(manufacturer, StringComparison.OrdinalIgnoreCase)
            ? model
            : $"{manufacturer} {model}";
        return new ApprovedConfig(path, fileName, manufacturer, display);
    }

    private static HashSet<string> InstalledFileNames(string? dir)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return set;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
                set.Add(Path.GetFileName(f));
        }
        catch { }
        return set;
    }

    // GitHub's API requires a User-Agent; raw.githubusercontent doesn't, but the same client is fine.
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OpenTabletArtist");
        return http;
    }

    private static async Task<string> DefaultGetAsync(string url)
    {
        using var resp = await Http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync();
    }
}
