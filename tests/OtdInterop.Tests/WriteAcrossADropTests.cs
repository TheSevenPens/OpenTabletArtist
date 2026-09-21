using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using StreamJsonRpc;
using Xunit;

namespace OtdInterop.Tests;

/// <summary>
/// What a settings write does when the connection carrying it goes away — over a real pipe (#922).
/// </summary>
///
/// <remarks>
/// <para>
/// The rest of this protection is tested against <see cref="FakeDaemonTransport"/>, whose write is a
/// <c>TaskCompletionSource</c> that stays pending across a disconnect. The real client cannot do that:
/// losing the connection faults the task, because the response channel it was waiting on is gone, while
/// the server method carries on running. Reading a completed task as a finished write therefore cleared
/// the block on exactly the case it exists for, and every fake-backed test agreed that it was fine.
/// </para>
/// <para>
/// Nothing here contacts a running OpenTabletDriver. The daemon is a <see cref="JsonRpc"/> server this
/// test stands up on a pipe named for the run, serving <c>GetSettings</c>, <c>SetSettings</c> and
/// <c>GetApplicationInfo</c>; settings are persisted to memory. The pipe's server process is this test
/// process, which is what makes the "is it still the same driver" comparison answer yes across the
/// reconnect, as it would for a daemon that never restarted.
/// </para>
/// </remarks>
public class WriteAcrossADropTests
{
    /// <summary>
    /// A write still running in the daemon when the pipe dropped keeps editing refused after reconnect.
    /// </summary>
    /// <remarks>
    /// Codex's reproduction, at the seam where it matters: hold the daemon inside SetSettings, drop the
    /// connection, reconnect to the same process. Left unblocked, OTA would let the artist apply and save
    /// something else, and the held write would then land on top of it — the driver holding one document,
    /// the file and OTA's snapshot another, and nothing anywhere reporting a conflict.
    /// </remarks>
    [Fact]
    public async Task AWriteTheDaemonIsStillRunningWhenThePipeDrops_KeepsEditingRefused()
    {
        var name = $"ota-test-{Guid.NewGuid():N}";
        var info = new AppInfo { AppDataDirectory = @"C:\otd", SettingsFile = @"C:\otd\settings.json" };
        var live = SettingsSessionTests.Document();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var client = new DaemonClient(NullOtdLog.Instance, name);
        using var session = OtdSession.ForTesting(client, new MemorySettingsFileStore(),
            NullOtdLog.Instance, new FakeProcessLocator { Path = "daemon.exe", OnlyDaemon = "daemon.exe" });

        Task Write(Settings s) { arrived.TrySetResult(); return held.Task; }

        var first = new Daemon(name, () => live, Write, info);
        try
        {
            var ready = Reconnected(session);
            _ = client.ConnectAsync(cancel.Token);
            await ready.WaitAsync(TimeSpan.FromSeconds(30), cancel.Token);

            Assert.True(session.CanEditSettings);

            // The write leaves, reaches the daemon, and is still inside it when the pipe goes.
            var applying = session.Settings!.ApplyAsync(SettingsSessionTests.Document(180));
            await arrived.Task.WaitAsync(TimeSpan.FromSeconds(30), cancel.Token);

            using var second = new Daemon(name, () => live, Write, info);
            var reconnected = Reconnected(session);
            first.Drop();
            await applying;

            await reconnected.WaitAsync(TimeSpan.FromSeconds(30), cancel.Token);
            await second.Served.WaitAsync(TimeSpan.FromSeconds(30), cancel.Token);

            Assert.False(session.CanEditSettings,
                "the daemon is still holding a write that would replace whatever is applied now");
            Assert.Contains("never confirmed", session.SettingsProblem);
        }
        finally
        {
            held.TrySetResult();
            client.AutoReconnect = false;
            cancel.Cancel();
            first.Dispose();
        }
    }

    /// <summary>
    /// Completes once the session has finished initializing over a connection.
    /// </summary>
    /// <remarks>
    /// The session's own event, not the server accepting (#923). A server that has started answering
    /// says nothing about whether the client has published its channel yet, and an
    /// <c>InitializeAsync</c> that arrives first captures incarnation 0 and returns having done nothing
    /// — leaving the test to assert against a session that was never ready. Subscribed before the
    /// connect it is waiting for, including before the drop, because the client reconnects from inside
    /// its own disconnect handler.
    /// </remarks>
    private static Task Reconnected(OtdSession session)
    {
        var back = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Connected += _ => back.TrySetResult();
        return back.Task;
    }

    /// <summary>A JSON-RPC daemon on a named pipe, serving the three methods a session needs.</summary>
    private sealed class Daemon : IDisposable
    {
        private readonly NamedPipeServerStream _pipe;
        private readonly Func<Settings> _get;
        private readonly Func<Settings, Task> _set;
        private readonly AppInfo _info;
        private JsonRpc? _rpc;

        private readonly TaskCompletionSource _served =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes once this daemon has accepted a connection and started answering.</summary>
        public Task Served => _served.Task;

        public Daemon(string name, Func<Settings> get, Func<Settings, Task> set, AppInfo info)
        {
            // Two instances, because the replacement listens while the first is still connected — which
            // is what lets the client's own reconnect find it immediately instead of retrying on a timer.
            _pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 2, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            _get = get;
            _set = set;
            _info = info;
            _ = AcceptAsync();
        }

        private async Task AcceptAsync()
        {
            try { await _pipe.WaitForConnectionAsync(); }
            catch (Exception) { return; }   // disposed before anything connected

            var rpc = new JsonRpc(_pipe);
            rpc.AddLocalRpcMethod("GetSettings", new Func<Settings>(_get));
            rpc.AddLocalRpcMethod("SetSettings", new Func<Settings, Task>(_set));
            rpc.AddLocalRpcMethod("GetApplicationInfo", new Func<AppInfo>(() => _info));
            _rpc = rpc;
            rpc.StartListening();
            _served.TrySetResult();
        }

        /// <summary>Drops the connection without ending the method the daemon is inside.</summary>
        public void Drop() => _pipe.Disconnect();

        public void Dispose()
        {
            try { _rpc?.Dispose(); } catch { /* already torn down with the pipe */ }
            _pipe.Dispose();
        }
    }
}
