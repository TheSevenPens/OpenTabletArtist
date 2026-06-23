using System;
using System.IO;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OtdWindowsHelper.Services;
using OtdWindowsHelper.ViewModels;
using Xunit;

namespace OtdWindowsHelper.Tests;

public class PresetsViewModelTests
{
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
            var vm = new PresetsViewModel(new SettingsFileStore(), () => null, _ => Task.CompletedTask)
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
            var vm = new PresetsViewModel(new SettingsFileStore(), () => new Settings(), _ => Task.CompletedTask)
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
            Settings? applied = null;
            var vm = new PresetsViewModel(
                new SettingsFileStore(),
                () => new Settings { LockUsableAreaTablet = true },
                s => { applied = s; return Task.CompletedTask; })
            { PresetDirectory = dir };

            await vm.SavePresetCommand.ExecuteAsync(null);   // writes Snapshot.json
            await vm.LoadPresetCommand.ExecuteAsync("Snapshot");

            Assert.NotNull(applied);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Save_WhenNoCurrentSettings_DoesNothing()
    {
        var dir = TempDir();
        try
        {
            var vm = new PresetsViewModel(new SettingsFileStore(), () => null, _ => Task.CompletedTask)
            { PresetDirectory = dir };

            await vm.SavePresetCommand.ExecuteAsync(null);

            Assert.False(vm.HasPresets);
        }
        finally { Directory.Delete(dir, true); }
    }
}
