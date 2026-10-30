using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Loading a preset while the artist's own edit is on its way (#1060). The comparison that decides whether to
/// ask about unsaved changes has to await the daemon's tablet list, and an edit submitted in that gap changes
/// the document the comparison was about. Skipping the question then let the preset overwrite an edit that had
/// just been applied successfully.
/// </summary>
public class PresetLoadRaceTests
{
    private static Settings Document()
    {
        var settings = new Settings
        {
            Profiles = new ProfileCollection { new Profile { OutputMode = TestOtd.Output(), Tablet = "T" } },
        };
        ProfileSanitizer.EnsureValidAbsoluteAreas(settings);
        return settings;
    }

    private sealed class OnePreset(Settings preset) : IPresetStore
    {
        public void Save(Settings settings, string path) => throw new NotSupportedException();
        public bool TryLoad(string path, out Settings? settings)
        {
            settings = SettingsCodec.Clone(preset);
            return true;
        }
    }

    /// <summary>The real workspace behind the switch service, as the app's session is.</summary>
    private sealed class Coordinator(SettingsWorkspace workspace) : ISettingsCoordinator
    {
        public Settings? CurrentSettings => workspace.Current;
        public Task<SettingsApplyOutcome> ApplySettingsAsync(Settings settings) => workspace.ApplyAsync(settings);
        public Task<SettingsApplyOutcome> ApplyLoadedSettingsAsync(Settings settings) => workspace.ApplyLoadedAsync(settings);
        public Task<bool> WouldChangeLoadedAsync(Settings settings) => workspace.WouldChangeLoadedAsync(settings);
        public SettingsStamp Stamp => new(workspace, workspace.Generation);
        public Task<SettingsRestoreOutcome> RestoreDefaultAsync() => throw new NotSupportedException();
    }

    private static async Task<(OtdSession Session, FakeDaemonTransport Daemon, SettingsWorkspace Workspace)> Connected()
    {
        var daemon = new FakeDaemonTransport { Settings = Document() };
        var session = FakeSession.Over(daemon, new MemorySettingsFileStore { Saved = Document() });
        daemon.Reconnect();
        await session.InitializeAsync();
        return (session, daemon, new SettingsWorkspace(session.Settings!, owned: false));
    }

    /// <summary>
    /// The review's reproduction. The document and the preset are equal, so the question would be skipped; an
    /// edit is submitted while the tablet list is being waited for; the question must be asked after all, and
    /// refusing it must leave the edit in place.
    /// </summary>
    [Fact]
    public async Task AnEditSubmittedWhileTheComparisonRuns_IsNotOverwrittenWithoutAsking()
    {
        var (session, daemon, workspace) = await Connected();
        using var lifetime = session;
        var service = new ProfileSwitchService(new Coordinator(workspace), new OnePreset(Document()), () => "presets");
        var asked = 0;
        service.BeforeReplaceAsync = _ => { asked++; return Task.FromResult(false); };   // the artist says no
        var release = new TaskCompletionSource<JArray>();
        daemon.GetTabletsHandler = () => release.Task;

        var load = service.SwitchToAsync("A");           // waits for the tablet list
        var edit = workspace.Current!;
        edit.LockUsableAreaTablet = true;
        var editApplied = workspace.ApplyAsync(edit);    // the artist's change, submitted meanwhile
        daemon.GetTabletsHandler = null;
        release.SetResult([]);

        Assert.False(await load, "the artist refused, so the preset is not loaded");
        Assert.Equal(1, asked);
        Assert.True((await editApplied).IsLive);
        Assert.True(daemon.Settings!.LockUsableAreaTablet, "the edit is still there");
    }

    /// <summary>The control: with nothing in between, the same load asks nothing and goes ahead.</summary>
    [Fact]
    public async Task WithNothingInBetween_LoadingWhatIsAlreadyThere_AsksNothing()
    {
        var (session, daemon, workspace) = await Connected();
        using var lifetime = session;
        var service = new ProfileSwitchService(new Coordinator(workspace), new OnePreset(Document()), () => "presets");
        var asked = 0;
        service.BeforeReplaceAsync = _ => { asked++; return Task.FromResult(false); };

        Assert.True(await service.SwitchToAsync("A"));

        Assert.Equal(0, asked);
        Assert.False(daemon.Settings!.LockUsableAreaTablet);
    }
}
