using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using Xunit;

namespace OtdInterop.Tests;

/// <summary>
/// What a host can do from inside the calls this coordinator makes to it (#845).
/// </summary>
///
/// <remarks>
/// <para>
/// The rule, from #843 and #844: a call into host code may reenter the session, dispose it, or supersede
/// the operation being handled, and nothing established before such a call authorises a mutation after it
/// without being reconsidered. Those two found the same defect in three disguises on the transition path.
/// This is the same audit applied to the settings operations.
/// </para>
/// <para>
/// The seams are <c>_onSaveState</c>, <c>_log</c>, <c>_store</c>, <c>_policy</c>, <c>_isOwnedDaemon</c>
/// and — added after #845 was written — <c>_rediscoverDestination</c>. The save-state callback is the
/// one with real hosts behind it: OTA updates a chip from it, and a host that opens a dialog there pumps
/// its dispatcher, which lets this session's own identification run.
/// </para>
/// </remarks>
public class ReentrancyAuditTests
{
    /// <summary>
    /// A retry that is superseded while announcing does not write the change anyway.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one site in the class that announced and then changed state without re-establishing that the
    /// session was still the one it started in. <c>RetryPersistCoreAsync</c> says "Saving", which runs
    /// host code, and then writes to disk and records what it wrote — with no <c>StillCurrent</c> between
    /// them. Every other operation re-checks after its own call-outs; the apply path checks twice.
    /// </para>
    /// <para>
    /// If a daemon change lands in that gap, the reset has already discarded the pending change and
    /// cleared the comparison baselines — and the retry then writes the departed daemon's settings and
    /// records them as what disk holds for the session that replaced it. The next identical apply is
    /// skipped as a no-op against a baseline no daemon ever had.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ARetrySupersededWhileAnnouncing_DoesNotWriteTheChangeAnyway()
    {
        var daemon = new FakeDaemonTransport { ServerProcessId = 1, Settings = Tablet("Baseline") };
        var locator = new FakeProcessLocator { Path = "A/OpenTabletDriver.Daemon.exe" };
        var store = new SwitchableStore { Succeeds = false };

        using var session = OtdSession.ForTesting(daemon, store, NullOtdLog.Instance,
            NoPolicy.Instance, locator);

        // The host's save-state callback IS the seam, so it is what reacts. Declared before the session
        // exists because that is the only way in: it is a constructor-time delegate, not an event a test
        // can attach to later -- which is itself worth noting, since it means every host has one.
        var reported = new List<SettingsSaveState>();
        Action<SettingsSaveState>? react = null;
        var settings = session.OpenSettings(() => true, state =>
        {
            reported.Add(state);
            react?.Invoke(state);
        });

        daemon.Reconnect();
        await settings.ReloadFromDaemonAsync();

        // An edit the daemon took and the disk refused: live, unsaved, retryable.
        Assert.Equal(SettingsApplyStatus.AppliedNotSaved,
            (await settings.ApplyAndSaveAsync(Tablet("Edited"))).Status);

        store.Succeeds = true;
        store.Wrote.Clear();

        // The host, from inside the "Saving" announcement, does what a dialog would let happen: this
        // session identifies a different daemon and resets. Nothing here is exotic to the library --
        // identification deliberately does not wait behind the mutation gate (#828).
        var switched = false;
        react = state =>
        {
            if (state != SettingsSaveState.Saving || switched) return;

            switched = true;
            locator.Path = "B/OpenTabletDriver.Daemon.exe";
            daemon.Reconnect();
        };

        var retry = await settings.RetryPersistAsync();

        Assert.True(switched, "the callback never ran, so this proves nothing");
        Assert.Empty(store.Wrote);
        Assert.NotEqual(SettingsApplyStatus.AppliedAndSaved, retry.Status);

        // And the comparison baseline does not claim the new daemon's disk holds the old one's settings.
        Assert.Equal(SettingsApplyStatus.AppliedAndSaved,
            (await settings.ApplyAndSaveAsync(Tablet("Edited"))).Status);
    }

