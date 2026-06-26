using System.Collections.Generic;
using System.Linq;
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

    private static TabletSettingsViewModel NewVm(
        FakeSettingsCoordinator? coordinator = null,
        FakeDeviceData? device = null,
        FakeDialogService? dialogs = null)
        => new(coordinator ?? new FakeSettingsCoordinator(), device ?? new FakeDeviceData(), dialogs ?? new FakeDialogService());

    [Fact]
    public async Task OpenTabletSettings_WithProfileItem_OpensDialog()
    {
        var dialogs = new FakeDialogService();
        var vm = NewVm(dialogs: dialogs);
        var profile = new Profile();

        await vm.OpenTabletSettingsCommand.ExecuteAsync(new ProfileItem(profile, true, null));

        Assert.Same(profile, dialogs.ShownProfile);
    }

    [Fact]
    public async Task OpenTabletSettings_WithProfile_OpensDialog()
    {
        var dialogs = new FakeDialogService();
        var vm = NewVm(dialogs: dialogs);
        var profile = new Profile();

        await vm.OpenTabletSettingsCommand.ExecuteAsync(profile);

        Assert.Same(profile, dialogs.ShownProfile);
    }

    [Fact]
    public async Task ForgetProfile_RemovesProfileAndApplies()
    {
        var profile = new Profile { Tablet = "Wacom CTL-672" };
        var settings = new Settings { Profiles = new ProfileCollection { profile } };
        var coordinator = new FakeSettingsCoordinator { CurrentSettings = settings };
        var vm = NewVm(coordinator);

        await vm.ForgetProfileCommand.ExecuteAsync("Wacom CTL-672");

        Assert.DoesNotContain(settings.Profiles, p => p.Tablet == "Wacom CTL-672");
        Assert.Same(settings, coordinator.Applied);
    }

    [Fact]
    public async Task ForgetProfile_WhenNoCurrentSettings_IsNoOp()
    {
        var coordinator = new FakeSettingsCoordinator();
        var vm = NewVm(coordinator);

        await vm.ForgetProfileCommand.ExecuteAsync("anything");

        Assert.Null(coordinator.Applied);
    }

    [Fact]
    public void HasProfiles_ReflectsList()
    {
        var vm = NewVm();
        Assert.False(vm.HasProfiles);

        vm.Profiles = new() { new ProfileItem(new Profile(), false, null) };

        Assert.True(vm.HasProfiles);
    }

    [Fact]
    public void DataLoaded_RefreshesProfilesFromSession()
    {
        var device = new FakeDeviceData();
        var vm = NewVm(device: device);
        Assert.False(vm.HasProfiles);

        device.Profiles = new List<ProfileItem> { new(new Profile { Tablet = "Wacom" }, true, null) };
        device.RaiseDataLoaded();

        Assert.True(vm.HasProfiles);
        Assert.Equal("Wacom", vm.Profiles[0].Profile.Tablet);
    }

    // #137: detected tablet first, then most-recently-seen, then never-seen (alphabetical).
    [Fact]
    public void Profiles_OrderedByDetectedThenRecencyThenName()
    {
        var now = System.DateTime.Now;
        var device = new FakeDeviceData
        {
            Profiles = new List<ProfileItem>
            {
                new(new Profile { Tablet = "NeverSeen-B" }, IsDetected: false, LastSeen: null),
                new(new Profile { Tablet = "SeenOld" },     IsDetected: false, LastSeen: now.AddDays(-3)),
                new(new Profile { Tablet = "Detected" },     IsDetected: true,  LastSeen: now),
                new(new Profile { Tablet = "NeverSeen-A" }, IsDetected: false, LastSeen: null),
                new(new Profile { Tablet = "SeenRecent" },  IsDetected: false, LastSeen: now.AddHours(-1)),
            }
        };

        var vm = NewVm(device: device);

        Assert.Equal(
            new[] { "Detected", "SeenRecent", "SeenOld", "NeverSeen-A", "NeverSeen-B" },
            vm.Profiles.Select(p => p.Tablet).ToArray());
    }
}
