using OtdWindowsHelper.Services;
using OtdWindowsHelper.ViewModels;
using Xunit;

namespace OtdWindowsHelper.Tests;

public class UtilitiesViewModelTests
{
    [Fact]
    public void CleanupInstallPath_MatchesRunner()
    {
        using var vm = new UtilitiesViewModel(new FakeDialogService());
        Assert.Equal(TabletDriverCleanupRunner.InstallDir, vm.CleanupInstallPath);
    }

    [Fact]
    public void Commands_Exist()
    {
        using var vm = new UtilitiesViewModel(new FakeDialogService());
        Assert.NotNull(vm.InstallCleanupCommand);
        Assert.NotNull(vm.RunCleanupCommand);
        Assert.NotNull(vm.UninstallCleanupCommand);
        Assert.NotNull(vm.OpenFolderCommand);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var vm = new UtilitiesViewModel(new FakeDialogService());
        vm.Dispose();
    }
}