    /// <summary>
    /// An apply superseded while the disk write is running does not record its outcome anyway.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same defect as the retry's, in the operation next door, and found by scanning rather than by
    /// reading: <c>_store</c> is the host's disk, so <c>TrySave</c> is a call into host code, and the
    /// pending-change and comparison bookkeeping after it was all decided before it ran.
    /// </para>
    /// <para>
    /// The write stands — it went to the file the daemon that accepted it reported. What must not stand
    /// is a comparison baseline claiming the <em>new</em> daemon's disk holds the old one's settings,
    /// which would make the next genuine apply look like a no-op.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnApplySupersededWhileWriting_DoesNotRecordItsOutcomeAnyway()
    {
        var daemon = new FakeDaemonTransport { ServerProcessId = 1, Settings = Tablet("Baseline") };
        var locator = new FakeProcessLocator { Path = "A/OpenTabletDriver.Daemon.exe" };
        var store = new SwitchableStore();

        using var session = OtdSession.ForTesting(daemon, store, NullOtdLog.Instance,
            NoPolicy.Instance, locator);
        var settings = session.OpenSettings(() => true, _ => { });

        daemon.Reconnect();
        await settings.ReloadFromDaemonAsync();

        // The host's disk, doing what a host's disk may do: reaching back into the session.
        var switched = false;
        store.WhileWriting = () =>
        {
            if (switched) return;

            switched = true;
            locator.Path = "B/OpenTabletDriver.Daemon.exe";
            daemon.Reconnect();
        };

        var outcome = await settings.ApplyAndSaveAsync(Tablet("Edited"));

        Assert.True(switched, "the store callback never ran, so this proves nothing");
        Assert.Equal(SettingsApplyStatus.Superseded, outcome.Status);

        // The next genuine apply is not skipped as a no-op against a baseline the new daemon never had.
        store.WhileWriting = null;
        Assert.Equal(SettingsApplyStatus.AppliedAndSaved,
            (await settings.ApplyAndSaveAsync(Tablet("Edited"))).Status);
    }

    /// <summary>
    /// A host that disposes before the send gets no apply, no write, and no later report.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This test used to claim the opposite</b>, and its explanation described a scenario it was not
    /// in: it disposed from <c>Saving</c>, which is emitted <em>before</em> <c>SetSettingsAsync</c>, and
    /// then said the destination belonged to "the daemon that accepted the change". Nothing had accepted
    /// anything yet.
    /// </para>
    /// <para>
    /// It passed because the fake's binding checked incarnation alone and ignored disposal, so it
    /// accepted a send the real client's binding refuses on <c>rpc.IsDisposed</c>. The fake models
    /// disposal now, and the honest answer is this one.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AHostDisposingBeforeTheSend_GetsNoApplyAndNoWrite()
    {
        var daemon = new FakeDaemonTransport { ServerProcessId = 1, Settings = Tablet("Baseline") };
        var store = new SwitchableStore();
        var session = OtdSession.ForTesting(daemon, store, NullOtdLog.Instance,
            NoPolicy.Instance, new FakeProcessLocator { Path = "A/OpenTabletDriver.Daemon.exe" });

        var reported = new List<SettingsSaveState>();
        Action<SettingsSaveState>? react = null;
        var settings = session.OpenSettings(() => true, state =>
        {
            reported.Add(state);
            react?.Invoke(state);
        });

        daemon.Reconnect();
        await settings.ReloadFromDaemonAsync();

        var before = settings.GetCurrent()!;

        var disposed = false;
        react = state =>
        {
            // Saving is announced before the send, so this disposes with nothing yet accepted.
            if (state != SettingsSaveState.Saving || disposed) return;

            disposed = true;
            session.Dispose();
        };

        var outcome = await settings.ApplyAndSaveAsync(Tablet("Edited"));

        Assert.True(disposed, "the callback never disposed, so this proves nothing");
        Assert.Equal(SettingsApplyStatus.Disconnected, outcome.Status);
        Assert.Null(outcome.Prepared);
        Assert.Empty(store.Wrote);

        // Nothing was published, and nothing was reported after the host tore down.
        Assert.Equal(before.Stamp, settings.GetCurrent()!.Stamp);
        Assert.Equal(SettingsSaveState.Saving, reported[^1]);
    }

