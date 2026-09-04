using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// Read-only view of the OTD plugins installed in the daemon's plugin directory and whether each is
/// active (referenced by an enabled output mode or filter in some profile). The daemon exposes no
/// "list plugins" RPC, so we enumerate the plugin directory and cross-reference the settings.
/// </summary>
public partial class PluginsViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceData _deviceData;
    private readonly ISettingsCoordinator _settings;

    [ObservableProperty] private List<PluginInfo> _plugins = [];
    [ObservableProperty] private string _emptyMessage = "No plugins found.";

    public bool HasPlugins => Plugins.Count > 0;
    partial void OnPluginsChanged(List<PluginInfo> value) => OnPropertyChanged(nameof(HasPlugins));

    /// <summary>The Windows Ink section, shown beside the plugin list — it manages a plugin, so it belongs
    /// with them rather than under DRIVERS with the VMulti driver (#wininK-to-plugins).
    ///
    /// Null off Windows. Nothing inside WindowsInkView guards the OS itself: it relied entirely on the
    /// DRIVERS pivot being filtered out off-Windows, and PLUGINS is not. The composition root decides, so
    /// this stays a pure container and the OS check stays in one place.</summary>
    public WindowsInkViewModel? WindowsInk { get; }

    public bool HasWindowsInk => WindowsInk != null;

    public PluginsViewModel(IDeviceData deviceData, ISettingsCoordinator settings,
                            WindowsInkViewModel? windowsInk = null)
    {
        WindowsInk = windowsInk;
        _deviceData = deviceData;
        _settings = settings;
        _deviceData.DataLoaded += Refresh;
        Refresh();
    }

    /// <summary>Open the daemon's plugin folder in File Explorer.</summary>
    [RelayCommand]
    private void Browse()
    {
        var dir = _deviceData.PluginDirectory;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        Services.PlatformShell.RevealInFileManager(dir);
    }

    [RelayCommand]
    private void Refresh()
    {
        var dir = _deviceData.PluginDirectory;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            EmptyMessage = "Plugin directory not available (is the daemon connected?).";
            Plugins = [];
            return;
        }

        var enabledPaths = EnabledPluginPaths().ToList();
        var list = new List<PluginInfo>();
        try
        {
            foreach (var folder in Directory.EnumerateDirectories(dir).OrderBy(Path.GetFileName))
            {
                var dlls = SafeEnumerateDlls(folder);
                bool active = dlls
                    .Select(Path.GetFileNameWithoutExtension)
                    .Any(baseName => enabledPaths.Any(p => PluginInventory.PathBelongsToAssembly(baseName!, p)));
                var name = Path.GetFileName(folder);
                // The manifest first: PluginVersion is the RELEASE version the plugin's author publishes,
                // and the number every other surface quotes — the Windows Ink section beside this list
                // reads the same file. An assembly version is a different thing that often never moves
                // between releases (Windows Ink 0.5.2 still stamps its DLL 0.4.2.0).
                list.Add(new PluginInfo(name, ManifestVersion(folder) ?? Version(PluginInventory.PluginDll(name, dlls)), active));
            }
        }
        catch { /* best-effort listing */ }

        EmptyMessage = "No plugins installed.";
        Plugins = list;
    }

    private IEnumerable<string> EnabledPluginPaths()
    {
        var settings = _settings.CurrentSettings;
        if (settings?.Profiles == null) yield break;
        foreach (var profile in settings.Profiles)
        {
            if (profile.OutputMode is { Enable: true, Path: { } omPath })
                yield return omPath;
            if (profile.Filters != null)
                foreach (var f in profile.Filters)
                    if (f is { Enable: true, Path: { } fPath })
                        yield return fPath;
        }
    }

    private static IReadOnlyList<string> SafeEnumerateDlls(string folder)
    {
        try { return Directory.EnumerateFiles(folder, "*.dll").ToList(); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>The PluginVersion from a plugin folder's metadata.json, or null when there is no manifest
    /// or it cannot be read. Parsed loosely rather than through OTD's PluginMetadata so a hand-made or
    /// partial manifest still yields a version.</summary>
    private static string? ManifestVersion(string folder)
    {
        var path = Path.Combine(folder, "metadata.json");
        if (!File.Exists(path)) return null;
        try
        {
            var version = JObject.Parse(File.ReadAllText(path))["PluginVersion"]?.ToString();
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch { return null; }
    }

    private static string Version(string? dll)
    {
        if (string.IsNullOrEmpty(dll)) return "";
        try { return AssemblyName.GetAssemblyName(dll).Version?.ToString() ?? ""; }
        catch { return ""; }
    }

    public void Dispose() => _deviceData.DataLoaded -= Refresh;
}
