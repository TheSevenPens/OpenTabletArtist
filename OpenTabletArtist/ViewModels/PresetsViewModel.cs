using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletDriver.Desktop;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

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
    private readonly IProfileHotkeys _hotkeys;
    private readonly ProfileSwitchService _profileSwitch;

    [ObservableProperty] private List<PresetInfo> _presets = [];
    [ObservableProperty] private string _presetDirectory = "";

    public bool HasPresets => Presets.Count > 0;
    partial void OnPresetsChanged(List<PresetInfo> value) => OnPropertyChanged(nameof(HasPresets));

    public PresetsViewModel(ISettingsFileStore store, ISettingsCoordinator settings, IDeviceData deviceData,
        IDialogService dialogs, IProfileHotkeys hotkeys, ProfileSwitchService profileSwitch)
    {
        _store = store;
        _settings = settings;
        _deviceData = deviceData;
        _dialogs = dialogs;
        _hotkeys = hotkeys;
        _profileSwitch = profileSwitch;
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
                var name = Path.GetFileNameWithoutExtension(file);
                presetList.Add(new PresetInfo(
                    Name: name,
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

        // Pick the lowest "Profile N" name not already taken (lowest gap is reused).
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

        // Guard: while a live-only override is active, CurrentSettings is the override, not your saved
        // default — updating would capture the wrong config. Make it a deliberate choice. (#320)
        if (_profileSwitch.HasOverride)
        {
            var proceed = await _dialogs.ShowConfirmAsync("Update Profile",
                $"A profile override (\"{_profileSwitch.ActiveSnapshot}\") is active, so this saves the " +
                "currently-overridden settings into this profile — not your saved default.\n\nContinue?");
            if (!proceed) return;
        }

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
            "Rename Profile",
            "Enter a new name for this profile:",
            name);

        if (!string.IsNullOrWhiteSpace(newName) && newName != name)
        {
            var newPath = Path.Combine(PresetDirectory, $"{newName}.json");
            if (!File.Exists(newPath))
            {
                File.Move(oldPath, newPath);
                _hotkeys.RenameSnapshot(name, newName); // carry the hotkey mapping across (#320)
                await LoadAsync();
            }
            else
            {
                await _dialogs.ShowMessageAsync("Rename",
                    $"A profile named \"{newName}\" already exists.");
            }
        }
    }

    [RelayCommand]
    private async Task DeletePreset(string name)
    {
        var path = Path.Combine(PresetDirectory, $"{name}.json");
        if (!File.Exists(path)) return;

        var confirmed = await _dialogs.ShowConfirmAsync(
            "Delete Profile",
            $"Delete the profile \"{name}\"?\n\nThis cannot be undone.");

        if (!confirmed) return;

        File.Delete(path);
        _hotkeys.ClearHotkey(name); // drop its hotkey mapping (#320)
        await LoadAsync();
    }

    public void Dispose() => _deviceData.DataLoaded -= OnDataLoaded;
}