    /// <summary>
    /// A host that disposes after the daemon has accepted lets that work finish, and hears nothing more.
    /// </summary>
    /// <remarks>
    /// The post-acceptance half, tested at an actual post-acceptance point: the disk write, which runs
    /// after the send succeeded and after the revision was published. Work already admitted and accepted
    /// completes (#859); the reporting stops, because a host that has torn down is not called back into;
    /// and what happened stays readable (#873).
    /// </remarks>
    [Fact]
    public async Task AHostDisposingAfterAcceptance_LetsTheAcceptedWorkFinishAndHearsNothingMore()
    {
        var daemon = new FakeDaemonTransport { ServerProcessId = 1, Settings = Tablet("Baseline") };
        var store = new SwitchableStore();
        var session = OtdSession.ForTesting(daemon, store, NullOtdLog.Instance,
            NoPolicy.Instance, new FakeProcessLocator { Path = "A/OpenTabletDriver.Daemon.exe" });

        var reported = new List<SettingsSaveState>();
        var settings = session.OpenSettings(() => true, reported.Add);

        daemon.Reconnect();
        await settings.ReloadFromDaemonAsync();

        var disposed = false;
        store.WhileWriting = () =>
        {
            if (disposed) return;

            disposed = true;
            session.Dispose();
        };

        var outcome = await settings.ApplyAndSaveAsync(Tablet("Edited"));

        Assert.True(disposed, "the store callback never disposed, so this proves nothing");
        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, outcome.Status);
        Assert.Single(store.Wrote);

        Assert.Equal(SettingsSaveState.Saving, reported[^1]);
        Assert.Equal("Edited", settings.GetCurrent()!.Settings.Profiles[0].Tablet);
    }

    /// <summary>
    /// A failure logger that supersedes the operation does not then get its report, or its result.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The gap my audit missed, and the reason its reasoning was wrong rather than merely incomplete: I
    /// concluded the loggers were safe because the operation's own state had settled by the time they
    /// ran. State is not the only thing a call-out can outlive. This logger runs after the write has
    /// failed, and after it come the save-state report and an adoptable result — both of which speak for
    /// an operation that no longer exists.
    /// </para>
    /// <para>
    /// Left alone, the superseded apply writes "Failed" over the "None" the reset had just reported,
    /// about a daemon the user has already moved off, and hands back a <c>Prepared</c> revision that
    /// OTA's editor adopts on the strength of the status alone.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AFailureLoggerThatSupersedes_GetsNeitherTheReportNorTheResult()
    {
        var daemon = new FakeDaemonTransport { ServerProcessId = 1, Settings = Tablet("Baseline") };
        var locator = new FakeProcessLocator { Path = "A/OpenTabletDriver.Daemon.exe" };
        var store = new SwitchableStore { Succeeds = false };

        var log = new ReactingLog();
        using var session = OtdSession.ForTesting(daemon, store, log, NoPolicy.Instance, locator);

        var reported = new List<SettingsSaveState>();
        var settings = session.OpenSettings(() => true, reported.Add);

        daemon.Reconnect();
        await settings.ReloadFromDaemonAsync();

        var switched = false;
        log.OnWarn = message =>
        {
            if (switched || !message.StartsWith("Settings applied but not saved:", StringComparison.Ordinal))
                return;

            switched = true;
            locator.Path = "B/OpenTabletDriver.Daemon.exe";
            daemon.Reconnect();
        };

        var outcome = await settings.ApplyAndSaveAsync(Tablet("Edited"));

        Assert.True(switched, "the logger never superseded, so this proves nothing");

        // The reset's own announcement is the last word, not the superseded apply's.
        Assert.Equal(SettingsSaveState.None, reported[^1]);

        Assert.Equal(SettingsApplyStatus.Superseded, outcome.Status);
        Assert.Null(outcome.Prepared);
    }

    // --- harness --------------------------------------------------------------------------------

    /// <summary>A log the test can act from, because logging is a call into host code.</summary>
    private sealed class ReactingLog : IOtdLog
    {
        public Action<string>? OnWarn { get; set; }

        public void Warn(string message, Exception? error = null) => OnWarn?.Invoke(message);

        public void Info(string message) { }

        public void Debug(string message, Exception? error = null) { }
    }

    private sealed class SwitchableStore : ISettingsFileStore
    {
        public bool Succeeds { get; set; } = true;

        public List<string> Wrote { get; } = [];

        /// <summary>What the host's disk does while it is being written to.</summary>
        public Action? WhileWriting { get; set; }

        public void Save(Settings settings, string path) => Wrote.Add(path);

        public bool TrySave(Settings settings, string path)
        {
            WhileWriting?.Invoke();
            if (!Succeeds) return false;

            Wrote.Add(path);
            return true;
        }

        public bool TryLoad(string path, out Settings? settings)
        {
            settings = null;
            return false;
        }
    }

    private static Settings Tablet(string name) =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = name } } };
}
