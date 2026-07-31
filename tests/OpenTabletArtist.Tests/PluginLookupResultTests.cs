using System;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class PluginLookupResultTests
{
    [Fact]
    public void FromException_NetworkKinds_MapToNetworkFailure()
    {
        Assert.Equal(PluginLookupStatus.NetworkFailure, PluginLookupResult.FromException(new HttpRequestException("no route")).Status);
        Assert.Equal(PluginLookupStatus.NetworkFailure, PluginLookupResult.FromException(new SocketException()).Status);
        Assert.Equal(PluginLookupStatus.NetworkFailure, PluginLookupResult.FromException(new TaskCanceledException()).Status);
        Assert.Equal(PluginLookupStatus.NetworkFailure, PluginLookupResult.FromException(new TimeoutException()).Status);
    }

    [Fact]
    public void FromException_UnwrapsInnerNetworkException()
    {
        // Download failures often surface wrapped (e.g. an IOException wrapping a SocketException).
        var wrapped = new IOException("stream broke", new SocketException());
        Assert.Equal(PluginLookupStatus.NetworkFailure, PluginLookupResult.FromException(wrapped).Status);
    }

    [Fact]
    public void FromException_ContentKinds_MapToParseFailure()
    {
        Assert.Equal(PluginLookupStatus.ParseFailure, PluginLookupResult.FromException(new JsonReaderException("bad json")).Status);
        Assert.Equal(PluginLookupStatus.ParseFailure, PluginLookupResult.FromException(new InvalidOperationException()).Status);
        // A plain IOException (no network inner) is a content/read problem, not a reachability one.
        Assert.Equal(PluginLookupStatus.ParseFailure, PluginLookupResult.FromException(new IOException("corrupt archive")).Status);
    }

    [Fact]
    public void Failure_Flags_And_Messages()
    {
        Assert.True(PluginLookupResult.NetworkFailure.IsFailure);
        Assert.True(PluginLookupResult.ParseFailure.IsFailure);
        // "Reached it, nothing compatible" is NOT a failure — it's an honest up-to-date-ish outcome.
        Assert.False(PluginLookupResult.NoCompatibleRelease.IsFailure);
        Assert.False(PluginLookupResult.NoCompatibleRelease.IsSuccess);

        Assert.NotEmpty(PluginLookupResult.NetworkFailure.StatusMessage);
        Assert.NotEmpty(PluginLookupResult.ParseFailure.StatusMessage);
        Assert.NotEmpty(PluginLookupResult.NoCompatibleRelease.StatusMessage);
    }
}
