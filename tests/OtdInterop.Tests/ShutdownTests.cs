using System;
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

        // Retrying is not reading, though -- it is work, and work is refused.
        Assert.Equal(SettingsApplyStatus.NoChange, (await settings.RetryPersistAsync()).Status);
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
