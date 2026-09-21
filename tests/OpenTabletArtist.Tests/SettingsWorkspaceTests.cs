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

    /// <summary>
    /// The driver changing its own settings while nothing is half-edited is adopted, not paused (#920).
    /// </summary>
    /// <remarks>
    /// The case the hands-on pass hit: attaching a tablet OTD has not seen makes it generate a profile
    /// and write the whole document back, and pausing for that stopped an artist who had done nothing
    /// but plug something in. While idle there is no draft to protect — one live snapshot to show — so
    /// the honest response is to show what the driver holds.
    /// </remarks>
    [Fact]
    public async Task ADriverChangeWhileIdle_IsAdoptedRatherThanPaused()
    {
        var (session, daemon, store) = await Connected();
        using var lifetime = session;
        var workspace = new SettingsWorkspace(session.Settings!, false);

        // What the driver does to itself when a new tablet arrives.
        var withNewTablet = Document();
        withNewTablet.Profiles.Add(new Profile { Tablet = "A tablet OTD had not seen" });
        ProfileSanitizer.EnsureValidAbsoluteAreas(withNewTablet);
        daemon.Settings = withNewTablet;

        var refreshed = await workspace.RefreshAsync(Idle);

        Assert.Equal(SettingsReloadStatus.Adopted, refreshed.Status);
        Assert.False(workspace.IsPaused, "an idle refresh should not have paused");
        Assert.Equal(3, workspace.Current!.Profiles.Count);

        // Observation only: nothing was written to the driver or the file.
        Assert.Empty(daemon.Applied);
        Assert.Equal(0, store.Attempts);

        // And editing continues, from what was adopted.
        var edit = workspace.Current!;
        edit.Profiles[0].BindingSettings.DisablePressure = true;
        Assert.True((await workspace.ApplyAsync(edit)).IsLive);
        Assert.Equal(3, daemon.Settings!.Profiles.Count);
    }

    /// <summary>Applied-but-unsaved still counts as idle.</summary>
    /// <remarks>
    /// Settings applied and not yet written to disk are live driver state. Their difference from the
    /// file is not a local edit at risk, and reading "idle" as "saved" would make every unsaved session
    /// pause the moment a tablet was plugged in — which is most of the time an artist is working.
    /// </remarks>
    [Fact]
    public async Task AnAppliedButUnsavedWorkspaceIsStillIdle()
    {
        var (session, daemon, _) = await Connected();
        using var lifetime = session;
        var workspace = new SettingsWorkspace(session.Settings!, false);

        var edit = workspace.Current!;
        edit.Profiles[0].BindingSettings.DisablePressure = true;
        Assert.True((await workspace.ApplyAsync(edit)).IsLive);
        Assert.True(workspace.HasUnsavedChanges, "applied but not saved");

        var theirs = SettingsCodec.Clone(daemon.Settings!);
        theirs.Profiles.Add(new Profile { Tablet = "New" });
        ProfileSanitizer.EnsureValidAbsoluteAreas(theirs);
        daemon.Settings = theirs;

        Assert.Equal(SettingsReloadStatus.Adopted, (await workspace.RefreshAsync(Idle)).Status);
        Assert.False(workspace.IsPaused);
    }

    /// <summary>Half-edited input is not adopted over; that pauses for the artist to answer.</summary>
    [Fact]
    public async Task WithInputPendingInTheHost_AnOutsideChangePauses()
    {
        var (session, daemon, store) = await Connected();
        using var lifetime = session;
        var workspace = new SettingsWorkspace(session.Settings!, false);

        var theirs = SettingsCodec.Clone(daemon.Settings!);
        theirs.Profiles[0].BindingSettings.DisableTilt = true;
        daemon.Settings = theirs;

        Assert.Equal(SettingsReloadStatus.Paused, (await workspace.RefreshAsync(Editing)).Status);
        Assert.True(workspace.IsPaused);
        Assert.Empty(daemon.Applied);
        Assert.Equal(0, store.Attempts);

        // Explicit Reload is the way out, as everywhere else.
        Assert.Equal(SettingsReloadStatus.Adopted, (await workspace.ReloadAsync()).Status);
        Assert.False(workspace.IsPaused);
    }

    /// <summary>
    /// Input that arrives <em>during</em> the observation keeps its pause (#920).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asking before the read is not enough: the read takes as long as the driver takes, and the artist
    /// can put a hand on a slider while it is outstanding. Nothing has been submitted at that point, so
    /// no apply is queued and no counter has moved — only asking the host again, after the read, catches
    /// it.
    /// </para>
    /// <para>
    /// Written this way deliberately. My first version made a real edit during the read, which the
    /// operation gate already serializes behind the observation — so the pending-apply check caught it
    /// and the test passed with the re-ask removed. Unsubmitted input is the case that has no other
    /// guard.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task InputThatBeginsWhileTheObservationIsOutstanding_KeepsItsPause()
    {
        var (session, daemon, _) = await Connected();
        using var lifetime = session;
        var workspace = new SettingsWorkspace(session.Settings!, false);

        var theirs = SettingsCodec.Clone(daemon.Settings!);
        theirs.Profiles[0].BindingSettings.DisableTilt = true;

        // Idle when the observation starts; the artist touches a control while it is out.
        var editing = false;
        var held = new TaskCompletionSource<Settings?>();
        daemon.GetSettingsHandler = () => held.Task;

        var refreshing = workspace.RefreshAsync(() => !editing);
        editing = true;
        held.SetResult(theirs);

        var refreshed = await refreshing;

        Assert.Equal(SettingsReloadStatus.Paused, refreshed.Status);
        Assert.True(workspace.IsPaused,
            "an observation that began while idle adopted over input that arrived during it");
    }

    /// <summary>A pause already on the board is never cleared by a background refresh.</summary>
    /// <remarks>
    /// Answering it is the artist's. A poll quietly resolving it is how a protection becomes a
    /// formality — and it would resolve it in the direction of discarding their draft.
    /// </remarks>
    [Fact]
    public async Task AnExistingPauseIsNotClearedByAnIdleRefresh()
    {
        var (session, daemon, _) = await Connected();
        using var lifetime = session;
        var workspace = new SettingsWorkspace(session.Settings!, false);

        var theirs = SettingsCodec.Clone(daemon.Settings!);
        theirs.Profiles[0].BindingSettings.DisableTilt = true;
        daemon.Settings = theirs;

        Assert.Equal(SettingsReloadStatus.Paused, (await workspace.RefreshAsync(Editing)).Status);
        Assert.True(workspace.IsPaused);

        // The host goes idle, and a later poll finds the same difference. It stays paused.
        Assert.NotEqual(SettingsReloadStatus.Adopted, (await workspace.RefreshAsync(Idle)).Status);
        Assert.True(workspace.IsPaused, "a poll cleared a pause the artist had not answered");
    }

    private static async Task<(OtdSession Session, FakeDaemonTransport Daemon, MemorySettingsFileStore Store)>
        Connected()
    {
        var daemon = new FakeDaemonTransport { Settings = Document() };
        var store = new MemorySettingsFileStore { Saved = Document() };
        var session = FakeSession.Over(daemon, store);
        daemon.Reconnect();
        await session.InitializeAsync();
        return (session, daemon, store);
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
        await workspace.RefreshAsync(Idle);
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

    /// <summary>Nothing is half-edited in the host — the ordinary state while a poll runs.</summary>
    private static bool Idle() => true;

    /// <summary>Something is, so an outside change must not be adopted over it.</summary>
    private static bool Editing() => false;
}
