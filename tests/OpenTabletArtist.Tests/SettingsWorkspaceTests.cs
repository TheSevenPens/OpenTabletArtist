using System;
using System.Threading.Tasks;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.Tests;

public class SettingsWorkspaceTests
{
    private static Settings Document()
    {
        var settings = new Settings
        {
            Profiles = new ProfileCollection { new Profile { Tablet = "A" }, new Profile { Tablet = "B" } }
        };
        ProfileSanitizer.EnsureValidAbsoluteAreas(settings);
        return settings;
    }

    [Fact]
    public async Task EditsFromTwoCachedEditorsPreserveBothProfilesAndSaveLatestAfterApply()
    {
        var daemon = new FakeDaemonTransport { Settings = Document() };
        using var session = FakeSession.Over(daemon);
        daemon.Reconnect();
        await session.InitializeAsync();
        var workspace = new SettingsWorkspace(session.Settings!, false);
        var editorA = workspace.Current!.Profiles[0];
        var editorB = workspace.Current!.Profiles[1];
        editorA.BindingSettings.DisablePressure = true;
        editorB.BindingSettings.DisableTilt = true;
        var release = new TaskCompletionSource<bool>();
        daemon.SetSettingsHandler = _ => release.Task;
        var first = workspace.ApplyProfileAsync(editorA);
        var second = workspace.ApplyProfileAsync(editorB);
        var save = workspace.SaveAsync();
        Assert.False(save.IsCompleted);
        release.SetResult(true);
        Assert.True((await first).IsLive);
        Assert.True((await second).IsLive);
        Assert.True((await save).IsSaved);
        Assert.True(daemon.Settings!.Profiles[0].BindingSettings.DisablePressure);
        Assert.True(daemon.Settings.Profiles[1].BindingSettings.DisableTilt);
        Assert.False(workspace.HasUnsavedChanges);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadingNeverChangesFiltersAndOnlyAnOwnedDaemonGetsPolicyOnEdit(bool owned)
    {
        const string filter = "OpenTabletDriver.Filters.Noise.NoiseReduction";
        var settings = Document();
        settings.Profiles[0].Filters.Add(new PluginSettingStore(filter) { Path = filter, Enable = true });
        var daemon = new FakeDaemonTransport { Settings = settings };
        var store = new MemorySettingsFileStore { Saved = SettingsCodec.Clone(settings) };
        using var session = FakeSession.Over(daemon, store);
        daemon.Reconnect();
        await session.InitializeAsync();
        var workspace = new SettingsWorkspace(session.Settings!, owned);
        await workspace.RefreshAsync();
        Assert.Empty(daemon.Applied);
        Assert.Equal(0, store.Attempts);
        var edit = workspace.Current!;
        edit.Profiles[0].BindingSettings.DisablePressure = true;
        Assert.True((await workspace.ApplyAsync(edit)).IsLive);
        Assert.Equal(!owned, daemon.Settings!.Profiles[0].Filters[0].Enable);
        Assert.Equal(0, store.Attempts);
    }

    [Fact]
    public async Task FailedApplyBlocksSaveUntilReloadAndDoesNotPretendTheDraftReachedTheDriver()
    {
        var daemon = new FakeDaemonTransport { Settings = Document(), SetSettingsSucceeds = false };
        var store = new MemorySettingsFileStore();
        using var session = FakeSession.Over(daemon, store);
        daemon.Reconnect();
        await session.InitializeAsync();
        var workspace = new SettingsWorkspace(session.Settings!, false);
        var edit = workspace.Current!;
        edit.Profiles[0].BindingSettings.DisablePressure = true;
        Assert.False((await workspace.ApplyAsync(edit)).IsLive);
        Assert.True(workspace.IsPaused);
        Assert.False((await workspace.SaveAsync()).IsSaved);
        Assert.Equal(0, store.Attempts);
        await workspace.ReloadAsync();
        Assert.False(workspace.IsPaused);
        Assert.False(workspace.Current!.Profiles[0].BindingSettings.DisablePressure);
    }

    [Fact]
    public async Task SaveBlocksAdditionalWritesUntilItFinishes()
    {
        var daemon = new FakeDaemonTransport { Settings = Document() };
        using var session = FakeSession.Over(daemon);
        daemon.Reconnect();
        await session.InitializeAsync();
        var workspace = new SettingsWorkspace(session.Settings!, false);
        var read = new TaskCompletionSource<Settings?>();
        daemon.GetSettingsHandler = () => read.Task;
        var saving = workspace.SaveAsync();
        var edit = workspace.Current!;
        edit.Profiles[0].BindingSettings.DisablePressure = true;
        Assert.False((await workspace.ApplyAsync(edit)).IsLive);
        read.SetResult(daemon.Settings);
        Assert.True((await saving).IsSaved);
        Assert.Empty(daemon.Applied);
    }
}
