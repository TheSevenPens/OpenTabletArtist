using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.Tests;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using Xunit;
using OtdInterop;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The four settings-coordinator defects from the follow-up review of #731 — #763, #764, #765, #766.
///
/// All four are interactions between things added across #734, #740 and #743 rather than pre-existing
/// bugs, and none was caught by the tests written alongside those changes: those tests encode the same
/// assumptions the code does. Each test here was written to fail against the merged code first.
/// </summary>
public class SettingsCoordinatorFollowupTests
{
    private sealed class StubLifecycle : IDaemonLifecycleService
    {
        public string? ExpectedExePath() => null;
        public bool IsAppManaged(string? path) => false;
        public bool HasBundledDaemon() => false;
        public string? FindExe() => null;
        public bool IsRunning() => false;
        public string? Launch() => null;
        public bool Stop(int processId) => true;
        public void StopAll() { }
        public string? GetProcessPath(int processId) => null;
        public string? GetSingleRunningDaemonPath() => null;
    }

    private sealed class RecordingStore : ISettingsFileStore
    {
        public List<Settings> Saved { get; } = new();
        public Settings? ToLoad { get; set; }
        public bool SaveSucceeds { get; set; } = true;

        public void Save(Settings settings, string path) => Saved.Add(settings);
        public bool TrySave(Settings settings, string path)
        {
            if (!SaveSucceeds) return false;
            Saved.Add(settings);
            return true;
        }
        public bool TryLoad(string path, out Settings? settings)
        {
            settings = ToLoad;
            return ToLoad != null;
        }
    }

    private static Settings SettingsFor(string tablet, bool locked = false) => new()
    {
        LockUsableAreaDisplay = locked,
        Profiles = new ProfileCollection { new Profile { Tablet = tablet } },
    };

    private static string FirstTablet(Settings? s) => s?.Profiles[0].Tablet ?? "";

    private static (AppSession session, FakeDaemonTransport daemon, RecordingStore store) Make()
    {
        var daemon = new FakeDaemonTransport
        {
            Settings = SettingsFor("Baseline"),
            AppInfo = new AppInfo
            {
                AppDataDirectory = "x",
                SettingsFile = "settings.json",
                PluginDirectory = "",   // keeps the load's fire-and-forget plugin install out of the test
            },
        };
        var store = new RecordingStore();
        return (new AppSession(daemon, new StubLifecycle(), store), daemon, store);
    }

    // --- #763: a no-change apply must not reload ---

    /// <summary>
    /// The no-op guard exists to break the apply → reload → binding-write-back loop. Reloading on
    /// NoChange re-arms exactly that cycle, and because the coordinator returns at its equality guard it
    /// never reaches the circuit breaker meant to back it up. A regression from #740: before the guard
    /// moved into the coordinator it returned before the reload.
    /// </summary>
    [AvaloniaFact]
    public async Task ReapplyingIdenticalSavedSettings_DoesNotReload()
    {
        var (session, daemon, _) = Make();
        using var _s = session;
        await session.ReloadAsync();

        var settings = SettingsFor("Edited");
        await session.ApplyAndSaveSettingsAsync(settings);
        var readsAfterFirstApply = daemon.GetSettingsCalls;

        var again = await session.ApplyAndSaveSettingsAsync(settings);

        Assert.Equal(SettingsApplyStatus.NoChange, again.Status);
        Assert.Equal(readsAfterFirstApply, daemon.GetSettingsCalls);
    }

    [AvaloniaFact]
    public async Task ReapplyingIdenticalSavedSettings_DoesNotRaiseDataLoaded()
    {
        var (session, _, _) = Make();
        using var _s = session;
        await session.ReloadAsync();
        var settings = SettingsFor("Edited");
        await session.ApplyAndSaveSettingsAsync(settings);

        var loads = 0;
        session.DataLoaded += () => loads++;
        await session.ApplyAndSaveSettingsAsync(settings);

        Assert.Equal(0, loads);
    }

    // --- #764: restoring the default must supersede a pending save ---

