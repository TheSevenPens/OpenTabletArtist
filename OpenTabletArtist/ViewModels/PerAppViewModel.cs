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

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private List<string> _snapshotNames = [];
    // Fallback for unmapped apps: revert to the live "Current settings" (true), or a specific profile.
    [ObservableProperty] private bool _fallbackToCurrent = true;
    [ObservableProperty] private string? _fallbackProfile;
    [ObservableProperty] private List<PerAppMappingRow> _mappings = [];
    [ObservableProperty] private string _statusLine = "Off.";

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

        _switcher.ActiveProfileChanged += OnActiveChanged;
        _switcher.DanglingSnapshot += OnDangling;
        _device.DataLoaded += OnDataLoaded;
        _connection.PropertyChanged += OnConnectionChanged;

        // Reflect persisted config.
        _enabled = _store.Config.Enabled;
    }

    private void OnConnectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(IConnectionState.IsForeignDaemon) or nameof(IConnectionState.IsConnected)))
            return;
        OnPropertyChanged(nameof(IsForeignDaemon));
        OnPropertyChanged(nameof(CanUse));
        // A foreign daemon connecting while enabled: turn the feature off (which restores the default).
        if (IsForeignDaemon && Enabled) Enabled = false;
    }

    private void OnDataLoaded() => _ = LoadSafelyAsync();
    private async Task LoadSafelyAsync()
    {
        try { await LoadAsync(); }
        catch (Exception ex)
        {
            StatusLine = $"Couldn't refresh per-app settings: {ex.Message}";
        }
    }

    /// <summary>Rescan snapshots and rebuild the pickers/mappings from the store.</summary>
    public Task LoadAsync()
    {
        _suppress = true;
        try
        {
            SnapshotNames = SnapshotNamesFromDisk();
            // Reflect the stored fallback: null default → "Current settings"; a name → that profile.
            var def = _store.Config.DefaultSnapshot;
            FallbackToCurrent = def is null || !SnapshotNames.Contains(def);
            FallbackProfile = FallbackToCurrent ? SnapshotNames.FirstOrDefault() : def;
            RebuildMappings();
            OnPropertyChanged(nameof(HasEnoughSnapshots));
        }
        finally { _suppress = false; }

        // Resume a persisted-enabled config once the daemon is up (LoadAsync runs on data load), unless a
        // foreign daemon is connected (feature is app-owned-only).
        if (Enabled && !IsForeignDaemon && !_switcher.IsRunning)
        {
            _switcher.Start();
            StatusLine = "On — waiting for an app change…";
        }
        return Task.CompletedTask;
    }

    private void RebuildMappings()
    {
        Mappings = _store.Config.Mappings
            .Select(m => new PerAppMappingRow(m, SnapshotNames, OnRowSnapshotChanged, OnRowRemove))
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

    partial void OnEnabledChanged(bool value)
    {
        if (_suppress) return;
        if (value && IsForeignDaemon) { _enabled = false; OnPropertyChanged(nameof(Enabled)); return; }
        _store.SetEnabled(value);
        if (value) _switcher.Start();
        else _ = _switcher.StopAsync();
        StatusLine = value ? "On — waiting for an app change…" : "Off.";
    }

    partial void OnFallbackToCurrentChanged(bool value)
    {
        if (_suppress) return;
        // "Current settings" → no stored default; else the chosen profile (default the picker if empty).
        _store.SetDefaultSnapshot(value ? null : FallbackProfile ?? SnapshotNames.FirstOrDefault());
    }

    partial void OnFallbackProfileChanged(string? value)
    {
        if (_suppress) return;
        if (!FallbackToCurrent && value != null) _store.SetDefaultSnapshot(value);
    }

    private void OnRowSnapshotChanged(PerAppMappingRow row)
    {
        if (_suppress) return;
        _store.Upsert(new PerAppMapping(row.ExePath, row.ExeName, row.SelectedSnapshot ?? "", row.Enabled));
    }

    private void OnRowRemove(PerAppMappingRow row)
    {
        _store.Remove(row.ExeName);
        RebuildMappings();
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
    }

    private void OnActiveChanged(string? snapshot) =>
        StatusLine = snapshot == null ? "Active: Current settings" : $"Active: {snapshot}";

    private async void OnDangling(string snapshot) =>
        await _dialogs.ShowMessageAsync("Missing profile",
            $"A mapping points at \"{snapshot}\", which no longer exists — using your default instead. " +
            "Re-assign that app on this page.");

    public void Dispose()
    {
        _switcher.ActiveProfileChanged -= OnActiveChanged;
        _switcher.DanglingSnapshot -= OnDangling;
        _device.DataLoaded -= OnDataLoaded;
        _connection.PropertyChanged -= OnConnectionChanged;
    }
}

/// <summary>One app→snapshot row on the Per-App page. Its snapshot dropdown writes back to the store via
/// the supplied callback; a missing snapshot is flagged for a warning badge.</summary>
public partial class PerAppMappingRow : ObservableObject
{
    private readonly Action<PerAppMappingRow> _onChanged;
    private readonly Action<PerAppMappingRow> _onRemove;

    public string ExeName { get; }
    public string ExePath { get; }
    public bool Enabled { get; }
    public IReadOnlyList<string> SnapshotOptions { get; }

    [ObservableProperty] private string? _selectedSnapshot;

    /// <summary>The mapped snapshot no longer exists in the snapshot list.</summary>
    public bool IsMissing => !string.IsNullOrEmpty(SelectedSnapshot) && !SnapshotOptions.Contains(SelectedSnapshot);

    public string PathDisplay => string.IsNullOrEmpty(ExePath) ? ExeName : ExePath;

    public PerAppMappingRow(PerAppMapping mapping, IReadOnlyList<string> snapshotOptions,
        Action<PerAppMappingRow> onChanged, Action<PerAppMappingRow> onRemove)
    {
        ExeName = mapping.ExeName;
        ExePath = mapping.ExePath;
        Enabled = mapping.Enabled;
        SnapshotOptions = snapshotOptions;
        _selectedSnapshot = mapping.SnapshotName;
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
