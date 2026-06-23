using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdWindowsHelper.Domain;
using OtdWindowsHelper.Services;
using OtdWindowsHelper.ViewModels;
using Xunit;

namespace OtdWindowsHelper.Tests;

public class TabletSettingsViewModelTests
{
    private sealed class FakeSettingsCoordinator : ISettingsCoordinator
    {
        public Settings? CurrentSettings { get; set; }
        public Settings? Applied { get; private set; }
        public Task ApplyAndSaveSettingsAsync(Settings settings) { Applied = settings; return Task.CompletedTask; }
    }

    [Fact]
    public async Task OpenTabletSettings_WithProfileItem_InvokesOpenDelegate()
    {
        Profile? opened = null;
        var vm = new TabletSettingsViewModel(new FakeSettingsCoordinator(), p => { opened = p; return Task.CompletedTask; });
        var profile = new Profile();

        await vm.OpenTabletSettingsCommand.ExecuteAsync(new ProfileItem(profile, true, null));

        Assert.Same(profile, opened);
    }

    [Fact]
    public async Task OpenTabletSettings_WithProfile_InvokesOpenDelegate()
    {
        Profile? opened = null;
        var vm = new TabletSettingsViewModel(new FakeSettingsCoordinator(), p => { opened = p; return Task.CompletedTask; });
        var profile = new Profile();

        await vm.OpenTabletSettingsCommand.ExecuteAsync(profile);

        Assert.Same(profile, opened);
    }

    [Fact]
    public async Task ForgetProfile_RemovesProfileAndApplies()
    {
        var profile = new Profile { Tablet = "Wacom CTL-672" };
        var settings = new Settings { Profiles = new ProfileCollection { profile } };
        var coordinator = new FakeSettingsCoordinator { CurrentSettings = settings };
        var vm = new TabletSettingsViewModel(coordinator, _ => Task.CompletedTask);

        await vm.ForgetProfileCommand.ExecuteAsync("Wacom CTL-672");

        Assert.DoesNotContain(settings.Profiles, p => p.Tablet == "Wacom CTL-672");
        Assert.Same(settings, coordinator.Applied);
    }

    [Fact]
    public async Task ForgetProfile_WhenNoCurrentSettings_IsNoOp()
    {
        var coordinator = new FakeSettingsCoordinator();
        var vm = new TabletSettingsViewModel(coordinator, _ => Task.CompletedTask);

        await vm.ForgetProfileCommand.ExecuteAsync("anything");

        Assert.Null(coordinator.Applied);
    }

    [Fact]
    public void HasProfiles_ReflectsList()
    {
        var vm = new TabletSettingsViewModel(new FakeSettingsCoordinator(), _ => Task.CompletedTask);
        Assert.False(vm.HasProfiles);

        vm.Profiles = new() { new ProfileItem(new Profile(), false, null) };

        Assert.True(vm.HasProfiles);
    }
}
