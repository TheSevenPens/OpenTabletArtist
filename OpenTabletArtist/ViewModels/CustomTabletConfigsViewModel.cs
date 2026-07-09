using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for the Custom Tablet Configs page — lists/views/deletes the tablet config
/// JSON files in OpenTabletDriver's Configurations folder. Page-VM split (#14 phase 2):
/// self-contained (filesystem only, no daemon), so it scans the folder in its constructor.
/// The folder location comes from <see cref="IConfigurationsDirectoryProvider"/> (so tests can
/// point at a temp dir); view/delete confirmations go through <see cref="IDialogService"/> (#37).
/// </summary>
public partial class CustomTabletConfigsViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly IConfigurationsDirectoryProvider _directory;
    private readonly ApprovedConfigsService _approved;

    [ObservableProperty] private string _configurationsDirectory = "";
    [ObservableProperty] private List<ConfigurationItem> _configurations = [];
    [ObservableProperty] private bool _hasConfigurations;

    // Approved configs available from OpenTabletDriver's repo but not yet installed (#480).
    [ObservableProperty] private bool _isFetchingAvailable;
    [ObservableProperty] private List<ApprovedConfig> _availableConfigs = [];
    [ObservableProperty] private bool _hasAvailableConfigs;
    [ObservableProperty] private string _availableStatus = "";

    public CustomTabletConfigsViewModel(IDialogService dialogs, IConfigurationsDirectoryProvider directory,
        ApprovedConfigsService? approved = null)
    {
        _dialogs = dialogs;
        _directory = directory;
        _approved = approved ?? new ApprovedConfigsService();
        LoadConfigurations();
    }

    private void LoadConfigurations()
    {
        // Re-resolve each load: the daemon's real folder may not have been known at construction time
        // (it arrives with AppInfo once the daemon connects), so Refresh picks it up (#480/#467 groundwork).
        ConfigurationsDirectory = _directory.GetOrCreate();
        var dir = ConfigurationsDirectory;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            Configurations = [];
            HasConfigurations = false;
            return;
        }

        var items = new List<ConfigurationItem>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories)
                                       .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var info = new FileInfo(file);

                // Friendly name derived from the JSON contents, with filename/folder fallbacks.
                string? raw = null;
                try { raw = File.ReadAllText(file); } catch { }
                string displayName = TabletConfigNaming.FriendlyName(file, raw);

                items.Add(new ConfigurationItem(
                    displayName,
                    Path.GetFileName(file),
                    file,
                    $"{info.Length:N0} bytes"));
            }
            catch { }
        }
        Configurations = items;
        HasConfigurations = items.Count > 0;
    }

    [RelayCommand]
    private void RefreshConfigurations() => LoadConfigurations();

    [RelayCommand]
    private void OpenConfigurationsFolder()
    {
        if (!string.IsNullOrEmpty(ConfigurationsDirectory) && Directory.Exists(ConfigurationsDirectory))
            Process.Start("explorer.exe", ConfigurationsDirectory);
    }

    [RelayCommand]
    private async Task ViewConfiguration(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        string content;
        try
        {
            var raw = await File.ReadAllTextAsync(path);
            try { content = JToken.Parse(raw).ToString(Newtonsoft.Json.Formatting.Indented); }
            catch { content = raw; }
        }
        catch (Exception ex) { content = $"Failed to read file:\n{ex.Message}"; }

        await _dialogs.ShowTextViewerAsync(Path.GetFileName(path), content);
    }

    [RelayCommand]
    private async Task DeleteConfiguration(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        var confirmed = await _dialogs.ShowConfirmAsync(
            "Delete Configuration",
            $"Delete \"{Path.GetFileName(path)}\"?\n\nThis cannot be undone.");
        if (!confirmed) return;
        try { File.Delete(path); } catch { }
        LoadConfigurations();
    }

    // --- Approved configs from OpenTabletDriver's repo (#480) ---

    /// <summary>Fetch the list of approved configs the bundled daemon doesn't already have.</summary>
    [RelayCommand]
    private async Task FetchAvailable()
    {
        IsFetchingAvailable = true;
        AvailableStatus = "Checking OpenTabletDriver for more tablet configs…";
        try
        {
            var available = await _approved.ListAvailableAsync(ConfigurationsDirectory);
            AvailableConfigs = available.ToList();
            HasAvailableConfigs = AvailableConfigs.Count > 0;
            AvailableStatus = AvailableConfigs.Count > 0
                ? $"{AvailableConfigs.Count} additional tablet config(s) available to install."
                : "Your OpenTabletDriver already has every approved tablet config.";
        }
        catch (Exception ex)
        {
            HasAvailableConfigs = false;
            AvailableConfigs = [];
            AvailableStatus = $"Couldn't reach OpenTabletDriver's config list: {ex.Message}";
        }
        finally { IsFetchingAvailable = false; }
    }

    /// <summary>Install one approved config into the daemon's folder, then refresh both lists.</summary>
    [RelayCommand]
    private async Task InstallApproved(ApprovedConfig? config)
    {
        if (config is null) return;
        var error = await _approved.InstallAsync(config, ConfigurationsDirectory);
        if (error != null)
        {
            AvailableStatus = error;
            return;
        }
        // Drop it from the available list and re-scan the installed folder.
        AvailableConfigs = AvailableConfigs.Where(c => c.Path != config.Path).ToList();
        HasAvailableConfigs = AvailableConfigs.Count > 0;
        AvailableStatus = $"Installed {config.DisplayName}. Reconnect the tablet (or restart the daemon) to use it.";
        LoadConfigurations();
    }
}
