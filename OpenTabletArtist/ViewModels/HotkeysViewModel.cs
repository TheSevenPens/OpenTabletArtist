using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// The Hotkeys page (#89): the single home for every global keyboard shortcut. Manages the app-wide
/// "cycle mapped monitor" hotkey and the per-snapshot profile-switch hotkeys (moved here from the Saved
/// Settings page so all hotkey assignment lives in one place). Reconciles registrations on data load so
/// persisted hotkeys work without the page being opened, and rescans when shown so a snapshot saved on
/// the Saved Settings page appears here.
/// </summary>
public partial class HotkeysViewModel : ObservableObject, IDisposable
{
    private readonly IProfileHotkeys _profiles;
    private readonly IMonitorCycleHotkey _monitor;
    private readonly IDialogService _dialogs;
    private readonly IDeviceData _device;

    [ObservableProperty] private string _monitorHotkeyDisplay = "";
    [ObservableProperty] private List<ProfileHotkeyRow> _snapshots = [];

    public bool HasMonitorHotkey => !string.IsNullOrEmpty(MonitorHotkeyDisplay);
    partial void OnMonitorHotkeyDisplayChanged(string value) => OnPropertyChanged(nameof(HasMonitorHotkey));

    public bool HasSnapshots => Snapshots.Count > 0;
    partial void OnSnapshotsChanged(List<ProfileHotkeyRow> value) => OnPropertyChanged(nameof(HasSnapshots));

    /// <summary>Directory holding the snapshot files; supplied by the session on data load.</summary>
    public string PresetDirectory { get; private set; } = "";

    public HotkeysViewModel(IProfileHotkeys profiles, IMonitorCycleHotkey monitor,
        IDialogService dialogs, IDeviceData device)
    {
        _profiles = profiles;
        _monitor = monitor;
        _dialogs = dialogs;
        _device = device;
        _device.DataLoaded += OnDataLoaded;
    }

    private void OnDataLoaded() => _ = LoadSafelyAsync();

    private async Task LoadSafelyAsync()
    {
        try { await LoadAsync(); }
        catch { /* a hotkey/snapshot refresh failure must not surface */ }
    }

    /// <summary>Rescan snapshots + refresh both hotkey displays, and reconcile registrations.</summary>
    public async Task LoadAsync()
    {
        PresetDirectory = _device.PresetDirectory;
        MonitorHotkeyDisplay = _monitor.GetChord()?.Display ?? "";

        var names = SnapshotNames();
        var rows = new List<ProfileHotkeyRow>();
        foreach (var name in names)
            rows.Add(new ProfileHotkeyRow(name, _profiles.GetChord(name)?.Display ?? ""));
        Snapshots = rows;

        // Reconcile registrations with the current snapshot set (registers persisted chords, drops
        // mappings for snapshots that no longer exist). This is the one owner of that reconcile now.
        _profiles.Sync(names);
        await Task.CompletedTask;
    }

    private List<string> SnapshotNames()
    {
        if (string.IsNullOrEmpty(PresetDirectory) || !Directory.Exists(PresetDirectory))
            return [];
        return Directory.GetFiles(PresetDirectory, "*.json")
            .OrderByDescending(File.GetLastWriteTime)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Cast<string>()
            .ToList();
    }

    // ── Monitor-cycle hotkey ─────────────────────────────────────────────────────
    [RelayCommand]
    private async Task AssignMonitorHotkey()
    {
        var chord = await _dialogs.ShowHotkeyCaptureAsync(_monitor.GetChord());
        if (chord == null) return;
        await ReportResult(_monitor.SetHotkey(chord), chord);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ClearMonitorHotkey()
    {
        _monitor.ClearHotkey();
        await LoadAsync();
    }

    // ── Per-snapshot profile-switch hotkeys ──────────────────────────────────────
    [RelayCommand]
    private async Task AssignProfileHotkey(string name)
    {
        var chord = await _dialogs.ShowHotkeyCaptureAsync(_profiles.GetChord(name));
        if (chord == null) return;
        await ReportResult(_profiles.SetHotkey(name, chord), chord);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ClearProfileHotkey(string name)
    {
        _profiles.ClearHotkey(name);
        await LoadAsync();
    }

    private async Task ReportResult(HotkeySetResult result, HotkeyChord chord)
    {
        switch (result)
        {
            case HotkeySetResult.Conflict:
                await _dialogs.ShowMessageAsync("Hotkey in use",
                    $"\"{chord.Display}\" is already registered by another application. Pick a different combination.");
                break;
            case HotkeySetResult.Invalid:
                await _dialogs.ShowMessageAsync("Hotkey not usable",
                    "Use a modifier (Ctrl / Alt / Shift / Win) plus a letter, digit, or F-key.");
                break;
        }
    }

    public void Dispose() => _device.DataLoaded -= OnDataLoaded;
}

/// <summary>One snapshot row on the Hotkeys page: its name and the assigned chord's display (empty when
/// unassigned).</summary>
public record ProfileHotkeyRow(string Name, string HotkeyDisplay)
{
    public bool HasHotkey => !string.IsNullOrEmpty(HotkeyDisplay);
}
