using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

public class AboutViewModelTests
{
    [Fact]
    public void RepoUrl_IsTheProjectRepository()
    {
        var vm = new AboutViewModel();
        Assert.Equal("https://github.com/TheSevenPens/OpenTabletArtist", vm.RepoUrl);
    }

    // The "User manual" link on Home and About: the published documentation site, not a file on GitHub.
    [Fact]
    public void UserManualUrl_IsTheDocumentationSite()
    {
        var vm = new AboutViewModel();
        Assert.Equal("https://docs.sevenpens.com/opentabletartist", vm.UserManualUrl);
        Assert.True(vm.OpenUrlCommand.CanExecute(vm.UserManualUrl));
    }

    [Fact]
    public void OpenUrlCommand_Exists()
    {
        var vm = new AboutViewModel();
        Assert.NotNull(vm.OpenUrlCommand);
        Assert.True(vm.OpenUrlCommand.CanExecute(vm.RepoUrl));
    }
}
