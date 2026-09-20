using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

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
        using var client = new DaemonClient(NullOtdLog.Instance, pipe);
        var channel = (IDaemonSettingsChannel)client;

        var first = await ConnectOnceAsync(client, channel, pipe);
        var second = await ConnectOnceAsync(client, channel, pipe);

        Assert.NotEqual(0, first);
        Assert.NotEqual(0, second);
        Assert.NotEqual(first, second);
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
        using var client = new DaemonClient(NullOtdLog.Instance, pipe);
        var channel = (IDaemonSettingsChannel)client;

        var live = await ConnectOnceAsync(client, channel, pipe);

        Assert.NotEqual(0, live);
        await WaitUntil(() => channel.Incarnation == 0);
        Assert.Equal(0, channel.Incarnation);
    }

    /// <summary>
    /// Accepts one connection on <paramref name="pipe"/>, waits for the client to establish a channel,
    /// then drops it — and reports the identity the channel had while it was up.
    /// </summary>
    /// <remarks>
    /// <c>AutoReconnect</c> is turned off before the drop so the client does not immediately race to
    /// re-establish and move the number under the next assertion.
    /// </remarks>
    private static async Task<int> ConnectOnceAsync(DaemonClient client, IDaemonSettingsChannel channel,
        string pipe)
    {
        using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        var accepted = server.WaitForConnectionAsync();
        _ = client.ConnectAsync(CancellationToken.None);
        await accepted.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        await WaitUntil(() => channel.Incarnation != 0);
        var identity = channel.Incarnation;

        client.AutoReconnect = false;
        server.Disconnect();
        await WaitUntil(() => channel.Incarnation == 0);

        return identity;
    }

    /// <summary>
    /// Polls until <paramref name="reached"/> holds, or fails the test.
    /// </summary>
    /// <remarks>
    /// Polling rather than a completion source because the state being waited for is set inside
    /// <c>JsonRpc</c>'s own disconnect handling, which this test has no hook into. The bound is a failure
    /// bound, not the synchronisation.
    /// </remarks>
    private static async Task WaitUntil(Func<bool> reached)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!reached() && DateTime.UtcNow < deadline)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        Assert.True(reached(), "the client never reached the expected channel state");
    }
}
