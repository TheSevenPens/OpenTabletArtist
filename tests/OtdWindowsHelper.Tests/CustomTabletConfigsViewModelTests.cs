using System.IO;
using System.Threading.Tasks;
using OtdWindowsHelper.ViewModels;
using Xunit;

namespace OtdWindowsHelper.Tests;

public class CustomTabletConfigsViewModelTests
{
    [Fact]
    public void ConfigurationsDirectory_PointsAtOtdConfigsFolder()
    {
        var vm = new CustomTabletConfigsViewModel(new FakeDialogService());
        Assert.EndsWith(Path.Combine("OpenTabletDriver", "Configurations"), vm.ConfigurationsDirectory);
    }

    [Fact]
    public async Task ViewConfiguration_OpensTextViewerWithFormattedJson()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"otdcfg_{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "tablet.json");
            await File.WriteAllTextAsync(file, "{\"Name\":\"Wacom\"}");
            var dialogs = new FakeDialogService();
            var vm = new CustomTabletConfigsViewModel(dialogs);

            await vm.ViewConfigurationCommand.ExecuteAsync(file);

            Assert.NotNull(dialogs.LastTextViewer);
            Assert.Equal("tablet.json", dialogs.LastTextViewer!.Value.Title);
            Assert.Contains("\"Name\": \"Wacom\"", dialogs.LastTextViewer.Value.Content); // pretty-printed
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task DeleteConfiguration_WhenNotConfirmed_KeepsFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"otdcfg_{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "tablet.json");
            await File.WriteAllTextAsync(file, "{}");
            var dialogs = new FakeDialogService { ConfirmResult = false };
            var vm = new CustomTabletConfigsViewModel(dialogs);

            await vm.DeleteConfigurationCommand.ExecuteAsync(file);

            Assert.True(File.Exists(file)); // declined → not deleted
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Configurations_IsInitialized()
    {
        var vm = new CustomTabletConfigsViewModel(new FakeDialogService());
        Assert.NotNull(vm.Configurations);
    }

    [Fact]
    public void Commands_Exist()
    {
        var vm = new CustomTabletConfigsViewModel(new FakeDialogService());
        Assert.NotNull(vm.RefreshConfigurationsCommand);
        Assert.NotNull(vm.OpenConfigurationsFolderCommand);
        Assert.NotNull(vm.ViewConfigurationCommand);
        Assert.NotNull(vm.DeleteConfigurationCommand);
    }
}
