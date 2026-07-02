using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// Experimental Per-App Profiles page (#167) — currently a TIMING SPIKE, not the full feature. Lets the
/// user pick two snapshots and enable a foreground watcher that toggles between them on every app switch,
/// showing the measured latency so we can decide (go/no-go) whether OTD's whole-Settings apply is fast
/// enough to switch on focus changes. If the spike passes, this page grows into the real mapping UI.
/// </summary>
public partial class PerAppViewModel : ObservableObject, IDisposable
{
    private readonly PerAppSpikeService _spike;
    private readonly IDeviceData _device;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private List<string> _snapshotNames = [];
    [ObservableProperty] private string? _selectedA;
    [ObservableProperty] private string? _selectedB;
    [ObservableProperty] private string _statusLine = "Not running.";

    public bool CanEnable => !string.IsNullOrEmpty(SelectedA) && !string.IsNullOrEmpty(SelectedB);
    public bool HasEnoughSnapshots => SnapshotNames.Count >= 2;

    public PerAppViewModel(PerAppSpikeService spike, IDeviceData device)
    {
        _spike = spike;
        _device = device;
        _spike.Measured += OnMeasured;
        _device.DataLoaded += OnDataLoaded;
    }

    private void OnDataLoaded() => _ = LoadSafelyAsync();

    private async Task LoadSafelyAsync()
    {
        try { await LoadAsync(); } catch { /* snapshot rescan failure must not surface */ }
    }

    public Task LoadAsync()
    {
        SnapshotNames = SnapshotNamesFromDisk();
        OnPropertyChanged(nameof(HasEnoughSnapshots));
        // Drop selections that no longer exist.
        if (SelectedA != null && !SnapshotNames.Contains(SelectedA)) SelectedA = null;
        if (SelectedB != null && !SnapshotNames.Contains(SelectedB)) SelectedB = null;
        return Task.CompletedTask;
    }

    private List<string> SnapshotNamesFromDisk()
    {
        var dir = _device.PresetDirectory;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, "*.json")
            .OrderByDescending(File.GetLastWriteTime)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Cast<string>()
            .ToList();
    }

    partial void OnSelectedAChanged(string? value) => Reconfigure();
    partial void OnSelectedBChanged(string? value) => Reconfigure();

    private void Reconfigure()
    {
        _spike.Configure(SelectedA ?? "", SelectedB ?? "");
        OnPropertyChanged(nameof(CanEnable));
        // Disabling the two pickers out from under an enabled run: turn it off cleanly.
        if (Enabled && !CanEnable) Enabled = false;
    }

    partial void OnEnabledChanged(bool value)
    {
        _spike.SetEnabled(value && CanEnable);
        StatusLine = value ? "Watching foreground app… switch apps to trigger a measurement." : "Not running.";
    }

    private void OnMeasured(string line) => StatusLine = line;

    public void Dispose()
    {
        _spike.Measured -= OnMeasured;
        _device.DataLoaded -= OnDataLoaded;
    }
}
