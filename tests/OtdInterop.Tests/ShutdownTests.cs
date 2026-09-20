using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using Xunit;

namespace OtdInterop.Tests;

/// <summary>
/// What closing a session does to work that is already running (#828).
/// </summary>
///
/// <remarks>
/// <para>
/// <c>Dispose</c> has always said what it is not: it closes the connection and stops the session issuing
/// new work, and operations already in flight are "not awaited, cancelled or settled". That honesty was
/// the right thing to write down and a poor thing to leave true. A host tearing down while an apply is
/// mid-flight disposed the transport underneath it, so a write that had reached the daemon could fail on
/// the way to disk with nothing able to say whether it landed.
/// </para>
/// <para>
/// The shape is the one the switch-check pump arrived at for the same question: refuse new work, let
/// what was admitted finish, then close. A bounded wait, because a daemon that never answers must not
/// stop an application exiting.
/// </para>
/// </remarks>
public class ShutdownTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Closing waits for an operation that is already running, rather than disposing under it.
    /// </summary>
    [Fact]
    public async Task ClosingWaitsForWorkAlreadyInFlight()
    {
        var (session, settings, daemon, _) = Make();

        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ =>
        {
            sending.TrySetResult();
            return held.Task;
        };

        var apply = settings.ApplyAndSaveAsync(Tablet("Mid-flight"));
        await sending.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        var closing = session.CloseAsync();

        // Still running, because the apply is. Disposing here is what used to pull the transport out
        // from under it.
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.False(closing.IsCompleted);
        Assert.False(daemon.IsDisposed);

        held.SetResult(true);

        await closing.WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, (await apply).Status);
        Assert.True(daemon.IsDisposed);
    }

    /// <summary>
    /// A daemon that never answers does not stop the session closing.
    /// </summary>
    /// <remarks>
    /// The other half, and the reason the wait is bounded. An application exiting cannot be held open by
    /// a daemon that has stopped responding, so the wait gives up and says so rather than settling.
    /// </remarks>
    [Fact]
    public async Task ADaemonThatNeverAnswers_DoesNotHoldTheSessionOpen()
    {
        var (session, settings, daemon, _) = Make();

        var never = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ =>
        {
            sending.TrySetResult();
            return never.Task;
        };

        _ = settings.ApplyAndSaveAsync(Tablet("Never answered"));
        await sending.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        var settled = await session.CloseAsync(TimeSpan.FromMilliseconds(200))
            .WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.False(settled);                  // it gave up rather than settling
        Assert.True(daemon.IsDisposed);         // and closed anyway
    }

    /// <summary>
    /// Work offered after closing is refused, rather than sent into a connection that is going away.
    /// </summary>
    [Fact]
    public async Task WorkOfferedAfterClosing_IsRefused()
    {
        var (session, settings, daemon, _) = Make();

        Assert.True(await session.CloseAsync().WaitAsync(Bound, TestContext.Current.CancellationToken));

        daemon.Applied.Clear();
        var outcome = await settings.ApplyAndSaveAsync(Tablet("Too late"));

        Assert.Equal(SettingsApplyStatus.Disconnected, outcome.Status);
        Assert.Empty(daemon.Applied);
    }

    /// <summary>
    /// And what already happened can still be asked about.
    /// </summary>
    /// <remarks>
    /// Deliberate, and older than this change: a host asking after teardown whether a change went unsaved
    /// should get the truth rather than an exception. Reading what happened is allowed; starting
    /// something new is what is refused.
    /// </remarks>
    [Fact]
    public async Task AfterClosing_WhatAlreadyHappenedIsStillReadable()
    {
        var (session, settings, daemon, store) = Make();

        store.SaveSucceeds = false;
        Assert.Equal(SettingsApplyStatus.AppliedNotSaved,
            (await settings.ApplyAndSaveAsync(Tablet("Unsaved"))).Status);

        await session.CloseAsync().WaitAsync(Bound, TestContext.Current.CancellationToken);

        // Reading what happened still works: the settings are there to look at, and nothing throws at a
        // host that is tidying up.
        Assert.Equal("Unsaved", Tablet(settings.GetCurrent()?.Settings));

        // Retrying is not reading, though -- it is work, and work on a closed session is refused as not
        // connected, which is what it is.
        Assert.Equal(SettingsApplyStatus.Disconnected, (await settings.RetryPersistAsync()).Status);
    }

    /// <summary>
    /// Closing again while the first close is still waiting does not report success.
    /// </summary>
    /// <remarks>
    /// The flag that stops new work was also doing duty as "this is closed", so any later caller was told
    /// true immediately — conflating <em>closing</em>, <em>closed after giving up</em> and <em>everything
    /// settled</em>. A host with two teardown paths would have had one of them told the work had
    /// finished while it was still running.
    /// </remarks>
    [Fact]
    public async Task ClosingTwiceWhileWorkIsStillRunning_DoesNotReportSuccess()
    {
        var (session, settings, daemon, _) = Make();

        var never = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ =>
        {
            sending.TrySetResult();
            return never.Task;
        };

        _ = settings.ApplyAndSaveAsync(Tablet("Never answered"));
        await sending.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.False(await session.CloseAsync(TimeSpan.Zero)
            .WaitAsync(Bound, TestContext.Current.CancellationToken));

        // Same question, same answer: the work it gave up on is still running.
        Assert.False(await session.CloseAsync(TimeSpan.Zero)
            .WaitAsync(Bound, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A read still in flight is work, and closing waits for it.
    /// </summary>
    /// <remarks>
    /// The wait was the mutation gate, which reads do not take — so a session with a reload outstanding
    /// reported that everything had settled, and the read's continuation could run afterwards. "True when
    /// everything in flight finished" has to mean every operation, not every operation that mutates.
    /// </remarks>
    [Fact]
    public async Task ClosingWaitsForAReadToo()
    {
        var (session, settings, daemon, _) = Make();

        var never = new TaskCompletionSource<Settings?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.GetSettingsHandler = () =>
        {
            reading.TrySetResult();
            return never.Task;
        };

        _ = settings.ReloadFromDaemonAsync();
        await reading.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.False(await session.CloseAsync(TimeSpan.Zero)
            .WaitAsync(Bound, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Work offered to a closed session is refused at once, not after whatever is stuck finishes.
    /// </summary>
    /// <remarks>
    /// The refusal was inside the mutation gate, so a caller arriving after a close that had already
    /// given up queued behind the stuck operation and waited for a daemon that was never going to answer.
    /// It was refused in the end, which is not the same as being refused.
    /// </remarks>
    [Fact]
    public async Task WorkOfferedToAClosedSession_IsRefusedWithoutWaiting()
    {
        var (session, settings, daemon, _) = Make();

        var never = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ =>
        {
            sending.TrySetResult();
            return never.Task;
        };

        _ = settings.ApplyAndSaveAsync(Tablet("Stuck"));
        await sending.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        await session.CloseAsync(TimeSpan.Zero).WaitAsync(Bound, TestContext.Current.CancellationToken);

        // Promptly: the stuck operation still holds the gate and always will.
        var refused = await settings.ApplyAndSaveAsync(Tablet("Too late"))
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(SettingsApplyStatus.Disconnected, refused.Status);
    }

    /// <summary>Restoring on a closed session says it is not connected, like everything else.</summary>
    /// <remarks>
    /// I claimed this outcome did not exist and that inventing one was more API than the situation
    /// deserved. <c>SettingsRestoreOutcome.Disconnected</c> was already there; I asserted its absence
    /// without looking.
    /// </remarks>
    [Fact]
    public async Task RestoringOnAClosedSession_ReportsDisconnected()
    {
        var (session, settings, _, _) = Make();

        Assert.True(await session.CloseAsync().WaitAsync(Bound, TestContext.Current.CancellationToken));

        Assert.Equal(SettingsRestoreStatus.Disconnected, (await settings.RestoreDefaultAsync()).Status);
    }

    /// <summary>
    /// Work the close gave up on does not call back into the host afterwards.
    /// </summary>
    /// <remarks>
    /// The limit of a bounded close: an operation that outlives the window is still running, and its
    /// daemon may answer long after the host has finished tearing down. It carries on into this session's
    /// state, which a host reading afterwards will see, and it stops there.
    ///
    /// Only after the close has <em>abandoned</em> it. Work that settles inside the window settled
    /// normally, and suppressing its report would hide a save that actually happened.
    /// </remarks>
    [Fact]
    public async Task WorkTheCloseGaveUpOn_DoesNotCallBackIntoTheHost()
    {
        var daemon = new FakeDaemonTransport { ServerProcessId = 1, Settings = new Settings() };
        var reported = new List<SettingsSaveState>();
        var session = OtdSession.ForTesting(daemon, new RecordingStore(), NullOtdLog.Instance,
            NoPolicy.Instance, new FakeProcessLocator());
        var settings = session.OpenSettings(() => true, reported.Add);
        daemon.Reconnect();

        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ =>
        {
            sending.TrySetResult();
            return held.Task;
        };

        var apply = settings.ApplyAndSaveAsync(Tablet("Outlives the window"));
        await sending.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.False(await session.CloseAsync(TimeSpan.Zero)
            .WaitAsync(Bound, TestContext.Current.CancellationToken));

        var before = reported.Count;
        held.SetResult(true);                       // the daemon answers, far too late
        await apply.WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.Equal(before, reported.Count);
    }

    /// <summary>
    /// A destination lookup the session started itself is work, and closing waits for it.
    /// </summary>
    /// <remarks>
    /// Registration covered the operations a host asks for and missed the one the session starts on its
    /// own account. <c>StillTheCurrentTransition</c> stops an obsolete answer being adopted; it does not
    /// settle the outstanding RPC, and the reply's continuation still posts through the host's execution
    /// context — the context a successful close has just told the caller it is safe to tear down.
    /// </remarks>
    [Fact]
    public async Task ClosingWaitsForADiscoveryTheSessionStarted()
    {
        var (session, _, daemon, _) = Make();

        var never = new TaskCompletionSource<AppInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var asking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.GetAppInfoHandler = () =>
        {
            asking.TrySetResult();
            return never.Task;
        };

        daemon.Reconnect();                         // a transition, and the lookup it starts
        await asking.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.False(await session.CloseAsync(TimeSpan.Zero)
            .WaitAsync(Bound, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Disposing during an asynchronous close tears the transport down rather than waiting on it.
    /// </summary>
    /// <remarks>
    /// I described this behaviour in the review and had it backwards. <c>RunCloseAsync</c> set the
    /// disposed flag first, so <c>Dispose</c> saw it and returned having done nothing — the transport
    /// stayed open until the drain finished. A host that gave up on a graceful close and disposed had no
    /// way to make anything happen.
    /// </remarks>
    [Fact]
    public async Task DisposingDuringAClose_TearsDownAtOnce()
    {
        var (session, settings, daemon, _) = Make();

        var never = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ =>
        {
            sending.TrySetResult();
            return never.Task;
        };

        _ = settings.ApplyAndSaveAsync(Tablet("Stuck"));
        await sending.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        var closing = session.CloseAsync(TimeSpan.FromMinutes(5));
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.False(closing.IsCompleted);

        session.Dispose();

        Assert.True(daemon.IsDisposed);

        // And it releases the close rather than leaving it to spend the five minutes waiting for work the
        // Dispose just abandoned. The held operation is deliberately never completed: the close has to
        // come back on the abandonment alone, and it does not claim the work settled gracefully.
        Assert.False(await closing.WaitAsync(Bound, TestContext.Current.CancellationToken));
        Assert.False(never.Task.IsCompleted);
    }

    /// <summary>
    /// A settings handle kept past <c>Dispose</c> cannot go on working.
    /// </summary>
    /// <remarks>
    /// Only the asynchronous close stopped admission, so the synchronous path — the one the application
    /// actually uses — left a retained handle able to write to disk after the session had been disposed.
    /// Both ways of closing have to mean the same thing about what may still run.
    /// </remarks>
    [Fact]
    public async Task ARetainedHandle_CannotWorkAfterDispose()
    {
        var (session, settings, _, store) = Make();

        store.SaveSucceeds = false;
        Assert.Equal(SettingsApplyStatus.AppliedNotSaved,
            (await settings.ApplyAndSaveAsync(Tablet("Pending"))).Status);

        session.Dispose();

        store.SaveSucceeds = true;
        Assert.Equal(SettingsApplyStatus.Disconnected, (await settings.RetryPersistAsync()).Status);
    }

    /// <summary>
    /// Closing after a <c>Dispose</c> answers at once, and does not claim the session settled.
    /// </summary>
    /// <remarks>
    /// The fourth corner of the lifecycle, after Dispose alone, Dispose during a close, and repeated
    /// closes. A host with both a window-close handler and an application-exit path can reach here, and
    /// "it closed anyway" is the honest answer: the transport went down under whatever was running.
    /// </remarks>
    [Fact]
    public async Task ClosingAfterDispose_AnswersAtOnceWithoutClaimingItSettled()
    {
        var (session, settings, daemon, _) = Make();

        var never = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ =>
        {
            sending.TrySetResult();
            return never.Task;
        };

        _ = settings.ApplyAndSaveAsync(Tablet("Stuck"));
        await sending.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        session.Dispose();

        // A generous window, so passing cannot mean "the wait expired": it has to answer without waiting.
        var closing = session.CloseAsync(TimeSpan.FromMinutes(5));
        Assert.False(await closing.WaitAsync(Bound, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A close waiting on a discovery alone is released by a <c>Dispose</c>, not left to time out.
    /// </summary>
    /// <remarks>
    /// The settings path reaches the same conclusion through its own abandonment, and would mask this:
    /// here no settings operation is running, so the lookup wait is the only thing holding the close.
    /// </remarks>
    [Fact]
    public async Task DisposingDuringACloseWaitingOnADiscovery_ReleasesIt()
    {
        var (session, _, daemon, _) = Make();

        var never = new TaskCompletionSource<AppInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var asking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.GetAppInfoHandler = () =>
        {
            asking.TrySetResult();
            return never.Task;
        };

        daemon.Reconnect();
        await asking.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        var closing = session.CloseAsync(TimeSpan.FromMinutes(5));
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.False(closing.IsCompleted);

        session.Dispose();

        Assert.False(await closing.WaitAsync(Bound, TestContext.Current.CancellationToken));
        Assert.False(never.Task.IsCompleted);
    }

    /// <summary>
    /// Closing after a <c>Dispose</c> that left a discovery outstanding still answers at once.
    /// </summary>
    /// <remarks>
    /// The other order, and it needs its own latch: the teardown's wake finds no one waiting, so a close
    /// arriving afterwards would create a fresh wait and hold for the whole window on a lookup that was
    /// abandoned with the transport.
    /// </remarks>
    [Fact]
    public async Task ClosingAfterADisposeThatLeftADiscoveryRunning_AnswersAtOnce()
    {
        var (session, _, daemon, _) = Make();

        var never = new TaskCompletionSource<AppInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var asking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.GetAppInfoHandler = () =>
        {
            asking.TrySetResult();
            return never.Task;
        };

        daemon.Reconnect();
        await asking.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        session.Dispose();

        var closing = session.CloseAsync(TimeSpan.FromMinutes(5));
        Assert.False(await closing.WaitAsync(Bound, TestContext.Current.CancellationToken));
    }

    // --- harness --------------------------------------------------------------------------------

    private static (OtdSession, IOtdSettingsSession, FakeDaemonTransport, RecordingStore) Make()
    {
        var daemon = new FakeDaemonTransport { ServerProcessId = 1, Settings = new Settings() };
        var store = new RecordingStore();
        var session = OtdSession.ForTesting(daemon, store, NullOtdLog.Instance,
            NoPolicy.Instance, new FakeProcessLocator());
        var settings = session.OpenSettings(() => true, _ => { });
        daemon.Reconnect();
        return (session, settings, daemon, store);
    }

    private sealed class RecordingStore : ISettingsFileStore
    {
        public bool SaveSucceeds { get; set; } = true;

        public void Save(Settings settings, string path) { }

        public bool TrySave(Settings settings, string path) => SaveSucceeds;

        public bool TryLoad(string path, out Settings? settings)
        {
            settings = null;
            return false;
        }
    }

    private static Settings Tablet(string name) =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = name } } };

    private static string Tablet(Settings? s) => s?.Profiles[0].Tablet ?? "";
}
