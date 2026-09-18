using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Tests;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The session's settings behaviour, against a daemon that isn't there (#740).
///
/// None of this was reachable before <see cref="IDaemonTransport"/>: <c>AppSession</c> took a concrete
/// <c>DaemonClient</c>, so the data load and the apply path could only be reasoned about. The baseline
/// case below is the specific thing #737 could not verify, and the reason
/// <see cref="FeatureFlags.PerAppProfiles"/> is still off.
/// </summary>
public class AppSessionSettingsTests
{
    private sealed class StubLifecycle : IDaemonLifecycleService
    {
        public string? ExpectedExePath() => null;
        public bool IsOwnBuild(string? path) => false;
        public bool HasBundledDaemon() => false;
        public string? FindExe() => null;
        public bool IsRunning() => false;
        public string? Launch() => null;
        public bool Stop(int processId) => true;
        public void StopAll() { }
        public string? GetProcessPath(int processId) => null;
        public string? GetSingleRunningDaemonPath() => null;
    }

    /// <summary>Records what reached disk, so "applied" and "saved" can be told apart.</summary>
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

    private static Settings SettingsFor(string tablet) =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = tablet } } };

    private static string FirstTablet(Settings? s) => s?.Profiles[0].Tablet ?? "";

    private static (AppSession session, FakeDaemonTransport daemon, RecordingStore store) Make()
    {
        var daemon = new FakeDaemonTransport { Settings = SettingsFor("Baseline") };
        var store = new RecordingStore();
        return (new AppSession(daemon, new StubLifecycle(), store), daemon, store);
    }

    [AvaloniaFact]
    public async Task AReload_AdoptsWhatTheDaemonHolds()
    {
        var (session, daemon, _) = Make();
        using var _s = session;

        await session.ReloadAsync();

        Assert.Equal("Baseline", FirstTablet(session.CurrentSettings));
        Assert.Equal(1, daemon.GetSettingsCalls);
    }

    // --- Baseline isolation across the poll (#737) ---

    /// <summary>
    /// The case #737 fixed but could not prove. <c>ApplyEphemeralAsync</c> deliberately leaves
    /// <c>CurrentSettings</c> alone, but the reload used to re-read the daemon into it unconditionally —
    /// so within 30 seconds the transient per-app snapshot became the editor's baseline: the thing it
    /// would persist as the user's default, and the source a "restore default" would restore from.
    /// </summary>
    [AvaloniaFact]
    public async Task AReloadDuringAPerAppOverride_KeepsTheBaseline_NotTheSnapshot()
    {
        var (session, daemon, _) = Make();
        using var _s = session;
        await session.ReloadAsync();

        await session.ApplyEphemeralAsync(SettingsFor("PerAppSnapshot"));
        Assert.True(session.HasEphemeralOverride);
        Assert.Equal("PerAppSnapshot", FirstTablet(daemon.Settings));   // the daemon really is on it

        await session.ReloadAsync();   // the 30-second poll

        Assert.Equal("Baseline", FirstTablet(session.CurrentSettings));
    }

    /// <summary>Repeated polls must not wear the guard down — the override survives as many as you like.</summary>
    [AvaloniaFact]
    public async Task RepeatedReloadsDuringAnOverride_NeverAdoptTheSnapshot()
    {
        var (session, _, _) = Make();
        using var _s = session;
        await session.ReloadAsync();
        await session.ApplyEphemeralAsync(SettingsFor("PerAppSnapshot"));

        for (var i = 0; i < 5; i++) await session.ReloadAsync();

        Assert.Equal("Baseline", FirstTablet(session.CurrentSettings));
    }

    /// <summary>A reconnect runs the same load path, so the baseline has to survive that too.</summary>
    [AvaloniaFact]
    public async Task AReconnectDuringAnOverride_KeepsTheBaseline()
    {
        var (session, daemon, _) = Make();
        using var _s = session;
        await session.ReloadAsync();
        await session.ApplyEphemeralAsync(SettingsFor("PerAppSnapshot"));

        daemon.RaiseDisconnected();
        // The session handles daemon events by posting to the dispatcher, and nothing awaits that post.
        // Drain it here so the continuation runs inside the test that caused it rather than during some
        // later test — the headless suite shares one dispatcher and does not tolerate stragglers.
        Dispatcher.UIThread.RunJobs();

        await session.ReloadAsync();

        Assert.Equal("Baseline", FirstTablet(session.CurrentSettings));
    }

    [AvaloniaFact]
    public async Task ClearingTheOverride_PutsTheDaemonBackOnTheBaseline_AndResumesReading()
    {
        var (session, daemon, _) = Make();
        using var _s = session;
        await session.ReloadAsync();
        await session.ApplyEphemeralAsync(SettingsFor("PerAppSnapshot"));

        await session.ClearEphemeralOverrideAsync();

        Assert.False(session.HasEphemeralOverride);
        Assert.Equal("Baseline", FirstTablet(daemon.Settings));      // daemon restored
        Assert.Equal("Baseline", FirstTablet(session.CurrentSettings));

        // Reading resumes: an external edit now reaches the editor again.
        daemon.Settings = SettingsFor("ChangedElsewhere");
        await session.ReloadAsync();
        Assert.Equal("ChangedElsewhere", FirstTablet(session.CurrentSettings));
    }

    /// <summary>A real apply supersedes an override, so the guard must not outlive it.</summary>
    [AvaloniaFact]
    public async Task ARealApplyDuringAnOverride_EndsTheOverride()
    {
        var (session, _, _) = Make();
        using var _s = session;
        await session.ReloadAsync();
        await session.ApplyEphemeralAsync(SettingsFor("PerAppSnapshot"));

        await session.ApplyAndSaveSettingsAsync(SettingsFor("UserEdit"));

        Assert.False(session.HasEphemeralOverride);
        Assert.Equal("UserEdit", FirstTablet(session.CurrentSettings));
    }

    // --- Apply outcomes (#734), now observable end to end ---

    [AvaloniaFact]
    public async Task AnApply_ReachesTheDaemonAndTheDisk()
    {
        var (session, daemon, store) = Make();
        using var _s = session;
        daemon.AppInfo = new AppInfo { AppDataDirectory = "x", SettingsFile = "settings.json" };
        await session.ReloadAsync();

        var outcome = await session.ApplyAndSaveSettingsAsync(SettingsFor("Edited"));

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, outcome.Status);
        Assert.Contains(daemon.Applied, s => FirstTablet(s) == "Edited");
        Assert.Contains(store.Saved, s => FirstTablet(s) == "Edited");
    }

    /// <summary>Applied but not persisted: live now, gone on the next daemon restart. The artist has to
    /// be told, and the save chip must say the right one of the two things.</summary>
    [AvaloniaFact]
    public async Task AnApplyThatCannotBeSaved_IsAppliedButNotSaved()
    {
        var (session, daemon, store) = Make();
        using var _s = session;
        daemon.AppInfo = new AppInfo { AppDataDirectory = "x", SettingsFile = "settings.json" };
        await session.ReloadAsync();
        store.SaveSucceeds = false;

        var outcome = await session.ApplyAndSaveSettingsAsync(SettingsFor("Edited"));

        Assert.Equal(SettingsApplyStatus.AppliedNotSaved, outcome.Status);
        Assert.True(outcome.IsLive);
        Assert.False(outcome.IsPersisted);
        Assert.True(session.SaveFailed);
        Assert.Contains(daemon.Applied, s => FirstTablet(s) == "Edited");
    }

    /// <summary>With no transport the change was never sent, so nothing may claim it is live.</summary>
    [AvaloniaFact]
    public async Task AnApplyWithNoTransport_IsDisconnected_AndNotLive()
    {
        var (session, daemon, _) = Make();
        using var _s = session;
        await session.ReloadAsync();
        daemon.SetSettingsSucceeds = false;

        var outcome = await session.ApplyAndSaveSettingsAsync(SettingsFor("Edited"));

        Assert.Equal(SettingsApplyStatus.Disconnected, outcome.Status);
        Assert.False(outcome.IsLive);
        Assert.Contains("Not connected", session.SaveStatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The persistence-only retry: the daemon already has it, only the file is behind.</summary>
    [AvaloniaFact]
    public async Task RetryPersist_WritesTheChangeTheDaemonAlreadyTook()
    {
        var (session, daemon, store) = Make();
        using var _s = session;
        daemon.AppInfo = new AppInfo { AppDataDirectory = "x", SettingsFile = "settings.json" };
        await session.ReloadAsync();

        store.SaveSucceeds = false;
        await session.ApplyAndSaveSettingsAsync(SettingsFor("Edited"));
        Assert.Empty(store.Saved);

        store.SaveSucceeds = true;
        var retry = await session.RetryPersistAsync();

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, retry.Status);
        Assert.Contains(store.Saved, s => FirstTablet(s) == "Edited");
    }

    /// <summary>
    /// The no-op guard must not swallow an unsaved change. Before #734 the baseline was the last
    /// daemon-LOADED settings, so a failed write was recorded as the baseline on reload and re-applying
    /// the identical settings returned early — persistence was never retried.
    /// </summary>
    [AvaloniaFact]
    public async Task ReapplyingAfterAFailedSave_IsNotSwallowedByTheNoOpGuard()
    {
        var (session, daemon, store) = Make();
        using var _s = session;
        daemon.AppInfo = new AppInfo { AppDataDirectory = "x", SettingsFile = "settings.json" };
        await session.ReloadAsync();

        store.SaveSucceeds = false;
        await session.ApplyAndSaveSettingsAsync(SettingsFor("Edited"));

        store.SaveSucceeds = true;
        var again = await session.ApplyAndSaveSettingsAsync(SettingsFor("Edited"));

        Assert.NotEqual(SettingsApplyStatus.NoChange, again.Status);
        Assert.Contains(store.Saved, s => FirstTablet(s) == "Edited");
    }

    // --- Restore (#734) ---

    [AvaloniaFact]
    public async Task RestoringWithNoReadableSettingsFile_ReportsSourceUnavailable()
    {
        var (session, _, store) = Make();
        using var _s = session;
        await session.ReloadAsync();
        store.ToLoad = null;   // nothing on disk to restore from

        var outcome = await session.RestoreDefaultAsync();

        Assert.Equal(SettingsRestoreStatus.SourceUnavailable, outcome.Status);
        Assert.False(outcome.IsRestored);
    }

    [AvaloniaFact]
    public async Task RestoringReadsTheSavedDefaultFromDisk_AndAppliesIt()
    {
        var (session, daemon, store) = Make();
        using var _s = session;
        daemon.AppInfo = new AppInfo { AppDataDirectory = "x", SettingsFile = "settings.json" };
        await session.ReloadAsync();
        store.ToLoad = SettingsFor("SavedDefault");

        var outcome = await session.RestoreDefaultAsync();

        Assert.True(outcome.IsRestored);
        Assert.Equal("SavedDefault", FirstTablet(daemon.Settings));
        Assert.Equal("SavedDefault", FirstTablet(session.CurrentSettings));
    }
}
