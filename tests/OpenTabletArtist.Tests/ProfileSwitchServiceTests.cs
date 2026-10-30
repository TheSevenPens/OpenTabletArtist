using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenTabletArtist.Services;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

public class ProfileSwitchServiceTests
{
    private sealed class FakePresetStore : IPresetStore
    {
        public System.Collections.Generic.HashSet<string> Existing { get; } = [];
        public void Save(OpenTabletDriver.Desktop.Settings settings, string path) => throw new NotSupportedException();
        public bool TrySave(OpenTabletDriver.Desktop.Settings settings, string path) => throw new NotSupportedException();
        public bool TryLoad(string path, out OpenTabletDriver.Desktop.Settings? settings)
        {
            settings = Existing.Contains(path) ? new() : null;
            return settings is not null;
        }
    }
    private static readonly string Directory = Path.Combine(Path.GetTempPath(), "preset-tests");
    private static (ProfileSwitchService Service, FakeSettingsCoordinator Settings, FakePresetStore Store) Open()
    {
        var settings = new FakeSettingsCoordinator();
        var store = new FakePresetStore();
        store.Existing.Add(Path.Combine(Directory, "Draw.json"));
        return (new ProfileSwitchService(settings, store, () => Directory), settings, store);
    }

    [Fact]
    public async Task LoadingAppliesAndAnnouncesPreset()
    {
        var (service, settings, _) = Open();
        string? announced = null;
        service.Switched += name => announced = name;
        Assert.True(await service.SwitchToAsync("Draw"));
        Assert.Equal(1, settings.ApplyCalls);
        Assert.Equal("Draw", announced);
    }

    /// <summary>
    /// What was applied includes the plugged-in tablet's current profile when the preset predates it.
    /// Without it the daemon generates one and the apply reads back as rejected.
    /// </summary>
    [Fact]
    public async Task APresetWithoutTheConnectedTablet_IsAppliedWithThatTabletsCurrentProfile()
    {
        var (service, settings, _) = Open();
        settings.CurrentSettings = new OpenTabletDriver.Desktop.Settings
        {
            Profiles = new OpenTabletDriver.Desktop.Profiles.ProfileCollection
            {
                new OpenTabletDriver.Desktop.Profiles.Profile { Tablet = "Wacom PTK-670", OutputMode = TestOtd.Output() },
            },
        };

        Assert.True(await service.SwitchToAsync("Draw"));

        Assert.Equal(["Wacom PTK-670"], settings.Applied!.Profiles.Select(p => p.Tablet));
    }

    /// <summary>
    /// Loading what is already live is the same as the first time: no question about unsaved changes, which
    /// are the preset itself, and nothing sent to the daemon. It still counts as loaded.
    /// </summary>
    [Fact]
    public async Task LoadingWhatIsAlreadyLive_AsksNothingAndSendsNothing()
    {
        var (service, settings, _) = Open();
        settings.Changes = false;
        var asked = false;
        string? announced = null;
        service.BeforeReplaceAsync = _ => { asked = true; return Task.FromResult(true); };
        service.Switched += name => announced = name;

        Assert.True(await service.SwitchToAsync("Draw"));

        Assert.False(asked);
        Assert.Equal(0, settings.ApplyCalls);
        Assert.Equal("Draw", announced);
    }

    /// <summary>The question is still asked when the load would replace something.</summary>
    [Fact]
    public async Task LoadingSomethingDifferent_StillAsks()
    {
        var (service, settings, _) = Open();
        settings.Changes = true;
        var asked = false;
        service.BeforeReplaceAsync = _ => { asked = true; return Task.FromResult(true); };

        Assert.True(await service.SwitchToAsync("Draw"));

        Assert.True(asked);
        Assert.Equal(1, settings.ApplyCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrRejectedPresetReportsFailure(bool rejected)
    {
        var (service, settings, _) = Open();
        settings.DaemonAccepts = !rejected;
        var failed = false;
        var announced = false;
        service.SwitchFailed += _ => failed = true;
        service.Switched += _ => announced = true;
        Assert.False(await service.SwitchToAsync(rejected ? "Draw" : "Missing"));
        Assert.True(failed);
        Assert.False(announced);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationLeavesCurrentSettingsAlone(bool restore)
    {
        var (service, settings, _) = Open();
        service.BeforeReplaceAsync = restoring =>
        {
            Assert.Equal(restore, restoring);
            return Task.FromResult(false);
        };
        Assert.False(await (restore ? service.RestoreDefaultAsync() : service.SwitchToAsync("Draw")));
        Assert.Equal(0, settings.ApplyCalls);
        Assert.Equal(0, settings.RestoreCalls);
    }

    [Fact]
    public async Task RepeatedHotkeyWhilePromptIsOpenDoesNotOpenAnotherOrReplaceTwice()
    {
        var (service, settings, _) = Open();
        var answer = new TaskCompletionSource<bool>();
        var asks = 0;
        service.BeforeReplaceAsync = _ => { asks++; return answer.Task; };
        var first = service.SwitchToAsync("Draw");
        Assert.False(await service.SwitchToAsync("Draw"));
        answer.SetResult(true);
        Assert.True(await first);
        Assert.Equal(1, asks);
        Assert.Equal(1, settings.ApplyCalls);
    }

    [Theory]
    [InlineData(SettingsRestoreStatus.Restored)]
    [InlineData(SettingsRestoreStatus.SourceUnavailable)]
    [InlineData(SettingsRestoreStatus.ApplyFailed)]
    [InlineData(SettingsRestoreStatus.Disconnected)]
    public async Task RestoreReportsTheActualResultEvenWithoutAPreviousPreset(SettingsRestoreStatus result)
    {
        var (service, settings, _) = Open();
        settings.RestoreResult = new SettingsRestoreOutcome(result);
        var announced = false;
        SettingsRestoreStatus? failure = null;
        service.Switched += name => { Assert.Null(name); announced = true; };
        service.RestoreFailed += status => failure = status;
        var success = await service.RestoreDefaultAsync();
        Assert.Equal(result == SettingsRestoreStatus.Restored, success);
        Assert.Equal(success, announced);
        Assert.Equal(success ? null : result, failure);
        Assert.Equal(1, settings.RestoreCalls);
    }
}
