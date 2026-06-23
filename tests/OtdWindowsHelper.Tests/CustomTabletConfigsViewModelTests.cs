using System.IO;
using OtdWindowsHelper.ViewModels;
using Xunit;

namespace OtdWindowsHelper.Tests;

public class CustomTabletConfigsViewModelTests
{
    [Fact]
    public void ConfigurationsDirectory_PointsAtOtdConfigsFolder()
    {
        var vm = new CustomTabletConfigsViewModel();
        Assert.EndsWith(Path.Combine("OpenTabletDriver", "Configurations"), vm.ConfigurationsDirectory);
    }

    [Fact]
    public void Configurations_IsInitialized()
    {
        var vm = new CustomTabletConfigsViewModel();
        Assert.NotNull(vm.Configurations);
    }

    [Fact]
    public void Commands_Exist()
    {
        var vm = new CustomTabletConfigsViewModel();
        Assert.NotNull(vm.RefreshConfigurationsCommand);
        Assert.NotNull(vm.OpenConfigurationsFolderCommand);
        Assert.NotNull(vm.ViewConfigurationCommand);
        Assert.NotNull(vm.DeleteConfigurationCommand);
    }
}