    /// <summary>
    /// A pending save is an edit the daemon took but the disk refused. Restoring the saved default is the
    /// user discarding exactly that; writing it out afterwards puts back what they just removed, and
    /// leaves disk and daemon disagreeing until the next restart resolves it the wrong way.
    /// </summary>
    [AvaloniaFact]
    public async Task RestoringTheDefault_CancelsAPendingSave()
    {
        var (session, daemon, store) = Make();
        using var _s = session;
        await session.ReloadAsync();

        store.SaveSucceeds = false;
        await session.ApplyAndSaveSettingsAsync(SettingsFor("UnsavedEdit"));

        store.SaveSucceeds = true;
        store.ToLoad = SettingsFor("SavedDefault");
        var restore = await session.RestoreDefaultAsync();
        Assert.True(restore.IsRestored);

        await session.ReloadAsync();   // the retry would fire here

        Assert.DoesNotContain(store.Saved, s => FirstTablet(s) == "UnsavedEdit");
        Assert.Equal("SavedDefault", FirstTablet(daemon.Settings));
        Assert.Equal("SavedDefault", FirstTablet(session.CurrentSettings));
    }

    // --- #765: a pending save must be a snapshot, not a live reference ---

    /// <summary>
    /// OTA mutates settings objects in place — the tablet editor mutates its profile and pushes the same
    /// object — so holding the caller's reference means a later edit rewrites what the retry will save,
    /// including an edit the daemon rejected.
    /// </summary>
    [AvaloniaFact]
    public async Task APendingSave_IsNotRewrittenByALaterRejectedEdit()
    {
        var (session, daemon, store) = Make();
        using var _s = session;
        await session.ReloadAsync();

        // Accepted by the daemon, refused by the disk → pending.
        var settings = SettingsFor("Edited", locked: true);
        store.SaveSucceeds = false;
        await session.ApplyAndSaveSettingsAsync(settings);

        // The same object is edited again, and this time the daemon refuses it.
        settings.LockUsableAreaDisplay = false;
        daemon.SetSettingsSucceeds = false;
        await session.ApplyAndSaveSettingsAsync(settings);

        // The retry must write the revision the daemon accepted, not the one it rejected.
        daemon.SetSettingsSucceeds = true;
        store.SaveSucceeds = true;
        await session.ReloadAsync();   // the reload is what drives the retry in production

        Assert.NotEmpty(store.Saved);
        Assert.True(store.Saved[^1].LockUsableAreaDisplay);
    }

    // --- #766: a disconnected apply must not be reported as an override ---

    /// <summary>
    /// Shipping behaviour: the hotkey snapshot switch. With no transport the change never leaves the app,
    /// but the switch reported success, set the active snapshot and raised its toast — the same class of
    /// lie #734 existed to remove, on a path #734 deliberately skipped and #737 never came back to.
    /// </summary>
    [AvaloniaFact]
    public async Task ADisconnectedLiveOnlyApply_DoesNotClaimSuccess()
    {
        var (session, daemon, _) = Make();
        using var _s = session;
        await session.ReloadAsync();
        daemon.SetSettingsSucceeds = false;

        var applied = await session.ApplyLiveOnlyAsync(SettingsFor("Preset"));

        Assert.False(applied);
    }

    [AvaloniaFact]
    public async Task ADisconnectedEphemeralApply_DoesNotSetAnOverride()
    {
        var (session, daemon, _) = Make();
        using var _s = session;
        await session.ReloadAsync();
        daemon.SetSettingsSucceeds = false;

        var applied = await session.ApplyEphemeralAsync(SettingsFor("PerApp"));

        Assert.False(applied);
        Assert.False(session.HasEphemeralOverride);
    }

    /// <summary>An override that was never established must not suppress the reload's settings read —
    /// that guard is what keeps a real override off the editor's baseline (#737), and it should not fire
    /// for one that does not exist.</summary>
    [AvaloniaFact]
    public async Task AFailedEphemeralApply_LeavesTheReloadReadingNormally()
    {
        var (session, daemon, _) = Make();
        using var _s = session;
        await session.ReloadAsync();

        daemon.SetSettingsSucceeds = false;
        await session.ApplyEphemeralAsync(SettingsFor("PerApp"));

        daemon.SetSettingsSucceeds = true;
        daemon.Settings = SettingsFor("ChangedElsewhere");
        await session.ReloadAsync();

        Assert.Equal("ChangedElsewhere", FirstTablet(session.CurrentSettings));
    }

    [AvaloniaFact]
    public async Task ADisconnectedClearOverride_ReportsFailure_AndKeepsTheOverride()
    {
        var (session, daemon, _) = Make();
        using var _s = session;
        await session.ReloadAsync();
        await session.ApplyEphemeralAsync(SettingsFor("PerApp"));
        Assert.True(session.HasEphemeralOverride);

        daemon.SetSettingsSucceeds = false;
        var cleared = await session.ClearEphemeralOverrideAsync();

        Assert.False(cleared);
        Assert.True(session.HasEphemeralOverride);   // still on the tablet, so still true
    }
}
