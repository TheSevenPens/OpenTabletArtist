using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The "Saved" chip goes away on its own.
/// </summary>
///
/// <remarks>
/// <para>
/// It stopped doing so, and the way it broke is the reason this test exists at the session level rather
/// than around the timer. The coordinator awaits the daemon with <c>ConfigureAwait(false)</c>, so the
/// report that sets <see cref="AppSession.SaveState"/> arrives on a thread-pool thread. The chip still
/// appeared, because the property is observable and Avalonia tolerated the cross-thread set; but the
/// auto-clear timer is built on first use, and a <c>DispatcherTimer</c> constructed on a pool thread
/// belongs to a dispatcher that never pumps. It reported itself enabled and never ticked, so the chip
/// stayed up for the rest of the session.
/// </para>
/// <para>
/// Nothing about that shows up as a failure: no exception, no warning, and a timer that says it is
/// running. So the test drives the real apply path end to end and waits for the chip to clear by itself,
/// which is the only observation that would have caught it.
/// </para>
/// </remarks>
public class SaveChipClearsTests
{
    [AvaloniaFact]
    public async Task AfterASave_TheChipClearsItself()
    {
        var (daemon, session) = Connected();
        using var _s = session;

        await Settled(session, daemon);

        var edit = Clone(daemon.Settings!);
        edit.Profiles[0].BindingSettings.DisablePressure = true;
        await session.ApplyAndSaveSettingsAsync(edit);

        // Pumped for, not asserted outright: the report is posted to the UI thread, so it lands on the
        // next turn of the dispatcher rather than inside the await.
        await PumpUntil(() => session.SaveState == SettingsSaveState.Saved, "the chip to say Saved");
        Assert.True(session.ShowSaveStatus, "the chip should be up once the save is reported");

        await PumpUntil(() => session.SaveState == SettingsSaveState.None,
                        "the saved chip to clear itself");

        Assert.False(session.ShowSaveStatus);
    }

    /// <summary>And every report reaches the session on the UI thread.</summary>
    /// <remarks>
    /// The direct statement of the cause, separate from the symptom above, because the symptom would
    /// start failing again for reasons that have nothing to do with threads — a longer interval, a
    /// different clearing rule — and this says which one it is.
    /// </remarks>
    [AvaloniaFact]
    public async Task EverySaveStateReport_ArrivesOnTheUiThread()
    {
        var (daemon, session) = Connected();
        using var _s = session;

        var offThread = new List<string>();
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSession.SaveState) && !Dispatcher.UIThread.CheckAccess())
                offThread.Add($"{session.SaveState} on thread {Environment.CurrentManagedThreadId}");
        };

        await Settled(session, daemon);

        var edit = Clone(daemon.Settings!);
        edit.Profiles[0].BindingSettings.DisablePressure = true;
        await session.ApplyAndSaveSettingsAsync(edit);
        await PumpUntil(() => session.SaveState == SettingsSaveState.Saved, "the chip to say Saved");

        // Stops here rather than waiting for the chip to clear. Waiting would make this fail with a
        // timeout when the reports come in off-thread — true, but it would be reporting the symptom the
        // other test owns instead of the cause this one is about.
        Assert.Empty(offThread);
    }

    private static (FakeDaemonTransport Daemon, AppSession Session) Connected()
    {
        var daemon = new FakeDaemonTransport
        {
            Settings = SettingsFor("T"),
            // An absolute path, so the coordinator has somewhere to write and reports Saved rather than
            // "nowhere to write". Nothing is actually written: the store below is a no-op. Temp rather
            // than a drive letter, since this suite also runs on the Linux and macOS legs.
            AppInfo = new AppInfo
            {
                AppDataDirectory = System.IO.Path.GetTempPath(),
                SettingsFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ota-save-chip-test.json"),
                PluginDirectory = "",
            },
        };
        // Answers asynchronously, which is the whole point. A fake that returns Task.FromResult completes
        // synchronously, so `await ... .ConfigureAwait(false)` continues inline on the caller's thread and
        // no hop ever happens — these tests passed against the bug until this was added. A real daemon is
        // a pipe: its reply arrives on a pool thread and the continuation stays there, which is what
        // stranded the chip's timer.
        daemon.GetSettingsHandler = async () =>
        {
            await Task.Yield();
            return daemon.Settings;
        };

        var session = new AppSession(FakeSession.Over(daemon, new NoopStore()), new StubLifecycle())
        {
            Ownership = DaemonOwnership.Owned,
        };
        return (daemon, session);
    }

    private static Settings SettingsFor(string tablet)
    {
        var profile = new Profile { Tablet = tablet };
        profile.BindingSettings.WheelBindings.Add(new WheelBindingSettings());
        return new Settings { Profiles = new ProfileCollection { profile } };
    }

    private static Settings Clone(Settings s) =>
        Newtonsoft.Json.JsonConvert.DeserializeObject<Settings>(
            Newtonsoft.Json.JsonConvert.SerializeObject(s))!;

    /// <summary>
    /// Loads, and lets the session finish asking the daemon where its settings live.
    /// </summary>
    /// <remarks>
    /// That lookup is started at construction and completes on the host's context, so a save attempted
    /// before the dispatcher has run it reports "nowhere to write" — which is a real outcome, just not
    /// the one these tests are about.
    /// </remarks>
    private static async Task Settled(AppSession session, FakeDaemonTransport daemon)
    {
        // The fake starts on incarnation 0, which reads as "no connection" — so the session never asks
        // where to write and every save reports "nowhere to go". Connecting it is what makes a save a
        // save.
        daemon.Reconnect();
        await session.ReloadAsync();
        var until = DateTime.UtcNow + TimeSpan.FromMilliseconds(400);
        while (DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Pumps until <paramref name="until"/> holds, or fails. The chip's timer is 2.5s.</summary>
    private static async Task PumpUntil(Func<bool> until, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (!until())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for {what}.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class NoopStore : ISettingsFileStore
    {
        public void Save(Settings settings, string path) { }
        public bool TrySave(Settings settings, string path) => true;
        public bool TryLoad(string path, out Settings? settings) { settings = null; return false; }
    }

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
        public string? PathOf(int processId) => null;
        public string? SingleRunningDaemonPath() => null;
    }
}
