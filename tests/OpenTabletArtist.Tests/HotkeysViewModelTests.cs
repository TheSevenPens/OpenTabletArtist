using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Input;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

public class HotkeysViewModelTests
{
    private sealed class FakeMonitorHotkey : IMonitorCycleHotkey
    {
        public HotkeyChord? Chord;
        public HotkeyChord? GetChord() => Chord;
        public HotkeySetResult SetHotkey(HotkeyChord c) { Chord = c; return HotkeySetResult.Ok; }
        public void ClearHotkey() => Chord = null;
    }

    private sealed class FakeProfiles : IProfileHotkeys
    {
        public Dictionary<string, HotkeyChord> Map = new();
        public HotkeyChord? GetChord(string s) => Map.TryGetValue(s, out var c) ? c : null;
        public HotkeySetResult SetHotkey(string s, HotkeyChord c) { Map[s] = c; return HotkeySetResult.Ok; }
        public void ClearHotkey(string s) => Map.Remove(s);
        public void Sync(IEnumerable<string> names) { }
        public void RenameSnapshot(string o, string n) { }
    }

    private static string TempDirWith(params string[] snapshotNames)
    {
        var d = Path.Combine(Path.GetTempPath(), $"otahotkeys_{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        foreach (var n in snapshotNames) File.WriteAllText(Path.Combine(d, n + ".json"), "{}");
        return d;
    }

    private static HotkeysViewModel New(FakeProfiles profiles, FakeMonitorHotkey monitor,
        FakeDialogService dialogs, FakeDeviceData device)
        => new(profiles, monitor, dialogs, device);

    [Fact]
    public async Task LoadAsync_ListsSnapshotsAndMonitorHotkey()
    {
        var dir = TempDirWith("Alpha", "Beta");
        try
        {
            var monitor = new FakeMonitorHotkey { Chord = new HotkeyChord(KeyModifiers.Control, Key.M) };
            var vm = New(new FakeProfiles(), monitor, new FakeDialogService(),
                new FakeDeviceData { PresetDirectory = dir });

            await vm.LoadAsync();

            Assert.True(vm.HasMonitorHotkey);
            Assert.Equal(2, vm.Snapshots.Count);
            Assert.True(vm.HasSnapshots);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task AssignMonitorHotkey_SetsChordAndDisplay()
    {
        var monitor = new FakeMonitorHotkey();
        var dialogs = new FakeDialogService { HotkeyResult = new HotkeyChord(KeyModifiers.Alt, Key.F9) };
        var vm = New(new FakeProfiles(), monitor, dialogs, new FakeDeviceData());

        await vm.AssignMonitorHotkeyCommand.ExecuteAsync(null);

        Assert.NotNull(monitor.Chord);
        Assert.Equal(Key.F9, monitor.Chord!.Key);
        Assert.True(vm.HasMonitorHotkey);
    }

    [Fact]
    public async Task AssignProfileHotkey_BindsTheSnapshot()
    {
        var dir = TempDirWith("Portrait");
        try
        {
            var profiles = new FakeProfiles();
            var dialogs = new FakeDialogService { HotkeyResult = new HotkeyChord(KeyModifiers.Control, Key.D1) };
            var vm = New(profiles, new FakeMonitorHotkey(), dialogs,
                new FakeDeviceData { PresetDirectory = dir });
            await vm.LoadAsync();

            await vm.AssignProfileHotkeyCommand.ExecuteAsync("Portrait");

            Assert.True(profiles.Map.ContainsKey("Portrait"));
            Assert.Equal(Key.D1, profiles.Map["Portrait"].Key);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ClearMonitorHotkey_Removes()
    {
        var monitor = new FakeMonitorHotkey { Chord = new HotkeyChord(KeyModifiers.Alt, Key.F9) };
        var vm = New(new FakeProfiles(), monitor, new FakeDialogService(), new FakeDeviceData());
        await vm.LoadAsync();

        await vm.ClearMonitorHotkeyCommand.ExecuteAsync(null);

        Assert.Null(monitor.Chord);
        Assert.False(vm.HasMonitorHotkey);
    }
}
