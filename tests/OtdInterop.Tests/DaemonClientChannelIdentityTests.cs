using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using OtdInterop;
using Xunit;

namespace OtdInterop.Tests;

/// <summary>
/// Channel identity, checked against the real <c>DaemonClient</c> over real named pipes.
/// </summary>
///
/// <remarks>
/// <para>
/// Every other test of this behaviour runs against <c>FakeDaemonTransport</c>, which keeps its own
/// monotonic counter — so it asserts the guarantee rather than checks it. That is how the defect this
/// file exists for survived: the client derived each channel's number from the previous channel, a
/// disconnect cleared the channel, and the next connection therefore reused the number the last one had.
/// A drop and reconnect went 1, 0, 1, and every "is this still the channel I had?" check answered yes for
/// two different channels.
/// </para>
/// <para>
/// Nothing here contacts a running OpenTabletDriver. The client is pointed at a pipe this test creates,
/// with a name unique to the run, and the "daemon" is a bare <see cref="NamedPipeServerStream"/> that
/// accepts a connection and then drops it.
/// </para>
/// </remarks>
public class DaemonClientChannelIdentityTests
{
    /// <summary>
    /// Two connections separated by a drop carry different identities.
    /// </summary>
    /// <remarks>
    /// The assertion is inequality, not any particular pair of numbers. What the rest of the library
    /// needs is that an identity captured before a drop cannot match one taken after it; which values
    /// they are is this client's business.
    /// </remarks>
    [Fact]
    public async Task AChannelIdentityIsNotReusedAfterADisconnect()
    {
        var pipe = $"ota-test-{Guid.NewGuid():N}";
        var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var client = new DaemonClient(NullOtdLog.Instance, pipe);
        try
        {
            var channel = (IDaemonSettingsChannel)client;

            var first = await ConnectOnceAsync(client, channel, pipe, cancel.Token);
            var second = await ConnectOnceAsync(client, channel, pipe, cancel.Token);

            Assert.NotEqual(0, first);
            Assert.NotEqual(0, second);
            Assert.NotEqual(first, second);
        }
        finally { StopConnecting(client, cancel); }
    }

    /// <summary>A dropped connection reports no channel at all, so an identity of 0 means "none".</summary>
    /// <remarks>
    /// The rest of the library reads 0 as "there is nothing connected" and refuses work on that basis, so
    /// it matters that a drop actually produces it rather than leaving the last number in place.
    /// </remarks>
    [Fact]
    public async Task ADroppedChannelReportsNoIdentity()
    {
        var pipe = $"ota-test-{Guid.NewGuid():N}";
        var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var client = new DaemonClient(NullOtdLog.Instance, pipe);
        try
        {
            var channel = (IDaemonSettingsChannel)client;

            var live = await ConnectOnceAsync(client, channel, pipe, cancel.Token);

            // ConnectOnceAsync returns only once Disconnected has been raised, and the client clears its
            // channel before raising it.
            Assert.NotEqual(0, live);
            Assert.Equal(0, channel.Incarnation);
        }
        finally { StopConnecting(client, cancel); }
    }

