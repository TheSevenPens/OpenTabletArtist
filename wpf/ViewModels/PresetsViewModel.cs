using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletDriver.Desktop;
using OtdWindowsHelper.Domain;
using OtdWindowsHelper.Services;

namespace OtdWindowsHelper.ViewModels;

/// <summary>
/// View model for the Saved Settings (snapshots) page. Page-VM split (#14 phase 2).
/// Reads/applies live settings through the shared <see cref="ISettingsCoordinator"/> (Option C,
/// #41 PR 3). Picks up the preset directory and rescans by self-subscribing to the session's
/// <see cref="IDeviceData.DataLoaded"/> event rather than being pushed to by the shell.
/// </summary>
public partial class PresetsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsFileStore _store;
    private readonly ISettingsCoordinator _settings;
    private readonly IDeviceData _deviceData;
    private readonly IDialogService _dialogs;

    [ObservableProperty] private List<PresetInfo> _presets = [];
    [ObservableProperty] private string _presetDirectory = "";

    public bool HasPresets => Presets.Count > 0;
    partial void OnPresetsChanged(List<PresetInfo> value) => OnPropertyChanged(nameof(HasPresets));

    public PresetsViewModel(ISettingsFileStore store, ISettingsCoordinator settings, IDeviceData deviceData, IDialogService dialogs)
    {
        _store = store;
        _settings = settings;
        _deviceData = deviceData;
        _dialogs = dialogs;
        _deviceData.DataLoaded += OnDataLoaded;
    }

    private void OnDataLoaded()
    {
        PresetDirectory = _deviceData.PresetDirectory;
        // Fire-and-forget rescan; swallow so an enumeration/ordering failure can't surface as
        // an unobserved exception (the old shell wrapped LoadAsync the same way, Codex #43).
        _ = LoadSafelyAsync();
    }

    private async Task LoadSafelyAsync()
    {
        try { await LoadAsync(); }
        catch { /* preset refresh failure must not surface */ }
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
        var settings = _settings.CurrentSettings;
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
            await _settings.ApplyAndSaveSettingsAsync(settings);
        }
    }

    [RelayCommand]
    private async Task UpdatePreset(string name)
    {
        var settings = _settings.CurrentSettings;
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

        var newName = await _dialogs.ShowInputAsync(
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
                await _dialogs.ShowMessageAsync("Rename",
                    $"A snapshot named \"{newName}\" already exists.");
            }
        }
    }

    [RelayCommand]
    private async Task DeletePreset(string name)
    {
        var path = Path.Combine(PresetDirectory, $"{name}.json");
        if (!File.Exists(path)) return;

        var confirmed = await _dialogs.ShowConfirmAsync(
            "Delete Snapshot",
            $"Delete the snapshot \"{name}\"?\n\nThis cannot be undone.");

        if (!confirmed) return;

        File.Delete(path);
        await LoadAsync();
    }

    public void Dispose() => _deviceData.DataLoaded -= OnDataLoaded;
}
