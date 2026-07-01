using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

public class DriverCleanupViewModelTests
{
    [Fact]
    public void CleanupInstallPath_MatchesRunner()
    {
        using var vm = new DriverCleanupViewModel(new FakeDialogService());
        Assert.Equal(TabletDriverCleanupRunner.InstallDir, vm.CleanupInstallPath);
    }

    [Fact]
    public void Commands_Exist()
    {
        using var vm = new DriverCleanupViewModel(new FakeDialogService());
        Assert.NotNull(vm.InstallCleanupCommand);
        Assert.NotNull(vm.RunCleanupCommand);
        Assert.NotNull(vm.UninstallCleanupCommand);
        Assert.NotNull(vm.OpenFolderCommand);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var vm = new DriverCleanupViewModel(new FakeDialogService());
        vm.Dispose();
    }
}
