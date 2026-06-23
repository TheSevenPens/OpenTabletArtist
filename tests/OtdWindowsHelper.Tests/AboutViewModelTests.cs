using OtdWindowsHelper.ViewModels;
using Xunit;

namespace OtdWindowsHelper.Tests;

public class AboutViewModelTests
{
    [Fact]
    public void RepoUrl_IsTheProjectRepository()
    {
        var vm = new AboutViewModel();
        Assert.Equal("https://github.com/TheSevenPens/OTDWindowsHelper", vm.RepoUrl);
    }

    [Fact]
    public void OpenUrlCommand_Exists()
    {
        var vm = new AboutViewModel();
        Assert.NotNull(vm.OpenUrlCommand);
        Assert.True(vm.OpenUrlCommand.CanExecute(vm.RepoUrl));
    }
}
