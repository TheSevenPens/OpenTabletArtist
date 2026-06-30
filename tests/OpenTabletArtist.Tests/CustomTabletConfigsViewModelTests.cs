using System;
using System.IO;
using System.Threading.Tasks;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

public class CustomTabletConfigsViewModelTests
{
    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"otdcfg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    private static CustomTabletConfigsViewModel NewVm(string dir, FakeDialogService? dialogs = null)
        => new(dialogs ?? new FakeDialogService(), new FakeConfigurationsDirectoryProvider(dir));

    [Fact]
    public void EmptyDirectory_HasNoConfigurations()
    {
        var dir = TempDir();
        try
        {
            var vm = NewVm(dir);
            Assert.Equal(dir, vm.ConfigurationsDirectory);
            Assert.NotNull(vm.Configurations);
            Assert.False(vm.HasConfigurations);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Commands_Exist()
    {
        var dir = TempDir();
        try
        {
            var vm = NewVm(dir);
            Assert.NotNull(vm.RefreshConfigurationsCommand);
            Assert.NotNull(vm.OpenConfigurationsFolderCommand);
            Assert.NotNull(vm.ViewConfigurationCommand);
            Assert.NotNull(vm.DeleteConfigurationCommand);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Scan_ListsJsonFilesWithFriendlyName()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "ctl672.json"), "{\"Name\":\"Wacom CTL-672\"}");

            var vm = NewVm(dir);

            Assert.True(vm.HasConfigurations);
            Assert.Contains(vm.Configurations, c => c.Name == "Wacom CTL-672");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ViewConfiguration_OpensTextViewerWithFormattedJson()
    {
        var dir = TempDir();
        try
        {
            var file = Path.Combine(dir, "tablet.json");
            await File.WriteAllTextAsync(file, "{\"Name\":\"Wacom\"}");
            var dialogs = new FakeDialogService();
            var vm = NewVm(dir, dialogs);

            await vm.ViewConfigurationCommand.ExecuteAsync(file);

            Assert.NotNull(dialogs.LastTextViewer);
            Assert.Equal("tablet.json", dialogs.LastTextViewer!.Value.Title);
            Assert.Contains("\"Name\": \"Wacom\"", dialogs.LastTextViewer.Value.Content); // pretty-printed
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task DeleteConfiguration_WhenConfirmed_DeletesAndRefreshes()
    {
        var dir = TempDir();
        try
        {
            var file = Path.Combine(dir, "tablet.json");
            await File.WriteAllTextAsync(file, "{}");
            var dialogs = new FakeDialogService { ConfirmResult = true };
            var vm = NewVm(dir, dialogs);
            Assert.True(vm.HasConfigurations);

            await vm.DeleteConfigurationCommand.ExecuteAsync(file);

            Assert.False(File.Exists(file));      // confirmed → deleted
            Assert.False(vm.HasConfigurations);   // list rescanned
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task DeleteConfiguration_WhenNotConfirmed_KeepsFile()
    {
        var dir = TempDir();
        try
        {
            var file = Path.Combine(dir, "tablet.json");
            await File.WriteAllTextAsync(file, "{}");
            var dialogs = new FakeDialogService { ConfirmResult = false };
            var vm = NewVm(dir, dialogs);

            await vm.DeleteConfigurationCommand.ExecuteAsync(file);

            Assert.True(File.Exists(file)); // declined → not deleted
        }
        finally { Directory.Delete(dir, true); }
    }
}
