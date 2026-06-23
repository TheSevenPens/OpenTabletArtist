using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using OtdWindowsHelper.Domain;
using OtdWindowsHelper.Services;

namespace OtdWindowsHelper.ViewModels;

/// <summary>
/// View model for the Custom Tablet Configs page — lists/views/deletes the tablet config
/// JSON files in OpenTabletDriver's Configurations folder. Page-VM split (#14 phase 2):
/// self-contained (filesystem only, no daemon), so it scans the folder in its constructor.
/// View/delete confirmations go through <see cref="IDialogService"/> (#37).
/// </summary>
public partial class CustomTabletConfigsViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;

    [ObservableProperty] private string _configurationsDirectory = "";
    [ObservableProperty] private List<ConfigurationItem> _configurations = [];
    [ObservableProperty] private bool _hasConfigurations;

    public CustomTabletConfigsViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        InitializeConfigurationsFolder();
        LoadConfigurations();
    }

    private void InitializeConfigurationsFolder()
    {
        // OTD reads tablet configs from %AppData%\OpenTabletDriver\Configurations
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData)) { ConfigurationsDirectory = ""; return; }
        var configDir = Path.Combine(appData, "OpenTabletDriver", "Configurations");
        try
        {
            if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);
        }
        catch { }
        ConfigurationsDirectory = configDir;
    }

    private void LoadConfigurations()
    {
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
}
