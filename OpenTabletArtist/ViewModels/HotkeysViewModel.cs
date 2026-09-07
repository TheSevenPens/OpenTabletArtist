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

    // One list, two groups (#hotkeys-one-list). Both hold the SAME row type, so the tablet action and a
    // preset render through one DataTemplate and read as two entries in one list of shortcuts rather
    // than as two differently-shaped halves of a page.
    [ObservableProperty] private List<HotkeyRowViewModel> _actions = [];
    [ObservableProperty] private List<HotkeyRowViewModel> _snapshots = [];

    public bool HasMonitorHotkey => !string.IsNullOrEmpty(MonitorHotkeyDisplay);
    partial void OnMonitorHotkeyDisplayChanged(string value) => OnPropertyChanged(nameof(HasMonitorHotkey));

    public bool HasSnapshots => Snapshots.Count > 0;
    partial void OnSnapshotsChanged(List<HotkeyRowViewModel> value) => OnPropertyChanged(nameof(HasSnapshots));

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

        Actions =
        [
            new HotkeyRowViewModel(
                "Move to next display",
                "Moves the active tablet's area to the next monitor, wrapping around.",
                MonitorHotkeyDisplay,
                () => AssignMonitorHotkeyCommand.ExecuteAsync(null),
                () => ClearMonitorHotkeyCommand.ExecuteAsync(null)),
        ];

        var snapshots = SnapshotFiles();
        var rows = new List<HotkeyRowViewModel>();
        foreach (var (name, saved) in snapshots)
            rows.Add(new HotkeyRowViewModel(
                name,
                $"Saved {saved:yyyy-MM-dd}",
                _profiles.GetChord(name)?.Display ?? "",
                () => AssignProfileHotkeyCommand.ExecuteAsync(name),
                () => ClearProfileHotkeyCommand.ExecuteAsync(name)));
        Snapshots = rows;

        var names = snapshots.Select(s => s.Name).ToList();

        // Reconcile registrations with the current snapshot set (registers persisted chords, drops
        // mappings for snapshots that no longer exist). This is the one owner of that reconcile now.
        _profiles.Sync(names);
        await Task.CompletedTask;
    }

    /// <summary>The presets on disk, newest first, each with the time it was saved (shown as the row's
    /// detail line so two similarly-named presets can be told apart).</summary>
    private List<(string Name, DateTime Saved)> SnapshotFiles()
    {
        if (string.IsNullOrEmpty(PresetDirectory) || !Directory.Exists(PresetDirectory))
            return [];
        return Directory.GetFiles(PresetDirectory, "*.json")
            .OrderByDescending(File.GetLastWriteTime)
            .Select(f => (Name: Path.GetFileNameWithoutExtension(f), Saved: File.GetLastWriteTime(f)))
            .Where(t => !string.IsNullOrEmpty(t.Name))
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

/// <summary>
/// One row on the Hotkeys page — a thing you can bind a shortcut to, whether that is a tablet action or
/// a preset. It carries its OWN commands rather than taking them from the page: a row's "…" contents
/// live in a flyout, which is a popup outside the row's visual tree, so
/// <c>$parent[ItemsControl].DataContext</c> does not resolve from in there. A flyout does inherit its
/// target's DataContext, so plain <c>{Binding AssignCommand}</c> reaches these. (The wheel rows on the
/// tablet page are bound the same way.)
/// </summary>
public partial class HotkeyRowViewModel : ObservableObject
{
    private readonly Func<Task> _assign;
    private readonly Func<Task> _clear;

    public HotkeyRowViewModel(string title, string detail, string chordDisplay,
        Func<Task> assign, Func<Task> clear)
    {
        Title = title;
        Detail = detail;
        ChordDisplay = chordDisplay;
        _assign = assign;
        _clear = clear;
    }

    /// <summary>What the shortcut does — an action's name, or a preset's.</summary>
    public string Title { get; }

    /// <summary>The quiet line under it: what the action does, or when the preset was saved.</summary>
    public string Detail { get; }

    /// <summary>The assigned chord, or empty when nothing is bound.</summary>
    public string ChordDisplay { get; }

    public bool HasHotkey => !string.IsNullOrEmpty(ChordDisplay);

    [RelayCommand]
    private Task Assign() => _assign();

    /// <summary>Greyed out when there is nothing to clear, as CLEAR is on a wheel row.</summary>
    [RelayCommand(CanExecute = nameof(HasHotkey))]
    private Task Clear() => _clear();
}
