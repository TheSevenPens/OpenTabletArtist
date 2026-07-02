using System;
using System.IO;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

public class PresetsViewModelTests
{
    private sealed class FakeSettingsCoordinator : ISettingsCoordinator
    {
        public Settings? CurrentSettings { get; set; }
        public Settings? Applied { get; private set; }
        public Task ApplyAndSaveSettingsAsync(Settings settings) { Applied = settings; return Task.CompletedTask; }
        public Task ApplyLiveOnlyAsync(Settings settings) { Applied = settings; return Task.CompletedTask; }
        public Task ApplyEphemeralAsync(Settings settings) { Applied = settings; return Task.CompletedTask; }
        public Task RestoreDefaultAsync() => Task.CompletedTask;
    }

    private sealed class FakeProfileHotkeys : IProfileHotkeys
    {
        public HotkeyChord? GetChord(string snapshot) => null;
        public HotkeySetResult SetHotkey(string snapshot, HotkeyChord chord) => HotkeySetResult.Ok;
        public void ClearHotkey(string snapshot) { }
        public void Sync(System.Collections.Generic.IEnumerable<string> snapshotNames) { }
        public void RenameSnapshot(string oldName, string newName) { }
    }

    private static ProfileSwitchService NewSwitch() =>
        new(new FakeSettingsCoordinator(), new SettingsFileStore(), () => "");

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"otdpresets_{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public async Task LoadAsync_EmptyDirectory_HasNoPresets()
    {
        var dir = TempDir();
        try
        {
            var vm = new PresetsViewModel(new SettingsFileStore(), new FakeSettingsCoordinator(), new FakeDeviceData(), new FakeDialogService(), new FakeProfileHotkeys(), NewSwitch())
            { PresetDirectory = dir };

            await vm.LoadAsync();

            Assert.False(vm.HasPresets);
            Assert.Empty(vm.Presets);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Save_ThenLoad_ListsTheSnapshot()
    {
        var dir = TempDir();
        try
        {
            var vm = new PresetsViewModel(new SettingsFileStore(), new FakeSettingsCoordinator { CurrentSettings = new Settings() }, new FakeDeviceData(), new FakeDialogService(), new FakeProfileHotkeys(), NewSwitch())
            { PresetDirectory = dir };

            await vm.SavePresetCommand.ExecuteAsync(null);

            Assert.True(vm.HasPresets);
            Assert.Contains(vm.Presets, p => p.Name == "Snapshot");
            Assert.True(File.Exists(Path.Combine(dir, "Snapshot.json")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Load_AppliesTheSavedSettings()
    {
        var dir = TempDir();
        try
        {
            var coordinator = new FakeSettingsCoordinator { CurrentSettings = new Settings { LockUsableAreaTablet = true } };
            var vm = new PresetsViewModel(new SettingsFileStore(), coordinator, new FakeDeviceData(), new FakeDialogService(), new FakeProfileHotkeys(), NewSwitch()) { PresetDirectory = dir };

            await vm.SavePresetCommand.ExecuteAsync(null);   // writes Snapshot.json
            await vm.LoadPresetCommand.ExecuteAsync("Snapshot");

            Assert.NotNull(coordinator.Applied);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Save_WhenNoCurrentSettings_DoesNothing()
    {
        var dir = TempDir();
        try
        {
            var vm = new PresetsViewModel(new SettingsFileStore(), new FakeSettingsCoordinator(), new FakeDeviceData(), new FakeDialogService(), new FakeProfileHotkeys(), NewSwitch())
            { PresetDirectory = dir };

            await vm.SavePresetCommand.ExecuteAsync(null);

            Assert.False(vm.HasPresets);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DataLoaded_PicksUpDirectoryFromSession()
    {
        var device = new FakeDeviceData { PresetDirectory = @"C:\some\presets" };
        var vm = new PresetsViewModel(new SettingsFileStore(), new FakeSettingsCoordinator(), device, new FakeDialogService(), new FakeProfileHotkeys(), NewSwitch());
        Assert.Equal("", vm.PresetDirectory);

        device.RaiseDataLoaded(); // handler sets PresetDirectory synchronously, then rescans (fire-and-forget)

        Assert.Equal(@"C:\some\presets", vm.PresetDirectory);
    }
}
