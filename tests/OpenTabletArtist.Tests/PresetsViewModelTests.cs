using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            var vm = new PresetsViewModel(new PresetStore(NullOtdLog.Instance), coordinator, new FakeDeviceData(), new FakeDialogService(), new FakeProfileHotkeys(), new ProfileSwitchService(coordinator, new PresetStore(NullOtdLog.Instance), () => dir)) { PresetDirectory = dir };

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

    private sealed class RecordingHotkeys : IProfileHotkeys
    {
        public List<(string Old, string New)> Renames { get; } = new();
        public HotkeyChord? GetChord(string snapshot) => null;
        public HotkeySetResult SetHotkey(string snapshot, HotkeyChord chord) => HotkeySetResult.Ok;
        public void ClearHotkey(string snapshot) { }
        public void Sync(System.Collections.Generic.IEnumerable<string> snapshotNames) { }
        public void RenameSnapshot(string oldName, string newName) => Renames.Add((oldName, newName));
    }

    private static (PresetsViewModel Vm, FakeDialogService Dialogs, RecordingHotkeys Hotkeys) RenameSetup(string dir, string typed)
    {
        File.WriteAllText(Path.Combine(dir, "Preset.json"), "{}");
        var dialogs = new FakeDialogService { InputResult = typed };
        var hotkeys = new RecordingHotkeys();
        var vm = new PresetsViewModel(new PresetStore(NullOtdLog.Instance), new FakeSettingsCoordinator(), new FakeDeviceData(), dialogs, hotkeys, NewSwitch())
        { PresetDirectory = dir };
        return (vm, dialogs, hotkeys);
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("..\\outside")]
    [InlineData("../outside")]
    [InlineData("what?")]
    [InlineData("CON")]
    [InlineData("ends with a dot.")]
    public async Task Rename_ToANameThatIsNotOneFileName_SaysSo_AndLeavesThePresetAlone(string typed)
    {
        // The preset folder sits inside a folder of the test's own, so a rename that escapes it lands where the
        // test can see it (and clean it up) rather than in the shared temp folder.
        var parent = TempDir();
        var dir = Directory.CreateDirectory(Path.Combine(parent, "presets")).FullName;
        try
        {
            var (vm, dialogs, hotkeys) = RenameSetup(dir, typed);

            await vm.RenamePresetCommand.ExecuteAsync("Preset");

            Assert.True(File.Exists(Path.Combine(dir, "Preset.json")));
            Assert.Single(Directory.GetFiles(dir));
            Assert.Empty(Directory.GetFiles(parent));
            Assert.Single(dialogs.Messages);
            Assert.Empty(hotkeys.Renames);
        }
        finally { Directory.Delete(parent, true); }
    }

    [Fact]
    public async Task Rename_TrimsTheName()
    {
        var dir = TempDir();
        try
        {
            var (vm, _, hotkeys) = RenameSetup(dir, "  Inking  ");

            await vm.RenamePresetCommand.ExecuteAsync("Preset");

            Assert.True(File.Exists(Path.Combine(dir, "Inking.json")));
            Assert.Equal(new[] { ("Preset", "Inking") }, hotkeys.Renames);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Rename_ChangingOnlyTheCase_IsNotRefusedAsTaken()
    {
        var dir = TempDir();
        try
        {
            var (vm, dialogs, hotkeys) = RenameSetup(dir, "PRESET");

            await vm.RenamePresetCommand.ExecuteAsync("Preset");

            Assert.Empty(dialogs.Messages);
            Assert.Equal(new[] { "PRESET.json" }, Directory.GetFiles(dir).Select(Path.GetFileName).ToArray());
            Assert.Equal(new[] { ("Preset", "PRESET") }, hotkeys.Renames);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Rename_OntoAnotherPreset_IsRefused()
    {
        var dir = TempDir();
        try
        {
            var (vm, dialogs, hotkeys) = RenameSetup(dir, "Other");
            File.WriteAllText(Path.Combine(dir, "Other.json"), "{\"x\":1}");

            await vm.RenamePresetCommand.ExecuteAsync("Preset");

            Assert.Equal("{\"x\":1}", File.ReadAllText(Path.Combine(dir, "Other.json")));
            Assert.True(File.Exists(Path.Combine(dir, "Preset.json")));
            Assert.Single(dialogs.Messages);
            Assert.Empty(hotkeys.Renames);
        }
        finally { Directory.Delete(dir, true); }
    }
}
