using System.Threading.Tasks;
using OpenTabletDriver.Desktop.Profiles;
using OtdWindowsHelper.Domain;
using OtdWindowsHelper.ViewModels;
using Xunit;

namespace OtdWindowsHelper.Tests;

public class TabletSettingsViewModelTests
{
    [Fact]
    public async Task OpenTabletSettings_WithProfileItem_InvokesOpenDelegate()
    {
        Profile? opened = null;
        var vm = new TabletSettingsViewModel(p => { opened = p; return Task.CompletedTask; }, _ => Task.CompletedTask);
        var profile = new Profile();

        await vm.OpenTabletSettingsCommand.ExecuteAsync(new ProfileItem(profile, true, null));

        Assert.Same(profile, opened);
    }

    [Fact]
    public async Task OpenTabletSettings_WithProfile_InvokesOpenDelegate()
    {
        Profile? opened = null;
        var vm = new TabletSettingsViewModel(p => { opened = p; return Task.CompletedTask; }, _ => Task.CompletedTask);
        var profile = new Profile();

        await vm.OpenTabletSettingsCommand.ExecuteAsync(profile);

        Assert.Same(profile, opened);
    }

    [Fact]
    public async Task ForgetProfile_InvokesForgetDelegate()
    {
        string? forgot = null;
        var vm = new TabletSettingsViewModel(_ => Task.CompletedTask, n => { forgot = n; return Task.CompletedTask; });

        await vm.ForgetProfileCommand.ExecuteAsync("Wacom CTL-672");

        Assert.Equal("Wacom CTL-672", forgot);
    }

    [Fact]
    public void HasProfiles_ReflectsList()
    {
        var vm = new TabletSettingsViewModel(_ => Task.CompletedTask, _ => Task.CompletedTask);
        Assert.False(vm.HasProfiles);

        vm.Profiles = new() { new ProfileItem(new Profile(), false, null) };

        Assert.True(vm.HasProfiles);
    }
}
