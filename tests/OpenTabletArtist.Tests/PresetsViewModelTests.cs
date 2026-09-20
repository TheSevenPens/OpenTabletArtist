using System;
using System.IO;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;
using OtdInterop;
using OtdInterop.Tests;

namespace OpenTabletArtist.Tests;

public class PresetsViewModelTests
{
    private sealed class FakeProfileHotkeys : IProfileHotkeys
    {
        public HotkeyChord? GetChord(string snapshot) => null;
        public HotkeySetResult SetHotkey(string snapshot, HotkeyChord chord) => HotkeySetResult.Ok;
        public void ClearHotkey(string snapshot) { }
        public void Sync(System.Collections.Generic.IEnumerable<string> snapshotNames) { }
        public void RenameSnapshot(string oldName, string newName) { }
    }

    private static ProfileSwitchService NewSwitch() =>
        new(new FakeSettingsCoordinator(), new PresetStore(NullOtdLog.Instance), () => "");

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
            var vm = new PresetsViewModel(new PresetStore(NullOtdLog.Instance), new FakeSettingsCoordinator(), new FakeDeviceData(), new FakeDialogService(), new FakeProfileHotkeys(), NewSwitch())
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
            var vm = new PresetsViewModel(new PresetStore(NullOtdLog.Instance), new FakeSettingsCoordinator { CurrentSettings = new Settings() }, new FakeDeviceData(), new FakeDialogService(), new FakeProfileHotkeys(), NewSwitch())
            { PresetDirectory = dir };

            await vm.SavePresetCommand.ExecuteAsync(null);

            Assert.True(vm.HasPresets);
            Assert.Contains(vm.Presets, p => p.Name == "Preset");
            Assert.True(File.Exists(Path.Combine(dir, "Preset.json")));
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
            var vm = new PresetsViewModel(new PresetStore(NullOtdLog.Instance), coordinator, new FakeDeviceData(), new FakeDialogService(), new FakeProfileHotkeys(), NewSwitch()) { PresetDirectory = dir };

            await vm.SavePresetCommand.ExecuteAsync(null);   // writes Preset.json
            await vm.LoadPresetCommand.ExecuteAsync("Preset");

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
            var vm = new PresetsViewModel(new PresetStore(NullOtdLog.Instance), new FakeSettingsCoordinator(), new FakeDeviceData(), new FakeDialogService(), new FakeProfileHotkeys(), NewSwitch())
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
        var vm = new PresetsViewModel(new PresetStore(NullOtdLog.Instance), new FakeSettingsCoordinator(), device, new FakeDialogService(), new FakeProfileHotkeys(), NewSwitch());
        Assert.Equal("", vm.PresetDirectory);

        device.RaiseDataLoaded(); // handler sets PresetDirectory synchronously, then rescans (fire-and-forget)

        Assert.Equal(@"C:\some\presets", vm.PresetDirectory);
    }
}