    /// <summary>
    /// A disposed client reports no connection, rather than throwing on a disposed channel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Against the real client over a real pipe, because that is where the defect was: every send guards
    /// on "no channel" — the state a <em>disconnect</em> leaves — while disposal disposed the channel and
    /// kept the reference. A disposed channel passes that guard, so the call reached StreamJsonRpc and
    /// threw, where <see cref="IDaemonCapabilities.GetAppInfoAsync"/> promises null.
    /// </para>
    /// <para>
    /// I said in review that this could not be unit-tested because the client is internal and needs a
    /// real connection. Both halves were wrong: this file already connects it to a pipe of its own, and
    /// the tests one class away do it without any OpenTabletDriver process.
    /// </para>
    /// <para>
    /// <b>What it does not settle, checked rather than guessed.</b> Disposing the channel also makes
    /// StreamJsonRpc raise its disconnect, whose handler clears the same reference. With the clearing
    /// reverted this still passes — five runs out of five — because that handler gets there first. So it
    /// pins the behaviour and is <em>not</em> mutation coverage of the reference-clearing; turning that
    /// race into a sleep would be worse than leaving it unclaimed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ADisposedClientReportsNoConnection_RatherThanThrowing()
    {
        var pipe = $"ota-test-{Guid.NewGuid():N}";
        var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var client = new DaemonClient(NullOtdLog.Instance, pipe);

        using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnConnected() => connected.TrySetResult();
        client.Connected += OnConnected;

        try
        {
            var accepted = server.WaitForConnectionAsync(cancel.Token);
            _ = client.ConnectAsync(cancel.Token);
            await accepted;
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(30), cancel.Token);

            // Disposed while still connected -- no disconnect has happened, so the reference is live.
            client.AutoReconnect = false;
            client.Dispose();

            Assert.Null(await client.GetAppInfoAsync());
            Assert.Empty(await client.GetTabletsAsync());
            Assert.Empty(await client.GetDevicesAsync());
            Assert.Empty(await client.GetCurrentLogAsync());
            Assert.False(await client.UninstallPluginAsync("anywhere"));
        }
        finally
        {
            client.Connected -= OnConnected;
            StopConnecting(client, cancel);
        }
    }

    /// <summary>
    /// Stops the client trying to connect, then disposes both it and the token source.
    /// </summary>
    /// <remarks>
    /// <b>Disposing a CancellationTokenSource does not cancel its token.</b> Leaving cleanup to a
    /// <c>using</c> meant that a wait failing before the 60-second timer fired would dispose the source,
    /// remove the pending cancellation, and leave the client's reconnect loop running with nothing left
    /// to stop it. Cancelling explicitly, and clearing AutoReconnect first, is what the documentation
    /// claimed was already happening.
    /// </remarks>
    private static void StopConnecting(DaemonClient client, CancellationTokenSource cancel)
    {
        client.AutoReconnect = false;
        cancel.Cancel();
        client.Dispose();
        cancel.Dispose();
    }

    /// <summary>
    /// Accepts one connection on <paramref name="pipe"/>, waits for the client to establish a channel,
    /// then drops it — and reports the identity the channel had while it was up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Waits on the client's own <c>Connected</c> and <c>Disconnected</c> events rather than polling for
    /// the channel value. Both are raised by the client after it has finished the corresponding state
    /// change, so this cannot mistake a channel that has been published for a connection that is
    /// established — which polling for a non-zero incarnation would.
    /// </para>
    /// <para>
    /// <c>AutoReconnect</c> is turned off before the drop so the client does not immediately race to
    /// re-establish and move the number under the next assertion. That means this covers a deliberate
    /// stop and not the automatic reconnect path; production does the same thing around a stop the user
    /// asked for. The caller cancels the token in cleanup, which is what stops a retry loop outliving a
    /// failed test — the timer alone does not, since disposing the source removes it.
    /// </para>
    /// </remarks>
    private static async Task<int> ConnectOnceAsync(DaemonClient client, IDaemonSettingsChannel channel,
        string pipe, CancellationToken ct)
    {
        using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dropped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnConnected() => connected.TrySetResult();
        void OnDisconnected() => dropped.TrySetResult();

        client.Connected += OnConnected;
        client.Disconnected += OnDisconnected;
        try
        {
            var accepted = server.WaitForConnectionAsync(ct);
            _ = client.ConnectAsync(ct);
            await accepted;
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);

            var identity = channel.Incarnation;

            client.AutoReconnect = false;
            server.Disconnect();
            await dropped.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);

            return identity;
        }
        finally
        {
            client.Connected -= OnConnected;
            client.Disconnected -= OnDisconnected;
        }
    }
}
