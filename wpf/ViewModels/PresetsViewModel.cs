using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletDriver.Desktop;
using OtdWindowsHelper.Domain;
using OtdWindowsHelper.Helpers;
using OtdWindowsHelper.Services;

namespace OtdWindowsHelper.ViewModels;

/// <summary>
/// View model for the Saved Settings (snapshots) page. Page-VM split (#14 phase 2).
/// Unlike the fully-isolated pages, this one is coupled to the shell's current settings
/// and apply path, so those are injected as delegates rather than reaching back into the
/// shell: <paramref name="getCurrentSettings"/> reads the live settings to snapshot, and
/// <paramref name="applySettings"/> applies a loaded snapshot (push to daemon + persist).
/// The shell sets <see cref="PresetDirectory"/> and calls <see cref="LoadAsync"/> once the
/// daemon's app info is available.
/// </summary>
public partial class PresetsViewModel : ObservableObject
{
    private readonly ISettingsFileStore _store;
    private readonly Func<Settings?> _getCurrentSettings;
    private readonly Func<Settings, Task> _applySettings;

    [ObservableProperty] private List<PresetInfo> _presets = [];
    [ObservableProperty] private string _presetDirectory = "";

    public bool HasPresets => Presets.Count > 0;
    partial void OnPresetsChanged(List<PresetInfo> value) => OnPropertyChanged(nameof(HasPresets));

    public PresetsViewModel(
        ISettingsFileStore store,
        Func<Settings?> getCurrentSettings,
        Func<Settings, Task> applySettings)
    {
        _store = store;
        _getCurrentSettings = getCurrentSettings;
        _applySettings = applySettings;
    }

    /// <summary>Rescans the preset directory, newest first. Called by the shell after connect.</summary>
    public async Task LoadAsync()
    {
        if (string.IsNullOrEmpty(PresetDirectory) || !Directory.Exists(PresetDirectory))
        {
            Presets = [];
            return;
        }

        var presetList = new List<PresetInfo>();
        // Sort newest first by file last-write time so the most recent
        // snapshot appears at the top of the list.
        var files = Directory.GetFiles(PresetDirectory, "*.json")
            .OrderByDescending(File.GetLastWriteTime);
        foreach (var file in files)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file);
                var lastWrite = File.GetLastWriteTime(file);
                presetList.Add(new PresetInfo(
                    Name: Path.GetFileNameWithoutExtension(file),
                    Path: file,
                    Content: content,
                    LastModified: lastWrite.ToString("yyyy-MM-dd HH:mm:ss")));
            }
            catch { }
        }
        Presets = presetList;
    }

    [RelayCommand]
    private void OpenFolder(string path)
    {
        if (Directory.Exists(path))
            Process.Start("explorer.exe", path);
    }

    [RelayCommand]
    private async Task SavePreset()
    {
        var settings = _getCurrentSettings();
        if (settings == null) return;
        if (!Directory.Exists(PresetDirectory)) Directory.CreateDirectory(PresetDirectory);

        // Pick the lowest "Snapshot N" name not already taken (lowest gap is reused).
        // Date/time is shown separately on each card from the file's last-write time.
        var existing = Directory.EnumerateFiles(PresetDirectory, "*.json")
                                .Select(Path.GetFileNameWithoutExtension)
                                .Where(s => s != null)!
                                .Cast<string>();
        var name = PresetNaming.NextSnapshotName(existing);

        var path = Path.Combine(PresetDirectory, $"{name}.json");
        _store.Save(settings, path);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task LoadPreset(string name)
    {
        var path = Path.Combine(PresetDirectory, $"{name}.json");
        if (_store.TryLoad(path, out var settings) && settings != null)
        {
            await _applySettings(settings);
        }
    }

    [RelayCommand]
    private async Task UpdatePreset(string name)
    {
        var settings = _getCurrentSettings();
        if (settings == null) return;
        var path = Path.Combine(PresetDirectory, $"{name}.json");
        _store.Save(settings, path);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task RenamePreset(string name)
    {
        var oldPath = Path.Combine(PresetDirectory, $"{name}.json");
        if (!File.Exists(oldPath)) return;

        var newName = await Dialogs.ShowInputAsync(
            "Rename Snapshot",
            "Enter a new name for this snapshot:",
            name);

        if (!string.IsNullOrWhiteSpace(newName) && newName != name)
        {
            var newPath = Path.Combine(PresetDirectory, $"{newName}.json");
            if (!File.Exists(newPath))
            {
                File.Move(oldPath, newPath);
                await LoadAsync();
            }
            else
            {
                await Dialogs.ShowMessageAsync("Rename",
                    $"A snapshot named \"{newName}\" already exists.");
            }
        }
    }

    [RelayCommand]
    private async Task DeletePreset(string name)
    {
        var path = Path.Combine(PresetDirectory, $"{name}.json");
        if (!File.Exists(path)) return;

        var confirmed = await Dialogs.ShowConfirmAsync(
            "Delete Snapshot",
            $"Delete the snapshot \"{name}\"?\n\nThis cannot be undone.");

        if (!confirmed) return;

        File.Delete(path);
        await LoadAsync();
    }
}
