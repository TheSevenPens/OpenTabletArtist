using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// Per-App Profiles page (#167): automatically applies a saved snapshot when the foreground app changes.
/// Owns the enable toggle, the default profile, and the app→snapshot mapping list; drives the headless
/// <see cref="PerAppSwitcher"/> and persists via <see cref="PerAppProfileStore"/>. Switches are ephemeral
/// (the daemon runs the per-app snapshot; the editor keeps your default). Rescans snapshots on data load
/// and when the page is shown.
/// </summary>
public partial class PerAppViewModel : ObservableObject, IDisposable
{
    private readonly PerAppSwitcher _switcher;
    private readonly PerAppProfileStore _store;
    private readonly IDeviceData _device;
    private readonly IDialogService _dialogs;
    private readonly IConnectionState _connection;
    private bool _suppress;   // guards store writes while we repopulate the UI from the store

    /// <summary>Dropdown sentinel: an app (or the unmapped fallback) uses the live Current settings
    /// rather than a saved profile. Maps to a null snapshot in the store.</summary>
    public const string CurrentSettingsOption = "Current settings";

    [ObservableProperty] private List<string> _snapshotNames = [];
    // Target dropdown options shared by the "Default for apps" card and every app card: Current settings
    // (the live config) followed by the saved profiles.
    [ObservableProperty] private List<string> _targetOptions = [CurrentSettingsOption];
    // What unmapped apps use (the "Default for apps" card). CurrentSettingsOption ⇔ a null stored default.
    [ObservableProperty] private string _unmappedTarget = CurrentSettingsOption;
    [ObservableProperty] private List<PerAppMappingRow> _mappings = [];

    /// <summary>Dropdown selection → stored snapshot name (Current settings ⇒ null).</summary>
    internal static string? TargetToSnapshot(string? target) =>
        string.IsNullOrEmpty(target) || target == CurrentSettingsOption ? null : target;

    public bool HasEnoughSnapshots => SnapshotNames.Count >= 1;
    public bool HasMappings => Mappings.Count > 0;

    /// <summary>Per-app switching writes to whatever daemon is connected; a foreign daemon's snapshot
    /// paths/filter stores may not match, so the feature is app-owned-daemon only (#167), same pattern as
    /// plugin install. When true, the controls are disabled and a banner explains why.</summary>
    public bool IsForeignDaemon => _connection.IsForeignDaemon;
    public bool CanUse => !IsForeignDaemon;

    public PerAppViewModel(PerAppSwitcher switcher, PerAppProfileStore store, IDeviceData device,
        IDialogService dialogs, IConnectionState connection)
    {
        _switcher = switcher;
        _store = store;
        _device = device;
        _dialogs = dialogs;
        _connection = connection;

        _switcher.DanglingSnapshot += OnDangling;
        _device.DataLoaded += OnDataLoaded;
        _connection.PropertyChanged += OnConnectionChanged;
    }

