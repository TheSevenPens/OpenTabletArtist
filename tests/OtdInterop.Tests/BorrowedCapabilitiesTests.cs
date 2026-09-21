using System;
using System.Threading.Tasks;
using Xunit;

namespace OtdInterop.Tests;

/// <summary>
/// What a borrowed <see cref="IDaemonCapabilities"/> does once the session that lent it has gone (#828).
/// </summary>
///
/// <remarks>
/// <para>
/// Disposing a session does not take this back: a host that kept a reference — a page, a view model, a
/// tray menu — can still call every member. That had no decided answer, so it got whatever the transport
/// happened to do, which was to throw: <c>DaemonClient</c> guards every send on "no channel", and
/// disposing left it holding a <em>disposed</em> channel, which passes that guard.
/// </para>
/// <para>
/// The decision is that it reports not connected, which is what these members already document for a
/// session with no daemon. A host should not need to know whether the session it is holding has been
/// disposed in order to know how to read the result.
/// </para>
/// </remarks>
public class BorrowedCapabilitiesTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Every read reports not connected, rather than throwing.
    /// </summary>
    [Fact]
    public async Task AfterTheSessionIsDisposed_EveryReadReportsNotConnected()
    {
        var (session, daemon) = Make();

        // Answering before, so "null" afterwards cannot be the fake having nothing to say.
        Assert.NotNull(await session.Capabilities.GetAppInfoAsync());
        Assert.NotEmpty(await session.Capabilities.GetTabletsAsync());

        session.Dispose();

        Assert.Null(await session.Capabilities.GetAppInfoAsync());
        Assert.Empty(await session.Capabilities.GetTabletsAsync());
        Assert.Empty(await session.Capabilities.GetDevicesAsync());
        Assert.Empty(await session.Capabilities.GetCurrentLogAsync());
        Assert.False(await session.Capabilities.UninstallPluginAsync("anywhere"));

        await session.Capabilities.SetTabletDebugAsync(true);
        await session.Capabilities.LoadPluginsAsync();

        // And none of it reached the connection that has gone.
        Assert.DoesNotContain(daemon.Calls, c => c == nameof(daemon.GetDevicesAsync));
    }

    /// <summary>
    /// The same answers after an asynchronous close, which is the exit the application actually takes.
    /// </summary>
    [Fact]
    public async Task AfterAnAsynchronousClose_TheAnswersAreTheSame()
    {
        var (session, _) = Make();

        Assert.True(await session.CloseAsync(TimeSpan.FromSeconds(5))
            .WaitAsync(Bound, TestContext.Current.CancellationToken));

        Assert.Null(await session.Capabilities.GetAppInfoAsync());
        Assert.Empty(await session.Capabilities.GetTabletsAsync());
    }

    /// <summary>
    /// Subscribing afterwards attaches nothing.
    /// </summary>
    /// <remarks>
    /// Nothing is left to raise it, so attaching would only keep the handler — and whatever it closes
    /// over — alive against a connection that has gone.
    /// </remarks>
    [Fact]
    public void AfterDisposal_SubscribingAttachesNothing()
    {
        var (session, daemon) = Make();

        session.Dispose();

        var raised = 0;
        void Handler() => raised++;

        session.Capabilities.TabletsChanged += Handler;
        daemon.RaiseTabletsChanged();

        Assert.Equal(0, raised);
    }

    /// <summary>
    /// Unsubscribing keeps working, so a host can always let go.
    /// </summary>
    /// <remarks>
    /// The half that must not be refused. A host tears down in an order this library did not choose, and
    /// one that detaches after disposing must not be met with an exception — or with a handler it can no
    /// longer remove.
    /// </remarks>
    [Fact]
    public void AfterDisposal_UnsubscribingStillWorks()
    {
        var (session, daemon) = Make();

        var raised = 0;
        void Handler() => raised++;

        // Attached while the session was alive, so there is really something to detach.
        session.Capabilities.TabletsChanged += Handler;
        daemon.RaiseTabletsChanged();
        Assert.Equal(1, raised);

        session.Dispose();

        session.Capabilities.TabletsChanged -= Handler;

        daemon.RaiseTabletsChanged();
        Assert.Equal(1, raised);
    }

    // --- harness --------------------------------------------------------------------------------

    private static (OtdSession, FakeDaemonTransport) Make()
    {
        var daemon = new FakeDaemonTransport { ServerProcessId = 1 };
        daemon.Tablets.Add(new Newtonsoft.Json.Linq.JObject());

        var session = OtdSession.ForTesting(daemon, store: null, NullOtdLog.Instance,
            new FakeProcessLocator());
        daemon.Reconnect();

        return (session, daemon);
    }
}
