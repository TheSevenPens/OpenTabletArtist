using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public PluginsViewModel(IDeviceData deviceData, ISettingsCoordinator settings)
    {
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
                list.Add(new PluginInfo(Path.GetFileName(folder), Version(dlls.FirstOrDefault()), active));
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

    private static string Version(string? dll)
    {
        if (string.IsNullOrEmpty(dll)) return "";
        try { return AssemblyName.GetAssemblyName(dll).Version?.ToString() ?? ""; }
        catch { return ""; }
    }

    public void Dispose() => _deviceData.DataLoaded -= Refresh;
}