    private void OnConnectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(IConnectionState.IsForeignDaemon) or nameof(IConnectionState.IsConnected)))
            return;
        OnPropertyChanged(nameof(IsForeignDaemon));
        OnPropertyChanged(nameof(CanUse));
        // A foreign daemon suspends switching (its files may not match); it resumes when ours returns.
        UpdateSwitcher();
    }

    /// <summary>Start or stop the headless switcher to match the implicit-enable rule: run whenever a
    /// mapping targets a real profile and we're on the app-owned daemon. Idempotent — safe to call on
    /// every mapping edit, data load, and connection change.</summary>
    private void UpdateSwitcher()
    {
        bool shouldRun = !IsForeignDaemon && _store.HasActiveMappings;
        if (shouldRun && !_switcher.IsRunning) _switcher.Start();
        else if (!shouldRun && _switcher.IsRunning) _ = _switcher.StopAsync();
    }

    private void OnDataLoaded() => _ = LoadSafelyAsync();
    private async Task LoadSafelyAsync()
    {
        try { await LoadAsync(); }
        catch (Exception ex)
        {
            // Fire-and-forget from DataLoaded — swallow so a bad reload can't crash, but don't lose it.
            System.Diagnostics.Debug.WriteLine($"Per-app settings refresh failed: {ex}");
        }
    }

    /// <summary>Rescan snapshots and rebuild the pickers/mappings from the store.</summary>
    public Task LoadAsync()
    {
        _suppress = true;
        try
        {
            SnapshotNames = SnapshotNamesFromDisk();
            var options = new List<string> { CurrentSettingsOption };
            options.AddRange(SnapshotNames);
            TargetOptions = options;
            // Reflect the stored fallback: null (or a dangling name) → Current settings; a name → profile.
            var def = _store.Config.DefaultSnapshot;
            UnmappedTarget = def != null && SnapshotNames.Contains(def) ? def : CurrentSettingsOption;
            RebuildMappings();
            OnPropertyChanged(nameof(HasEnoughSnapshots));
        }
        finally { _suppress = false; }

        // Bring the switcher in line with the current mappings once the daemon is up (LoadAsync runs on
        // data load): running iff a mapping targets a real profile and the daemon is app-owned.
        UpdateSwitcher();
        return Task.CompletedTask;
    }

    private void RebuildMappings()
    {
        Mappings = _store.Config.Mappings
            .Select(m => new PerAppMappingRow(m, TargetOptions, OnRowSnapshotChanged, OnRowRemove))
            .ToList();
        OnPropertyChanged(nameof(HasMappings));
    }

    private List<string> SnapshotNamesFromDisk()
    {
        var dir = _device.PresetDirectory;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, "*.json")
            .OrderByDescending(File.GetLastWriteTime)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n)).Cast<string>().ToList();
    }

    partial void OnUnmappedTargetChanged(string value)
    {
        if (_suppress) return;
        _store.SetDefaultSnapshot(TargetToSnapshot(value));
    }

    private void OnRowSnapshotChanged(PerAppMappingRow row)
    {
        if (_suppress) return;
        _store.Upsert(new PerAppMapping(row.ExePath, row.ExeName, TargetToSnapshot(row.SelectedSnapshot), row.Enabled));
        UpdateSwitcher(); // switching to/from Current settings can arm or disarm the feature
    }

    private void OnRowRemove(PerAppMappingRow row)
    {
        _store.Remove(row.ExeName);
        RebuildMappings();
        UpdateSwitcher(); // removing the last profile-mapped app disarms and restores the default
    }

    [RelayCommand]
    private async Task AddApp()
    {
        var app = await _dialogs.ShowProcessPickerAsync();
        if (app == null) return;
        var snapshot = SnapshotNames.FirstOrDefault();
        if (snapshot == null)
        {
            await _dialogs.ShowMessageAsync("No profiles",
                "Save at least one profile on the Profiles page first, then map apps to it.");
            return;
        }
        _store.Upsert(new PerAppMapping(app.ExePath, app.ExeName, snapshot));
        RebuildMappings();
        UpdateSwitcher(); // the first profile-mapped app arms the feature
    }

    private async void OnDangling(string snapshot) =>
        await _dialogs.ShowMessageAsync("Missing profile",
            $"A mapping points at \"{snapshot}\", which no longer exists — using your default instead. " +
            "Re-assign that app on this page.");

    public void Dispose()
    {
        _switcher.DanglingSnapshot -= OnDangling;
        _device.DataLoaded -= OnDataLoaded;
        _connection.PropertyChanged -= OnConnectionChanged;
    }
}

/// <summary>One app row on the Per-App page. Its target dropdown (Current settings + saved profiles)
/// writes back to the store via the supplied callback; a target pointing at a deleted profile is flagged
/// for a warning badge.</summary>
public partial class PerAppMappingRow : ObservableObject
{
    private readonly Action<PerAppMappingRow> _onChanged;
    private readonly Action<PerAppMappingRow> _onRemove;

    public string ExeName { get; }
    public string ExePath { get; }
    public bool Enabled { get; }
    public IReadOnlyList<string> SnapshotOptions { get; }

    [ObservableProperty] private string? _selectedSnapshot;

    /// <summary>The mapped target is a saved profile that no longer exists (Current settings is never missing).</summary>
    public bool IsMissing => SelectedSnapshot != PerAppViewModel.CurrentSettingsOption
        && !string.IsNullOrEmpty(SelectedSnapshot) && !SnapshotOptions.Contains(SelectedSnapshot);

    public string PathDisplay => string.IsNullOrEmpty(ExePath) ? ExeName : ExePath;

    public PerAppMappingRow(PerAppMapping mapping, IReadOnlyList<string> snapshotOptions,
        Action<PerAppMappingRow> onChanged, Action<PerAppMappingRow> onRemove)
    {
        ExeName = mapping.ExeName;
        ExePath = mapping.ExePath;
        Enabled = mapping.Enabled;
        SnapshotOptions = snapshotOptions;
        _selectedSnapshot = mapping.SnapshotName ?? PerAppViewModel.CurrentSettingsOption;
        _onChanged = onChanged;
        _onRemove = onRemove;
    }

    partial void OnSelectedSnapshotChanged(string? value)
    {
        OnPropertyChanged(nameof(IsMissing));
        _onChanged(this);
    }

    [RelayCommand]
    private void Remove() => _onRemove(this);
}
