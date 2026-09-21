using System;
using System.IO;
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
